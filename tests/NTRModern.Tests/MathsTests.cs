namespace NTRModern.Tests;

public class MathsTests
{
    private const uint DIVCNT = 0x04000280;
    private const uint DIV_NUMER = 0x04000290;
    private const uint DIV_DENOM = 0x04000298;
    private const uint DIV_RESULT = 0x040002A0;
    private const uint DIVREM_RESULT = 0x040002A8;
    private const uint SQRTCNT = 0x040002B0;
    private const uint SQRT_RESULT = 0x040002B4;
    private const uint SQRT_PARAM = 0x040002B8;
    private const uint DivisionByZero = 1 << 14;
    private const uint Busy = 1 << 15;

    public MathsTests() => Hardware.Reset(Model.DS);

    private static void Write64(uint address, ulong value)
    {
        ARM9.IO.Write32(address, (uint)value);
        ARM9.IO.Write32(address + 4, (uint)(value >> 32));
    }

    private static ulong Read64(uint address) => ARM9.IO.Read32(address) | ((ulong)ARM9.IO.Read32(address + 4) << 32);

    private static (ulong Result, ulong Remainder) Divide(uint mode, long numerator, long denominator)
    {
        ARM9.IO.Write32(DIVCNT, mode);
        Write64(DIV_NUMER, (ulong)numerator);
        Write64(DIV_DENOM, (ulong)denominator);
        return (Read64(DIV_RESULT), Read64(DIVREM_RESULT));
    }

    [Fact]
    public void A32BitDivisionSignExpandsItsResults()
    {
        Assert.Equal((0xFFFFFFFFFFFFFFFCul, 0ul), Divide(0, -8, 2));
    }

    [Fact]
    public void Division32BitModeOnlyUsesTheLowerWords()
    {
        Assert.Equal((2ul, 1ul), Divide(0, 0x1_00000007, 3));
    }

    [Fact]
    public void DividingByZeroGivesTheNumeratorAndOneOppositeToItsSign()
    {
        Assert.Equal((1ul, 0xFFFFFFFFFFFFFFFBul), Divide(2, -5, 0));
        Assert.Equal(DivisionByZero, ARM9.IO.Read32(DIVCNT) & DivisionByZero);
    }

    [Fact]
    public void A32BitOverflowInvertsTheUpperWordOfTheResult()
    {
        Assert.Equal((0x00000000FFFFFFFFul, 5ul), Divide(0, 5, 0));
        Assert.Equal((0x0000000080000000ul, 0ul), Divide(0, int.MinValue, -1));
    }

    [Fact]
    public void A64BitOverflowGivesTheMostNegativeNumber()
    {
        Assert.Equal((0x8000000000000000ul, 0ul), Divide(2, long.MinValue, -1));
    }

    [Fact]
    public void TheDivisionByZeroFlagChecksAllOfTheDenominator()
    {
        Divide(0, 5, 0x1_00000000);

        Assert.Equal(0u, ARM9.IO.Read32(DIVCNT) & DivisionByZero);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(3u)]
    public void Modes1And3DivideBy32Bits(uint mode)
    {
        Assert.Equal((0x80000000ul, 0ul), Divide(mode, 0x1_00000000, 0x1_00000002));
    }

    [Theory]
    [InlineData(0u, 18)]
    [InlineData(1u, 34)]
    [InlineData(2u, 34)]
    public void DivisionIsBusyForItsExecutionTime(uint mode, long busCycles)
    {
        Divide(mode, 100, 7);

        ARM9.Scheduler.Cycles += (busCycles * SystemClock.BusCycle) - 1;
        Assert.Equal(Busy, ARM9.IO.Read32(DIVCNT) & Busy);

        ARM9.Scheduler.Cycles++;
        Assert.Equal(0u, ARM9.IO.Read32(DIVCNT) & Busy);
    }

    [Fact]
    public void SquareRoot32BitModeOnlyUsesTheLowerWord()
    {
        Write64(SQRT_PARAM, 0x1_00000010);

        Assert.Equal(4u, ARM9.IO.Read32(SQRT_RESULT));
    }

    [Fact]
    public void SquareRootRoundsDown()
    {
        ARM9.IO.Write32(SQRTCNT, 1);
        Write64(SQRT_PARAM, ulong.MaxValue);

        Assert.Equal(0xFFFFFFFFu, ARM9.IO.Read32(SQRT_RESULT));

        Write64(SQRT_PARAM, 0xFFFFFFFE00000001);
        Assert.Equal(0xFFFFFFFFu, ARM9.IO.Read32(SQRT_RESULT));

        Write64(SQRT_PARAM, 0xFFFFFFFE00000000);
        Assert.Equal(0xFFFFFFFEu, ARM9.IO.Read32(SQRT_RESULT));
    }

    [Fact]
    public void SquareRootIsBusyFor13BusCycles()
    {
        Write64(SQRT_PARAM, 16);

        ARM9.Scheduler.Cycles += (13 * SystemClock.BusCycle) - 1;
        Assert.Equal(Busy, ARM9.IO.Read32(SQRTCNT) & Busy);

        ARM9.Scheduler.Cycles++;
        Assert.Equal(0u, ARM9.IO.Read32(SQRTCNT) & Busy);
    }
}
