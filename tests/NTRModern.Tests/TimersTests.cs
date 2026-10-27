namespace NTRModern.Tests;

public class TimersTests
{
    private const uint TM0CNT_L = 0x04000100;
    private const uint TM1CNT_L = 0x04000104;
    private const uint IF = 0x04000214;

    public TimersTests() => Hardware.Reset(Model.DS);

    private static void Advance(long cycles)
    {
        ARM9.Scheduler.Cycles += cycles;
        ARM7.Scheduler.Cycles += cycles;
        SystemClock.Synchronize(Processor.ARM9);
    }

    [Fact]
    public void ARunningTimerCountsBusCyclesFromItsReloadValue()
    {
        ARM9.IO.Write16(TM0CNT_L, 0xFF00);
        ARM9.IO.Write16(TM0CNT_L + 2, 0x0080);

        Advance(0x80 * SystemClock.BusCycle);
        Assert.Equal(0xFF80, ARM9.IO.Read16(TM0CNT_L));

        ARM9.IO.Write16(TM0CNT_L + 2, 0);
        Advance(0x80 * SystemClock.BusCycle);
        Assert.Equal(0xFF80, ARM9.IO.Read16(TM0CNT_L));
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 256)]
    [InlineData(3, 1024)]
    public void ThePrescalerDividesTheBusClock(ushort prescaler, long busCyclesPerTick)
    {
        ARM7.IO.Write16(TM0CNT_L + 2, (ushort)(0x0080 | prescaler));

        Advance((3 * busCyclesPerTick * SystemClock.BusCycle) - 1);
        Assert.Equal(2, ARM7.IO.Read16(TM0CNT_L));

        Advance(1);
        Assert.Equal(3, ARM7.IO.Read16(TM0CNT_L));
    }

    [Fact]
    public void AnOverflowReloadsTheTimerAndRequestsItsInterrupt()
    {
        ARM9.IO.Write16(TM0CNT_L, 0xFFFE);
        ARM9.IO.Write16(TM0CNT_L + 2, 0x00C0);

        Advance(2 * SystemClock.BusCycle);

        Assert.Equal((uint)Interrupt.Timer0, ARM9.IO.Read32(IF));
        Assert.Equal(0xFFFE, ARM9.IO.Read16(TM0CNT_L));
        Assert.Equal(0u, ARM7.IO.Read32(IF));
    }

    [Fact]
    public void ACountUpTimerCountsOverflowsOfTheTimerBeforeIt()
    {
        ARM9.IO.Write16(TM0CNT_L, 0xFFFF);
        ARM9.IO.Write16(TM1CNT_L + 2, 0x0084);
        ARM9.IO.Write16(TM0CNT_L + 2, 0x0080);

        Advance(SystemClock.BusCycle);
        Assert.Equal(1, ARM9.IO.Read16(TM1CNT_L));

        Advance(SystemClock.BusCycle);
        Assert.Equal(2, ARM9.IO.Read16(TM1CNT_L));
    }

    [Fact]
    public void Timer0CantCountUp()
    {
        ARM9.IO.Write16(TM0CNT_L + 2, 0x0084);

        Assert.Equal(0x0080, ARM9.IO.Read16(TM0CNT_L + 2));
    }

    [Fact]
    public void AByteWriteKeepsTheOtherByteOfTheReloadValue()
    {
        ARM9.IO.Write16(TM0CNT_L, 0xFF00);
        ARM9.IO.Write8(TM0CNT_L, 0x80);
        ARM9.IO.Write16(TM0CNT_L + 2, 0x0080);

        Assert.Equal(0xFF80, ARM9.IO.Read16(TM0CNT_L));
    }
}
