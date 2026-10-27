namespace NTRModern.Tests;

public class PowerTests
{
    private const uint WIFIWAITCNT = 0x04000206;
    private const uint POWCNT2 = 0x04000304;
    private const ushort WifiOn = 1 << 1;
    private const uint POWCNT1 = 0x04000304;
    private const ushort EngineB = 1 << 9;
    private const uint EngineAPalette = 0x05000000;
    private const uint EngineBPalette = 0x05000400;

    public PowerTests() => Hardware.Reset(Model.DS);

    [Fact]
    public void OnlyTheSpeakersArePoweredAtFirst()
    {
        Assert.Equal(1, ARM7.IO.Read16(POWCNT2));
    }

    [Fact]
    public void WIFIWAITCNTHasTheFirmwaresSettingOnceWifiIsPowered()
    {
        ARM7.IO.Write16(POWCNT2, 1 | WifiOn);

        Assert.Equal(0x30, ARM7.IO.Read16(WIFIWAITCNT));
    }

    [Fact]
    public void WIFIWAITCNTIsOutOfReachWithWifiOff()
    {
        Assert.Throws<NotSupportedException>(() => ARM7.IO.Read16(WIFIWAITCNT));
    }

    [Fact]
    public void POWCNT1KeepsOnlyItsUsedBits()
    {
        ARM9.IO.Write16(POWCNT1, 0xFFFF);

        Assert.Equal(0x820F, ARM9.IO.Read16(POWCNT1));
    }

    [Fact]
    public void APoweredOffEnginesPaletteReadsZeroAndIgnoresWrites()
    {
        ARM9.Memory.Write16(EngineAPalette, 0x1234);

        ARM9.IO.Write16(POWCNT1, 0x820F & ~EngineB);
        ARM9.Memory.Write16(EngineBPalette, 0x7FFF);

        Assert.Equal(0x1234, ARM9.Memory.Read16(EngineAPalette));
        Assert.Equal(0, ARM9.Memory.Read16(EngineBPalette));
    }
}
