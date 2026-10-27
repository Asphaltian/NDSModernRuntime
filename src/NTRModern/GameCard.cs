using System.Buffers.Binary;

namespace NTRModern;

internal static class GameCard
{
    public const uint AUXSPICNT = 0x1A0;
    public const uint ROMCTRL = 0x1A4;
    public const uint CommandOut = 0x1A8;
    public const uint Seeds = 0x1B0;
    public const uint SeedsEnd = 0x1BC;
    public const uint DataIn = 0x100010;

    private const ushort SPIHoldChipselect = 1 << 6;
    private const ushort SPIBusy = 1 << 7;
    private const ushort SerialMode = 1 << 13;
    private const ushort TransferReadyIRQ = 1 << 14;
    private const ushort SlotEnable = 1 << 15;
    private const ushort AUXSPICNTWritable = 0xE043;
    private const ushort ARM7SlotAccess = 1 << 11;
    private const uint Gap1Length = 0x1FFF;
    private const uint KEY2ApplySeed = 1u << 15;
    private const uint DataWordReady = 1u << 23;
    private const uint SlowCLK = 1u << 27;
    private const uint ReleaseReset = 1u << 29;
    private const uint WriteDirection = 1u << 30;
    private const uint BlockStart = 1u << 31;
    private const uint ROMCTRLStored = ~(KEY2ApplySeed | DataWordReady | BlockStart);
    private const uint ROMCTRLAtBoot = ReleaseReset;
    private const int FourByteBlock = 7;
    private const int Block200h = 1;
    private const int CommandBytes = 8;
    private const byte GetData = 0xB7;
    private const byte GetChipID = 0xB8;
    private const uint FirstDataAddress = 0x8000;
    private const uint BlockWrap = 0x1000;
    private const uint RedirectMask = 0x1FF;
    private const int HeaderCapacity = 0x14;
    private const int HeaderUnitCode = 0x12;
    private const int HeaderROMCTRLData = 0x60;
    private const int HeaderROMCTRLSecure = 0x64;
    private const byte Macronix = 0xC2;
    private const uint DSiUnitCode = 1 << 1;
    private const uint DSiChipFlag = 1u << 30;
    private const uint NewProtocolChipFlag = 1u << 31;

    private static readonly long[] BaudRates = [4_000_000, 2_000_000, 1_000_000, 512_000];
    private static readonly uint[] ROMControls = new uint[2];
    private static readonly byte[] Command = new byte[CommandBytes];
    private static readonly SystemClock.Event WordReady = SystemClock.CreateEvent(OnWordReady);

    private static ushort _control;
    private static ushort _writtenControl;
    private static byte _data;
    private static bool _isDataKnown;
    private static long _busyUntil;
    private static byte[] _rom = [];
    private static uint _capacity;
    private static Processor _transferOwner;
    private static uint _address;
    private static int _position;
    private static int _length;
    private static long _cyclesPerByte;
    private static long _readyAt;
    private static uint _word;
    private static bool _isWordReady;

    public static uint ChipID { get; private set; }

    public static uint? Read(Processor processor, uint word, uint mask) => word switch
    {
        AUXSPICNT => ReadAUXSPI(processor, mask),
        ROMCTRL => ReadROMCTRL(processor),
        CommandOut or CommandOut + 4 or (>= Seeds and < SeedsEnd) => throw new NotSupportedException($"The {processor} read the write-only game card register at 0x{0x04000000 + word:X8}."),
        DataIn => ReadData(processor),
        _ => null,
    };

    public static bool Write(Processor processor, uint word, uint value, uint mask)
    {
        switch (word)
        {
            case AUXSPICNT:
                WriteAUXSPI(processor, value, mask);
                return true;
            case ROMCTRL:
                WriteROMCTRL(processor, value, mask);
                return true;
            case CommandOut or CommandOut + 4:
                CheckAccess(processor);
                for (int index = 0; index < 4; index++)
                {
                    if ((mask & (0xFFu << (index * 8))) != 0)
                    {
                        Command[(int)(word - CommandOut) + index] = (byte)(value >> (index * 8));
                    }
                }

                return true;
            case >= Seeds and < SeedsEnd:
                CheckAccess(processor);
                return true;
            case DataIn:
                throw new NotSupportedException($"The {processor} wrote the game card data port, which isn't supported.");
            default:
                return false;
        }
    }

    public static void Insert(byte[] rom)
    {
        ulong capacity = 0x20000UL << rom[HeaderCapacity];
        if (capacity is < 0x100000 or > 0x8000000)
        {
            throw new ArgumentException($"The cartridge header says the ROM chip holds 0x{capacity:X} bytes, which isn't supported.");
        }

        _rom = rom;
        _capacity = (uint)capacity;
        ChipID = Macronix | (((_capacity >> 20) - 1) << 8) | ProtocolFlag(rom) | ((rom[HeaderUnitCode] & DSiUnitCode) != 0 ? DSiChipFlag : 0);
    }

    public static void Reset()
    {
        (_control, _writtenControl, _data, _isDataKnown, _busyUntil) = (0, 0, 0, false, 0);
        Array.Fill(ROMControls, ROMCTRLAtBoot);
        Array.Clear(Command);
        (_address, _position, _length, _cyclesPerByte, _readyAt, _word, _isWordReady) = (0, 0, 0, 0, 0, 0, false);
        WordReady.Cancel();
        SaveMemory.Deselect();
    }

    private static uint ProtocolFlag(byte[] rom)
    {
        uint data = BinaryPrimitives.ReadUInt32LittleEndian(rom.AsSpan(HeaderROMCTRLData));
        uint secure = BinaryPrimitives.ReadUInt32LittleEndian(rom.AsSpan(HeaderROMCTRLSecure));
        return (data, secure) switch
        {
            (0x00586000, 0x001808F8) => 0,
            (0x00416657, 0x081808F8) => NewProtocolChipFlag,
            _ => throw new ArgumentException($"The cartridge header has ROMCTRL values 0x{data:X8} and 0x{secure:X8}, which aren't supported."),
        };
    }

    private static uint ReadAUXSPI(Processor processor, uint mask)
    {
        CheckAccess(processor);
        uint value = 0;
        if ((mask & 0xFFFF) != 0)
        {
            if ((mask & AUXSPICNTWritable & ~_writtenControl) != 0)
            {
                throw new NotSupportedException($"The {processor} read AUXSPICNT before writing it.");
            }

            value |= (uint)(_control | (SystemClock.CyclesOf(processor) < _busyUntil ? SPIBusy : 0));
        }

        if ((mask & 0xFFFF0000) != 0)
        {
            value |= _isDataKnown
                ? (uint)_data << 16
                : throw new NotSupportedException($"The {processor} read AUXSPIDATA before any transfer.");
        }

        return value;
    }

    private static void WriteAUXSPI(Processor processor, uint value, uint mask)
    {
        CheckAccess(processor);
        if ((mask & 0xFFFF) != 0)
        {
            _control = (ushort)(IORegisters.Merge(_control, value, mask) & AUXSPICNTWritable);
            _writtenControl |= (ushort)mask;
        }

        if ((mask & 0x00FF0000) != 0)
        {
            TransferSPI(processor, (byte)(value >> 16));
        }
    }

    private static void TransferSPI(Processor processor, byte value)
    {
        if ((_control & (SlotEnable | SerialMode)) != (SlotEnable | SerialMode))
        {
            throw new NotSupportedException($"The {processor} wrote AUXSPIDATA with AUXSPICNT set to 0x{_control:X4}.");
        }

        long now = SystemClock.CyclesOf(processor);
        if (now < _busyUntil)
        {
            throw new NotSupportedException($"The {processor} wrote AUXSPIDATA while the last transfer was still busy.");
        }

        _data = SaveMemory.Transfer(value);
        _isDataKnown = true;
        _busyUntil = now + (8 * SystemClock.CyclesPerSecond / BaudRates[_control & 3]);
        if ((_control & SPIHoldChipselect) == 0)
        {
            SaveMemory.Deselect();
        }
    }

    private static uint ReadROMCTRL(Processor processor)
    {
        CheckAccess(processor);
        return ROMControls[(int)processor] | (_isWordReady && _transferOwner == processor ? DataWordReady : 0);
    }

    private static void WriteROMCTRL(Processor processor, uint value, uint mask)
    {
        CheckAccess(processor);
        ref uint control = ref ROMControls[(int)processor];
        if ((control & BlockStart) != 0)
        {
            throw new NotSupportedException($"The {processor} wrote ROMCTRL during a game card transfer.");
        }

        control = (IORegisters.Merge(control, value, mask) & ROMCTRLStored) | (control & ReleaseReset);
        if ((value & mask & BlockStart) != 0)
        {
            control |= BlockStart;
            StartTransfer(processor, control);
        }
    }

    private static void StartTransfer(Processor processor, uint control)
    {
        if ((_control & (SlotEnable | SerialMode)) != SlotEnable)
        {
            throw new NotSupportedException($"The {processor} started a game card ROM transfer with AUXSPICNT set to 0x{_control:X4}.");
        }

        if ((control & (ReleaseReset | WriteDirection)) != ReleaseReset)
        {
            throw new NotSupportedException($"The {processor} started a game card transfer with ROMCTRL 0x{control:X8}, which isn't supported.");
        }

        _length = (int)((control >> 24) & 7) switch
        {
            FourByteBlock => 4,
            Block200h => 0x200,
            int size => throw new NotSupportedException($"The {processor} started a game card transfer with block size {size}, which isn't supported."),
        };

        switch (Command[0])
        {
            case GetData:
                _address = BinaryPrimitives.ReadUInt32BigEndian(Command.AsSpan(1));
                break;
            case GetChipID:
                break;
            default:
                throw new NotSupportedException($"The {processor} sent game card command 0x{Command[0]:X2}, which isn't supported.");
        }

        _transferOwner = processor;
        _position = 0;
        _cyclesPerByte = ((control & SlowCLK) != 0 ? 8 : 5) * SystemClock.BusCycle;
        long firstWord = CommandBytes + (control & Gap1Length) + 4;
        WordReady.ScheduleAt(SystemClock.CyclesOf(processor) + (firstWord * _cyclesPerByte));
    }

    private static void OnWordReady(long dueAt)
    {
        _word = 0;
        for (int index = 0; index < 4; index++)
        {
            _word |= (uint)DataByte(_position + index) << (index * 8);
        }

        _readyAt = dueAt;
        _isWordReady = true;
        Hardware.DMAOf(_transferOwner).OnCardWordReady();
    }

    private static byte DataByte(int offset)
    {
        if (Command[0] == GetChipID)
        {
            return (byte)(ChipID >> ((offset & 3) * 8));
        }

        uint address = (_address & ~(BlockWrap - 1)) | ((_address + (uint)offset) & (BlockWrap - 1));
        address &= _capacity - 1;
        if (address < FirstDataAddress)
        {
            address = FirstDataAddress + (address & RedirectMask);
        }

        return address < _rom.Length
            ? _rom[address]
            : throw new NotSupportedException($"The game read ROM address 0x{address:X8}, which is past the end of the ROM image.");
    }

    private static uint ReadData(Processor processor)
    {
        CheckAccess(processor);
        if (!_isWordReady)
        {
            throw new NotSupportedException($"The {processor} read the game card data port with no word ready.");
        }

        _isWordReady = false;
        _position += 4;
        if (_position < _length)
        {
            WordReady.ScheduleAt(Math.Max(SystemClock.CyclesOf(processor), _readyAt) + (4 * _cyclesPerByte));
        }
        else
        {
            ROMControls[(int)_transferOwner] &= ~BlockStart;
            if ((_control & TransferReadyIRQ) != 0)
            {
                Hardware.InterruptsOf(_transferOwner).Raise(Interrupt.CardTransferComplete);
            }
        }

        return _word;
    }

    private static void CheckAccess(Processor processor)
    {
        var owner = (ARM9.IO.ExternalMemoryControl & ARM7SlotAccess) != 0 ? Processor.ARM7 : Processor.ARM9;
        if (processor != owner)
        {
            throw new NotSupportedException($"The {processor} used the game card registers while the slot belonged to the {owner}.");
        }
    }
}
