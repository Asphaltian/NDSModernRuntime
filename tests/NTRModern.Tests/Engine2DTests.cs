namespace NTRModern.Tests;

public class Engine2DTests
{
    private const uint DISPCNT = 0x04000000;
    private const uint VCOUNT = 0x04000006;
    private const uint BG0CNT = 0x04000008;
    private const uint BG0HOFS = 0x04000010;
    private const uint WININ = 0x04000048;
    private const uint NotUsed = 0x0400004E;
    private const uint DISP3DCNT = 0x04000060;
    private const uint DISPCAPCNT = 0x04000064;
    private const uint MASTER_BRIGHT = 0x0400006C;
    private const uint EngineB = 0x1000;
    private const uint POWCNT1 = 0x04000304;
    private const ushort EngineAPower = 1 << 1;

    public Engine2DTests() => Hardware.Reset(Model.DS);

    [Fact]
    public void TheEnginesHaveTheirOwnRegisters()
    {
        ARM9.IO.Write16(BG0CNT, 0x1234);
        ARM9.IO.Write16(BG0CNT + EngineB, 0x5678);

        Assert.Equal((0x1234, 0x5678), (ARM9.IO.Read16(BG0CNT), ARM9.IO.Read16(BG0CNT + EngineB)));
    }

    [Fact]
    public void BGControlUsesAllSixteenBitsOnTheDS()
    {
        ARM9.IO.Write16(BG0CNT, 0xFFFF);

        Assert.Equal(0xFFFF, ARM9.IO.Read16(BG0CNT));
    }

    [Fact]
    public void UnusedBitsReadAsZero()
    {
        ARM9.IO.Write16(WININ, 0xFFFF);
        ARM9.IO.Write16(DISP3DCNT, 0xFFFF);

        Assert.Equal(0x3F3F, ARM9.IO.Read16(WININ));
        Assert.Equal(0x4FFF, ARM9.IO.Read16(DISP3DCNT));
    }

    [Fact]
    public void UndefinedPortsReadAsZero()
    {
        ARM9.IO.Write16(NotUsed, 0xFFFF);
        ARM9.IO.Write32(DISP3DCNT + EngineB, 0xFFFFFFFF);

        Assert.Equal(0, ARM9.IO.Read16(NotUsed));
        Assert.Equal(0u, ARM9.IO.Read32(DISP3DCNT + EngineB));
    }

    [Fact]
    public void WriteOnlyRegistersCantBeRead()
    {
        ARM9.IO.Write16(BG0HOFS, 1);
        ARM9.IO.Write16(MASTER_BRIGHT, 0x4010);

        Assert.Throws<NotSupportedException>(() => ARM9.IO.Read16(BG0HOFS));
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Read16(MASTER_BRIGHT));
    }

    [Fact]
    public void RegistersHaveNoKnownValueAtBoot()
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Read32(DISPCNT));

        ARM9.IO.Write8(DISPCNT, 0x05);

        Assert.Equal(0x05, ARM9.IO.Read8(DISPCNT));
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Read16(DISPCNT));
    }

    [Fact]
    public void EngineBRejectsEngineAOnlyDISPCNTBits()
    {
        ARM9.IO.Write32(DISPCNT + EngineB, 0xC0B1FFD5);

        Assert.Equal(0xC0B1FFD5, ARM9.IO.Read32(DISPCNT + EngineB));
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write32(DISPCNT + EngineB, 1 << 3));
    }

    [Fact]
    public void APoweredOffEnginesPortsAreReadOnly()
    {
        ARM9.IO.Write16(BG0CNT, 0x1111);

        ARM9.IO.Write16(POWCNT1, (ushort)(ARM9.IO.Read16(POWCNT1) & ~EngineAPower));
        ARM9.IO.Write16(BG0CNT, 0x2222);
        ARM9.IO.Write32(DISPCNT, 0x00010000);

        Assert.Equal(0x1111, ARM9.IO.Read16(BG0CNT));
        Assert.Equal(0x00010000u, ARM9.IO.Read32(DISPCNT));
    }

    [Theory]
    [InlineData(0u, 7u)]
    [InlineData(EngineB, 6u)]
    [InlineData(EngineB, 7u)]
    public void ReservedBGModesThrow(uint engine, uint mode)
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write32(DISPCNT + engine, 0x00010000 | mode));
    }

    [Fact]
    public void TheReservedBitmapOBJMappingThrows()
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write32(DISPCNT, 0x00010060));
    }

    [Fact]
    public void MainMemoryDisplayIsntSupported()
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write32(DISPCNT, 0x00030000));
    }

    [Fact]
    public void CaptureIsntSupported()
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write32(DISPCAPCNT, 1u << 31));
    }

    [Fact]
    public void VCOUNTWritesArentTakenForEngineRegisters()
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write16(VCOUNT, 202));
    }
}
