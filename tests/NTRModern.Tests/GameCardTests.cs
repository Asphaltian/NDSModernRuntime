namespace NTRModern.Tests;

public class GameCardTests
{
    private const uint AUXSPICNT = 0x040001A0;
    private const uint AUXSPIDATA = 0x040001A2;
    private const uint EXMEMCNT = 0x04000204;
    private const ushort ARM7SlotAccess = 1 << 11;
    private const ushort SlotEnable = 1 << 15;
    private const ushort SerialMode = 1 << 13;
    private const ushort Hold = 1 << 6;
    private const ushort Busy = 1 << 7;
    private const byte WRSR = 0x01;
    private const byte WR = 0x02;
    private const byte RD = 0x03;
    private const byte RDSR = 0x05;
    private const byte WREN = 0x06;
    private const byte WRHI = 0x0A;
    private const byte RDHI = 0x0B;

    public GameCardTests()
    {
        Hardware.Reset(Model.DS);
        SaveMemory.Initialize(SaveType.EEPROM128K);
        ARM9.IO.Write16(EXMEMCNT, ARM7SlotAccess);
    }

    private static byte Transfer(byte value, bool hold = true)
    {
        ARM7.IO.Write16(AUXSPICNT, (ushort)(SlotEnable | SerialMode | (hold ? Hold : 0)));
        ARM7.IO.Write16(AUXSPIDATA, value);
        ARM7.Scheduler.Cycles += 1000;
        return (byte)ARM7.IO.Read16(AUXSPIDATA);
    }

    private static byte[] Send(params byte[] bytes) => [.. bytes.Select((value, index) => Transfer(value, hold: index < bytes.Length - 1))];

    [Fact]
    public void WrittenBytesReadBack()
    {
        Send(WREN);
        Send(WR, 0x01, 0x23, 0x40, 0xAA, 0xBB);

        Assert.Equal([0xAA, 0xBB], Send(RD, 0x01, 0x23, 0x40, 0, 0)[4..]);
        Assert.True(SaveMemory.IsModified);
    }

    [Fact]
    public void WRENSetsTheLatchAndAWriteClearsIt()
    {
        Assert.Equal(0x00, Send(RDSR, 0)[1]);

        Send(WREN);
        Assert.Equal(0x02, Send(RDSR, 0)[1]);

        Send(WR, 0, 0, 0, 0x55);
        Assert.Equal(0x00, Send(RDSR, 0)[1]);
    }

    [Fact]
    public void WritingNeedsWREN()
    {
        Assert.Throws<NotSupportedException>(() => Send(WR, 0, 0, 0, 0x55));
    }

    [Fact]
    public void AWriteCantCrossItsPage()
    {
        Send(WREN);

        Assert.Throws<NotSupportedException>(() => Send(WR, 0, 0, 0xFF, 1, 2));
    }

    [Fact]
    public void AddressesPastTheEndMirror()
    {
        Send(WREN);
        Send(WR, 0, 0, 5, 0x77);

        Assert.Equal(0x77, Send(RD, 0x02, 0x00, 0x05, 0)[4]);
    }

    [Fact]
    public void The512ByteEEPROMHasHalvesAndOnesInItsStatus()
    {
        SaveMemory.Initialize(SaveType.EEPROM512);

        Send(WREN);
        Send(WRHI, 0x10, 0x99);

        Assert.Equal(0x99, Send(RDHI, 0x10, 0)[2]);
        Assert.Equal(0xFF, Send(RD, 0x10, 0)[2]);
        Assert.Equal(0xF0, Send(RDSR, 0)[1]);
    }

    [Fact]
    public void WriteProtectionIsntKept()
    {
        Send(WREN);

        Assert.Throws<NotSupportedException>(() => Send(WRSR, 0x0C));
    }

    [Fact]
    public void NoSaveChipReadsFF()
    {
        SaveMemory.Initialize(SaveType.None);

        Assert.Equal([0xFF, 0xFF], Send(RDSR, 0));
    }

    [Fact]
    public void OnlyTheSlotsOwnerCanUseIt()
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write16(AUXSPICNT, SlotEnable));
    }

    [Fact]
    public void ATransferKeepsTheBusBusyForEightBits()
    {
        ARM7.IO.Write16(AUXSPICNT, SlotEnable | SerialMode);
        ARM7.IO.Write16(AUXSPIDATA, RDSR);

        long eightBitsAt4MHz = 8 * SystemClock.CyclesPerSecond / 4_000_000;
        ARM7.Scheduler.Cycles += eightBitsAt4MHz - 1;
        Assert.Equal(Busy, ARM7.IO.Read16(AUXSPICNT) & Busy);

        ARM7.Scheduler.Cycles += 1;
        Assert.Equal(0, ARM7.IO.Read16(AUXSPICNT) & Busy);
    }

    [Fact]
    public void TheRegistersHaveNoKnownValueAtBoot()
    {
        Assert.Throws<NotSupportedException>(() => ARM7.IO.Read16(AUXSPICNT));
        Assert.Throws<NotSupportedException>(() => ARM7.IO.Read16(AUXSPIDATA));
    }
}
