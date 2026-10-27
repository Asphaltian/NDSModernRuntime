using LibRecomp;
using NDSRecomp;
using NTRModern;
using static Recompilation.Tests.RecompiledCode;
using Processor = NTRModern.Processor;

namespace Recompilation.Tests;

public class ARMv5Tests
{
    private const uint BxLr = 0xE12FFF1E;
    private const uint Data = 0x02000100;

    public ARMv5Tests() => Hardware.Reset(Model.DS);

    private static RecompFunc ARM9(uint encoding, string disassembly) => ARM(Processor.ARM9, (encoding, disassembly), (BxLr, "bx lr"));

    private static RecompContext Run(RecompFunc function, uint r0 = 0, uint r1 = 0, uint r2 = 0, uint r3 = 0, bool q = false)
    {
        return RecompiledCode.Run(function, new RecompContext(Processor.ARM9) { R0 = r0, R1 = r1, R2 = r2, R3 = r3, Q = q });
    }

    [Theory]
    [InlineData(0u, 32u)]
    [InlineData(1u, 31u)]
    [InlineData(0x00010000u, 15u)]
    [InlineData(0x80000000u, 0u)]
    public void CountLeadingZeros(uint value, uint zeros)
    {
        Assert.Equal(zeros, Run(ARM9(0xE16F0F11, "clz r0, r1"), r1: value).R0);
    }

    [Fact]
    public void SaturatingAdditionClampsAndSetsTheStickyFlag()
    {
        var function = ARM9(0xE1020051, "qadd r0, r1, r2");

        var ctx = Run(function, r1: 0x7FFFFFFF, r2: 1);
        Assert.Equal((0x7FFFFFFFu, true), (ctx.R0, ctx.Q));

        ctx = Run(function, r1: 1, r2: 2);
        Assert.Equal((3u, false), (ctx.R0, ctx.Q));

        Assert.True(Run(function, r1: 1, r2: 2, q: true).Q);
    }

    [Fact]
    public void SaturatingSubtractionClampsToTheMostNegativeNumber()
    {
        var ctx = Run(ARM9(0xE1220051, "qsub r0, r1, r2"), r1: 0x80000000, r2: 1);

        Assert.Equal((0x80000000u, true), (ctx.R0, ctx.Q));
    }

    [Fact]
    public void DoublingSaturatesEvenWhenTheResultWouldFit()
    {
        var ctx = Run(ARM9(0xE1420051, "qdadd r0, r1, r2"), r1: unchecked((uint)-5), r2: 0x40000000);

        Assert.Equal((0x7FFFFFFAu, true), (ctx.R0, ctx.Q));
    }

    [Fact]
    public void DoubledSubtractionSaturatesTheResult()
    {
        var ctx = Run(ARM9(0xE1620051, "qdsub r0, r1, r2"), r1: 0, r2: 0xC0000000);

        Assert.Equal((0x7FFFFFFFu, true), (ctx.R0, ctx.Q));
    }

    [Fact]
    public void HalfwordMultipliesPickTheirHalves()
    {
        var ctx = Run(ARM9(0xE16002A1, "smultb r0, r1, r2"), r1: 0xFFFE0000, r2: 0x00070003);

        Assert.Equal(unchecked((uint)-6), ctx.R0);
    }

    [Fact]
    public void AnOverflowingAccumulateWrapsAndSetsTheStickyFlag()
    {
        var ctx = Run(ARM9(0xE1003281, "smlabb r0, r1, r2, r3"), r1: 1, r2: 1, r3: 0x7FFFFFFF);

        Assert.Equal((0x80000000u, true), (ctx.R0, ctx.Q));
    }

    [Fact]
    public void WordByHalfwordMultipliesKeepTheTop32BitsOfTheProduct()
    {
        var ctx = Run(ARM9(0xE12002A1, "smulwb r0, r1, r2"), r1: 0x00030000, r2: 0x0000FFFF);
        Assert.Equal(unchecked((uint)-3), ctx.R0);

        ctx = Run(ARM(Processor.ARM9, (0xE12032C1, "smlawt r0, r1, r2, r3"), (BxLr, "bx lr")), r1: 0x00010000, r2: 0x00020000, r3: 0x7FFFFFFF);
        Assert.Equal((0x80000001u, true), (ctx.R0, ctx.Q));
    }

    [Fact]
    public void LongHalfwordAccumulateAddsIntoBothWordsWithoutTheStickyFlag()
    {
        var ctx = Run(ARM9(0xE1410382, "smlalbb r0, r1, r2, r3"), r0: 0xFFFFFFFF, r1: 0, r2: 2, r3: 3);

        Assert.Equal((5u, 1u, false), (ctx.R0, ctx.R1, ctx.Q));
    }

    [Fact]
    public void DoublewordsGoThroughMemoryAsTwoWords()
    {
        var function = ARM(Processor.ARM9, (0xE1C020F8, "strd r2, [r0, #0x8]"), (0xE1C040D8, "ldrd r4, [r0, #0x8]"), (BxLr, "bx lr"));

        var ctx = Run(function, r0: Data, r2: 0x11111111, r3: 0x22222222);

        Assert.Equal((0x11111111u, 0x22222222u), (ctx.R4, ctx.R5));
        Assert.Equal(0x22222222u, NTRModern.ARM9.Memory.Read32(Data + 12));
    }

    [Theory]
    [InlineData(Processor.ARM9, Data + 8)]
    [InlineData(Processor.ARM7, 0xAAAAu)]
    public void LoadMultipleWritesBackItsListedBaseOnlyOnTheARM9(Processor processor, uint r0)
    {
        NTRModern.ARM9.Memory.Write32(Data, 0xAAAA);
        NTRModern.ARM9.Memory.Write32(Data + 4, 0xBBBB);
        var function = ARM(processor, (0xE8B00003, "ldmia r0!, {r0, r1}"), (BxLr, "bx lr"));

        var ctx = RecompiledCode.Run(function, new RecompContext(processor) { R0 = Data });

        Assert.Equal((r0, 0xBBBBu), (ctx.R0, ctx.R1));
    }

    [Theory]
    [InlineData(Processor.ARM9, Data)]
    [InlineData(Processor.ARM7, Data + 8)]
    public void StoreMultipleStoresTheOldBaseOnTheARM9(Processor processor, uint stored)
    {
        var function = ARM(processor, (0xE8A10003, "stmia r1!, {r0, r1}"), (BxLr, "bx lr"));

        var ctx = RecompiledCode.Run(function, new RecompContext(processor) { R0 = 5, R1 = Data });

        Assert.Equal(Data + 8, ctx.R1);
        Assert.Equal(stored, NTRModern.ARM9.Memory.Read32(Data + 4));
    }

    [Fact]
    public void CoprocessorRegistersAreReadAndWritten()
    {
        Assert.Equal(0x41059461u, Run(ARM9(0xEE100F10, "mrc p15, 0, r0, c0, c0, 0")).R0);

        var function = ARM(Processor.ARM9, (0xEE090F11, "mcr p15, 0, r0, c9, c1, 0"), (0xEE190F11, "mrc p15, 0, r0, c9, c1, 0"), (BxLr, "bx lr"));
        Assert.Equal(0x027E000Au, Run(function, r0: 0x027E000A).R0);
    }

    [Fact]
    public void BranchWithLinkAndExchangeCallsAThumbFunctionThatReturnsWithPop()
    {
        byte[] code =
        [
            .. ARMBytes(
                0xE52DE004, // 00: push {lr}
                0xE12FFF31, // 04: blx r1
                0xE49DF004), // 08: pop {pc}
            .. ThumbBytes(
                0xB500, // 0C: push {lr}
                0x2009, // 0E: movs r0, #9
                0xBD00), // 10: pop {pc}
        ];
        var funcs = RecompileProgram(
            Processor.ARM9,
            code,
            Address,
            new Symbol(Address, 12, "Caller", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 12, 6, "Callee", IsThumb: true, Section: 0, IsFunction: true));

        var ctx = RecompiledCode.Run(Function(funcs, "Caller"), new RecompContext(Processor.ARM9) { R1 = Address + 13, R13 = Stack });

        Assert.Equal((9u, Stack), (ctx.R0, ctx.R13));
    }

    [Fact]
    public void AJumpWithTheLinkRegisterChangedCarriesOnWhereItPoints()
    {
        byte[] code = ARMBytes(
            0xE52DE004, // 00: push {lr}
            0xE51FE000, // 04: ldr lr, =0x02000018
            0xE12FFF11, // 08: bx r1
            Address + 0x18,
            0xE3A00005, // 10: mov r0, #5
            0xE12FFF1E, // 14: bx lr
            0xE2800001, // 18: add r0, r0, #1
            0xE49DF004); // 1C: pop {pc}
        var funcs = RecompileProgram(
            Processor.ARM9,
            code,
            Address,
            new Symbol(Address, 0x10, "Start", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0x10, 8, "Callee", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0x18, 8, "Continue", IsThumb: false, Section: 0, IsFunction: true));

        var ctx = RecompiledCode.Run(Function(funcs, "Start"), new RecompContext(Processor.ARM9) { R1 = Address + 0x10, R13 = Stack });

        Assert.True(LibRecomp.ARM9.Recomp.TryResumeAt(ReturnAddress));
        Assert.Equal((6u, Stack), (ctx.R0, ctx.R13));
    }

    [Theory]
    [InlineData(5u, 0u)]
    [InlineData(1u, 4u)]
    [InlineData(0u, 8u)]
    public void AJumpIntoUnrolledCodeRunsFromWhereItLands(uint input, uint result)
    {
        uint[] code =
        [
            0xE3A01000, // 00: mov r1, #0
            0xE3A0200C, // 04: mov r2, #12
            0xE3500001, // 08: cmp r0, #1
            0xD2422004, // 0C: suble r2, r2, #4
            0xE3500000, // 10: cmp r0, #0
            0xD2422004, // 14: suble r2, r2, #4
            0xE08FF102, // 18: add pc, pc, r2, lsl #2
            0xE1A00000, // 1C: nop
            .. Enumerable.Repeat(0xE2811001u, 12), // 20: add r1, r1, #1
            0xE1A00001, // 50: mov r0, r1
            BxLr, // 54: bx lr
        ];
        var function = Recompile(
            Processor.ARM9,
            ARMBytes(code),
            isThumb: false,
            [
                "mov r1, #0x0", "mov r2, #0xc", "cmp r0, #0x1", "suble r2, r2, #0x4", "cmp r0, #0x0", "suble r2, r2, #0x4", "add pc, pc, r2, lsl #2",
                .. Enumerable.Repeat("add r1, r1, #0x1", 8), "mov r0, r1", "bx lr",
            ]);

        Assert.Equal(result, Run(function, r0: input).R0);
    }

    [Fact]
    public void AnSWIThatIsNoBIOSFunctionThrows()
    {
        var function = Thumb(Processor.ARM9, (0xDF01, "swi 0x1"), (0x4770, "bx lr"));

        Assert.Throws<InvalidOperationException>(() => RecompiledCode.Run(function, new RecompContext(Processor.ARM9)));
    }
}
