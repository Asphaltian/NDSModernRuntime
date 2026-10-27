namespace NTRModern.ARM7;

internal static class IO
{
    private const uint RCNT = 0x134;
    private const uint RTCRegister = 0x138;
    private const uint SPICNT = 0x1C0;
    private const uint EXMEMSTAT = 0x204;
    private const uint VRAMSTAT = 0x240;
    private const uint POSTFLG = 0x300;
    private const uint POWCNT2 = 0x304;
    private const uint LocalMemoryControl = 0x7F;
    private const ushort SpeakersOn = 1 << 0;
    private const ushort WifiOn = 1 << 1;
    private const ushort POWCNT2Writable = SpeakersOn | WifiOn;
    private const ushort WIFIWAITCNTWritable = 0x3F;
    private const ushort WIFIWAITCNTFromFirmware = 0x30;
    private const ushort RCNTWritable = 0xC1FF;
    private const int Lines = 0xF;
    private const byte PostBootFlag = 1;
    private const byte PowerDownMode = 0xC0;
    private const int HaltMode = 2;

    public static readonly Interrupts Interrupts = new(Processor.ARM7);
    public static readonly Timers Timers = new(Processor.ARM7, Interrupts);
    public static readonly DMA DMA = new(Processor.ARM7, Interrupts);

    private static byte _haltcnt;
    private static ushort _rcnt;
    private static ushort _powcnt2 = SpeakersOn;

    public static ushort ExternalMemoryControl { get; private set; }

    public static ushort WifiWaitControl { get; private set; } = WIFIWAITCNTFromFirmware;

    public static bool IsWifiPowered => (_powcnt2 & WifiOn) != 0;

    public static uint Read32(uint address) => Read(address, 4);

    public static ushort Read16(uint address) => (ushort)Read(address, 2);

    public static byte Read8(uint address) => (byte)Read(address, 1);

    public static void Write32(uint address, uint value) => Write(address, 4, value);

    public static void Write16(uint address, ushort value) => Write(address, 2, value);

    public static void Write8(uint address, byte value) => Write(address, 1, value);

    public static void Reset()
    {
        Interrupts.Reset();
        Timers.Reset();
        DMA.Reset();
        ExternalMemoryControl = 0;
        _haltcnt = 0;
        _rcnt = 0;
        _powcnt2 = SpeakersOn;
        WifiWaitControl = WIFIWAITCNTFromFirmware;
    }

    private static uint Read(uint address, int size)
    {
        var (word, shift, mask) = IORegisters.Locate(address, size);
        uint value = IORegisters.Read(Processor.ARM7, word, size, mask) ?? word switch
        {
            >= Sound.FirstRegister and <= Sound.LastRegister => Sound.Read(word) ?? throw IORegisters.Unsupported(Processor.ARM7, "read", address, size),
            RCNT => ReadRCNT() | ((uint)Keypad.EXTKEYIN << 16),
            RTCRegister when (mask & 0xFFFF0000) == 0 => RTC.Read(),
            SPICNT => SPI.SPICNT | ((uint)SPI.SPIDATA << 16),
            EXMEMSTAT => ExternalMemoryControl | (ARM9.IO.ExternalMemoryControl & ~LocalMemoryControl) | ((mask & 0xFFFF0000) != 0 ? (uint)WIFIWAITCNT(address) << 16 : 0),
            VRAMSTAT when (mask & 0xFFFF0000) == 0 => VRAM.VRAMSTAT | ((uint)SharedWRAM.WRAMCNT << 8),
            POSTFLG when (mask & 0xFFFF0000) == 0 => PostBootFlag | ((uint)_haltcnt << 8),
            POWCNT2 when (mask & 0xFFFF0000) == 0 => _powcnt2,
            _ => throw IORegisters.Unsupported(Processor.ARM7, "read", address, size),
        };

        return value >> shift;
    }

    private static void Write(uint address, int size, uint value)
    {
        var (word, shift, mask) = IORegisters.Locate(address, size);
        value <<= shift;
        if (IORegisters.Write(Processor.ARM7, word, size, value, mask))
        {
            return;
        }

        switch (word)
        {
            case >= Sound.FirstRegister and <= Sound.LastRegister when Sound.Write(word, value, mask):
                break;
            case RCNT when (mask & 0xFFFF0000) == 0:
                _rcnt = (ushort)(IORegisters.Merge(_rcnt, value, mask) & RCNTWritable);
                break;
            case RTCRegister when (mask & 0xFFFF0000) == 0:
                RTC.Write((ushort)IORegisters.Merge(RTC.Read(), value, mask));
                break;
            case SPICNT:
                if ((mask & 0xFFFF) != 0)
                {
                    SPI.WriteSPICNT((ushort)IORegisters.Merge(SPI.SPICNT, value, mask));
                }

                if ((mask & 0x00FF0000) != 0)
                {
                    SPI.WriteSPIDATA((byte)(value >> 16));
                }

                break;
            case EXMEMSTAT:
                if ((mask & 0xFFFF) != 0)
                {
                    ExternalMemoryControl = (ushort)(IORegisters.Merge(ExternalMemoryControl, value, mask) & LocalMemoryControl);
                }

                if ((mask & 0xFFFF0000) != 0)
                {
                    WifiWaitControl = (ushort)((IORegisters.Merge((uint)WIFIWAITCNT(address) << 16, value, mask) >> 16) & WIFIWAITCNTWritable);
                }

                break;
            case POWCNT2 when (mask & 0xFFFF0000) == 0:
                _powcnt2 = (ushort)(IORegisters.Merge(_powcnt2, value, mask) & POWCNT2Writable);
                break;
            case POSTFLG when (mask & 0xFFFF0000) == 0:
                if ((mask & 0xFF00) != 0)
                {
                    WriteHALTCNT((byte)(value >> 8));
                }

                break;
            default:
                throw IORegisters.Unsupported(Processor.ARM7, "write", address, size);
        }
    }

    private static ushort WIFIWAITCNT(uint address)
    {
        return IsWifiPowered
            ? WifiWaitControl
            : throw new NotSupportedException($"The ARM7 accessed WIFIWAITCNT at 0x{address:X8} with wifi power off.");
    }

    private static ushort ReadRCNT()
    {
        int directions = (_rcnt >> 4) & Lines;
        return (ushort)((_rcnt & ~Lines) | (_rcnt & directions) | (Lines & ~directions));
    }

    private static void WriteHALTCNT(byte value)
    {
        _haltcnt = (byte)(value & PowerDownMode);
        switch (value >> 6)
        {
            case 0:
                break;
            case HaltMode:
                SystemClock.Halt(Processor.ARM7, () => Interrupts.IsRequested);
                break;
            default:
                throw new NotSupportedException($"HALTCNT 0x{value:X2} isn't supported.");
        }
    }
}
