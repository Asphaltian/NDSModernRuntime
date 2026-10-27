using System.Buffers.Binary;

namespace NTRModern.Tests;

public class GameCardROMTests
{
    private const uint AUXSPICNT = 0x040001A0;
    private const uint ROMCTRL = 0x040001A4;
    private const uint CommandOut = 0x040001A8;
    private const uint DataIn = 0x04100010;
    private const uint IF = 0x04000214;
    private const uint DMA0SAD = 0x040000B0;
    private const uint Destination = 0x02000000;
    private const ushort SlotEnable = 1 << 15;
    private const ushort TransferReadyIRQ = 1 << 14;
    private const uint DataWordReady = 1u << 23;
    private const uint ReleaseReset = 1u << 29;
    private const uint BlockStart = 1u << 31;
    private const uint Block200h = 1u << 24;
    private const uint FourBytes = 7u << 24;
    private const uint Gap1 = 0x100;
    private const long FastCLK = 5 * SystemClock.BusCycle;

    private static readonly byte[] ROM = CreateROM(capacityShift: 3, data: 0x00586000, secure: 0x001808F8, 0x100000);

    public GameCardROMTests()
    {
        Hardware.Reset(Model.DS);
        GameCard.Insert(ROM);
    }

    private static byte[] CreateROM(byte capacityShift, uint data, uint secure, int size)
    {
        byte[] rom = new byte[size];
        for (int i = 0x1000; i < size; i++)
        {
            rom[i] = (byte)((i * 7) + (i >> 8));
        }

        rom[0x14] = capacityShift;
        BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x60), data);
        BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x64), secure);
        return rom;
    }

    private static void Send(byte command, uint address, uint blockSize)
    {
        ARM9.IO.Write16(AUXSPICNT, SlotEnable | TransferReadyIRQ);
        ARM9.IO.Write8(CommandOut, command);
        ARM9.IO.Write8(CommandOut + 1, (byte)(address >> 24));
        ARM9.IO.Write8(CommandOut + 2, (byte)(address >> 16));
        ARM9.IO.Write8(CommandOut + 3, (byte)(address >> 8));
        ARM9.IO.Write32(CommandOut + 4, (byte)address);
        ARM9.IO.Write32(ROMCTRL, BlockStart | ReleaseReset | blockSize | Gap1);
    }

    private static uint[] ReadWords(int count, List<long>? readyTimes = null)
    {
        uint[] words = new uint[count];
        SystemClock.Run(
            () =>
            {
                for (int i = 0; i < count; i++)
                {
                    while ((ARM9.IO.Read32(ROMCTRL) & DataWordReady) == 0)
                    {
                        Time.Spend(Processor.ARM9, 1);
                    }

                    readyTimes?.Add(ARM9.Scheduler.Cycles);
                    words[i] = ARM9.IO.Read32(DataIn);
                }
            },
            () => { });
        return words;
    }

    private static uint Expected(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(ROM.AsSpan((int)address));

    [Fact]
    public void TheChipIDComesFromTheHeader()
    {
        Assert.Equal(0x000000C2u, GameCard.ChipID);

        GameCard.Insert(CreateROM(capacityShift: 4, data: 0x00416657, secure: 0x081808F8, 0x200000));
        Assert.Equal(0x800001C2u, GameCard.ChipID);
    }

    [Fact]
    public void B7ReadsABlockAfterTheCommandAndGap()
    {
        Send(0xB7, 0x8000, Block200h);
        var readyTimes = new List<long>();

        uint[] words = ReadWords(0x80, readyTimes);

        Assert.Equal(Enumerable.Range(0, 0x80).Select(i => Expected(0x8000 + ((uint)i * 4))), words);
        Assert.Equal((8 + Gap1 + 4) * FastCLK, readyTimes[0]);
        Assert.Equal(0u, ARM9.IO.Read32(ROMCTRL) & BlockStart);
        Assert.Equal((uint)Interrupt.CardTransferComplete, ARM9.IO.Read32(IF));
    }

    [Fact]
    public void AddressesBelow8000hReadFrom8000hOn()
    {
        Send(0xB7, 0x1234, FourBytes);

        Assert.Equal(Expected(0x8034), ReadWords(1)[0]);
    }

    [Fact]
    public void TheStreamWrapsAtFourKilobyteBlocks()
    {
        Send(0xB7, 0x8F00, Block200h);

        Assert.Equal(Expected(0x8000), ReadWords(0x80)[0x40]);
    }

    [Fact]
    public void B8GivesTheChipID()
    {
        Send(0xB8, 0, FourBytes);

        Assert.Equal(GameCard.ChipID, ReadWords(1)[0]);
    }

    [Fact]
    public void CardDMAMovesEachWordAsItArrives()
    {
        ARM9.IO.Write32(DMA0SAD, DataIn);
        ARM9.IO.Write32(DMA0SAD + 4, Destination);
        ARM9.IO.Write32(DMA0SAD + 8, (1u << 31) | (5u << 27) | (1u << 26) | (1u << 25) | (2u << 23) | 1);
        Send(0xB7, 0x9000, Block200h);

        SystemClock.Run(() => Time.Spend(Processor.ARM9, 0x200 * 10 * FastCLK), () => { });

        Assert.Equal(Expected(0x9000), ARM9.Memory.Read32(Destination));
        Assert.Equal(Expected(0x91FC), ARM9.Memory.Read32(Destination + 0x1FC));
    }

    [Fact]
    public void OtherCommandsBreakTheConnection()
    {
        Assert.Throws<NotSupportedException>(() => Send(0x9F, 0, FourBytes));
    }
}
