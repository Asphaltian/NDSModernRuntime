using LibRecomp;
using NDSRecomp;
using NTRModern;
using static Recompilation.Tests.RecompiledCode;
using Processor = NTRModern.Processor;

namespace Recompilation.Tests;

public class TimingTests
{
    private const uint BxLr = 0xE12FFF1E;
    private const uint ARM7WRAM = 0x03800000;

    public TimingTests() => Hardware.Reset(Model.DS);

    private static long Cycles(RecompFunc function, RecompContext ctx)
    {
        ref long cycles = ref SystemClock.CyclesOf(ctx.Processor);
        long before = cycles;
        Run(function, ctx);
        return cycles - before;
    }

    [Theory]
    [InlineData(Processor.ARM7, 2 + 2 + 22)]
    [InlineData(Processor.ARM9, 9 + 9 + 36)]
    public void ThumbCodeInMainMemoryCostsItsFetchesAndTheReturn(Processor processor, long cycles)
    {
        var function = Thumb(processor, (0x2000, "movs r0, #0x0"), (0x4770, "bx lr"));

        Assert.Equal(cycles, Cycles(function, new RecompContext(processor)));
    }

    [Fact]
    public void AStoreMakesItsOwnFetchNonsequential()
    {
        var function = ARM(Processor.ARM7, (0xE5810000, "str r0, [r1]"), (BxLr, "bx lr"));

        Assert.Equal(18 + 2 + 4 + 22, Cycles(function, new RecompContext(Processor.ARM7) { R1 = ARM7WRAM }));
    }

    [Theory]
    [InlineData(true, 4 + 4 + 22)]
    [InlineData(false, 18 + 2 + 4 + 22)]
    public void ASkippedStoreOnlyCostsASequentialFetch(bool z, long cycles)
    {
        var function = ARM(Processor.ARM7, (0x15810000, "strne r0, [r1]"), (BxLr, "bx lr"));

        Assert.Equal(cycles, Cycles(function, new RecompContext(Processor.ARM7) { R1 = ARM7WRAM, Z = z }));
    }

    [Fact]
    public void LoadMultipleReadsAfterTheFirstAreSequential()
    {
        var function = ARM(Processor.ARM7, (0xE890001E, "ldmia r0, {r1-r4}"), (BxLr, "bx lr"));

        Assert.Equal(4 + 2 + 20 + (3 * 4) + 4 + 22, Cycles(function, new RecompContext(Processor.ARM7) { R0 = 0x02000100 }));
    }

    [Theory]
    [InlineData(0x000000FFu, 1)]
    [InlineData(0x00000100u, 2)]
    [InlineData(0x00FFFFFFu, 3)]
    [InlineData(0x01000000u, 4)]
    [InlineData(0xFFFFFF00u, 1)]
    public void MultipliesTakeLongerForBiggerMultipliers(uint multiplier, int m)
    {
        var function = ARM(Processor.ARM7, (0xE0000291, "mul r0, r1, r2"), (BxLr, "bx lr"));

        Assert.Equal(4 + (m * 2) + 4 + 22, Cycles(function, new RecompContext(Processor.ARM7) { R2 = multiplier }));
    }

    [Theory]
    [InlineData(Processor.ARM7, 2 + 2 + 16 + 2 + 2 + 22)]
    [InlineData(Processor.ARM9, 9 + 9 + 18 + 9 + 9 + 36)]
    public void AJumpRefetchesItsTargetAndTheOpcodeAfterIt(Processor processor, long cycles)
    {
        var function = Recompile(
            processor,
            ThumbBytes(
                0x2001, // 00: movs r0, #1
                0xE000, // 02: b 0x06
                0x2002, // 04: movs r0, #2
                0x4770), // 06: bx lr
            isThumb: true,
            "movs r0, #0x1",
            "b 0x02000006",
            "bx lr");

        var ctx = new RecompContext(processor);
        Assert.Equal(cycles, Cycles(function, ctx));
        Assert.Equal(1u, ctx.R0);
    }

    [Fact]
    public void CachedCodeFillsItsLineOnceAndThenHits()
    {
        NTRModern.ARM9.CP15.Write(0, 6, 1, 0, 0x0200002D);
        NTRModern.ARM9.CP15.Write(0, 2, 0, 1, 0x02);
        NTRModern.ARM9.CP15.Write(0, 1, 0, 0, 0x5005);
        var function = Thumb(Processor.ARM9, (0x2000, "movs r0, #0x0"), (0x4770, "bx lr"));

        Assert.Equal(46 + 1 + 36, Cycles(function, new RecompContext(Processor.ARM9)));
        Assert.Equal(1 + 1 + 36, Cycles(function, new RecompContext(Processor.ARM9)));
    }

    [Fact]
    public void CodeInTCMIsFetchedInHalfABusCycle()
    {
        var funcs = RecompileProgram(
            Processor.ARM9,
            ThumbBytes(0x2000, 0x4770),
            0x01FF8000,
            new Symbol(0x01FF8000, 4, "Test", IsThumb: true, Section: 0, IsFunction: true));

        Assert.Equal(1 + 1 + 36, Cycles(Function(funcs, "Test"), new RecompContext(Processor.ARM9)));
    }
}
