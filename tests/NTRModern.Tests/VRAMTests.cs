namespace NTRModern.Tests;

public class VRAMTests
{
    private const uint VRAMCNT_A = 0x04000240;
    private const uint VRAMCNT_B = 0x04000241;
    private const uint VRAMCNT_C = 0x04000242;
    private const uint VRAMCNT_F = 0x04000245;
    private const uint VRAMCNT_H = 0x04000248;
    private const uint VRAMCNT_I = 0x04000249;
    private const byte Enable = 1 << 7;

    public VRAMTests() => Hardware.Reset(Model.DS);

    private static void Map(uint register, int mst, int offset = 0) => ARM9.IO.Write8(register, (byte)(Enable | mst | (offset << 3)));

    [Fact]
    public void ABankKeepsItsContentsWhenMovedFromLCDCToEngineABG()
    {
        Map(VRAMCNT_A, mst: 0);
        ARM9.Memory.Write32(0x06800010, 0x12345678);

        Map(VRAMCNT_A, mst: 1, offset: 2);

        Assert.Equal(0x12345678u, ARM9.Memory.Read32(0x06040010));
        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x06800010));
    }

    [Fact]
    public void EngineBBGTakesBanksCHAndI()
    {
        Map(VRAMCNT_C, mst: 4);
        ARM9.Memory.Write32(0x06200000, 1);
        Map(VRAMCNT_C, mst: 0);
        Map(VRAMCNT_H, mst: 1);
        Map(VRAMCNT_I, mst: 1);
        ARM9.Memory.Write32(0x06200000, 2);
        ARM9.Memory.Write32(0x06208000, 3);

        Assert.Equal(1u, ARM9.Memory.Read32(0x06840000));
        Assert.Equal((2u, 3u), (ARM9.Memory.Read32(0x06200000), ARM9.Memory.Read32(0x06208000)));
    }

    [Fact]
    public void SixteenKilobyteBanksUseBothOffsetBits()
    {
        Map(VRAMCNT_F, mst: 1, offset: 3);
        ARM9.Memory.Write32(0x06014000, 0xABCD);

        Map(VRAMCNT_F, mst: 0);

        Assert.Equal(0xABCDu, ARM9.Memory.Read32(0x06890000));
    }

    [Fact]
    public void OverlappingBanksArentSupported()
    {
        Map(VRAMCNT_A, mst: 1);
        Map(VRAMCNT_B, mst: 1);

        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x06000000));
    }

    [Fact]
    public void AddressesWithNoBankArentSupported()
    {
        Map(VRAMCNT_A, mst: 1);

        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x06020000));
    }

    [Fact]
    public void TextureAndPaletteModesLeaveTheCPUBus()
    {
        Map(VRAMCNT_A, mst: 3);

        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x06800000));
    }

    [Fact]
    public void GBATEKsTableLimitsEachBank()
    {
        Assert.Throws<NotSupportedException>(() => Map(VRAMCNT_H, mst: 3));
        Assert.Throws<NotSupportedException>(() => Map(VRAMCNT_A, mst: 2, offset: 2));
        Assert.Throws<NotSupportedException>(() => Map(VRAMCNT_F, mst: 4, offset: 2));
    }
}
