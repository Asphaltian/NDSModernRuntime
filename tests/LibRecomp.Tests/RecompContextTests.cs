using NTRModern;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace LibRecomp.Tests;

public class RecompContextTests
{
    private const uint IRQMode = 0x12;
    private const uint SystemMode = 0x1F;
    private const uint StickyOverflow = 1u << 27;

    public RecompContextTests() => Hardware.Reset(Model.DS);

    [Fact]
    public void StartsInSystemMode()
    {
        Assert.Equal(SystemMode, new RecompContext(Processor.ARM9).CPSR);
    }

    [Fact]
    public void EachModeKeepsItsOwnStackPointerAndLinkRegister()
    {
        var ctx = new RecompContext(Processor.ARM7) { R13 = 0x1000, R14 = 0x2000 };

        ctx.WriteCPSR(IRQMode, 0b0001);
        ctx.R13 = 0x3000;
        ctx.R14 = 0x4000;

        ctx.WriteCPSR(SystemMode, 0b0001);
        Assert.Equal((0x1000u, 0x2000u), (ctx.R13, ctx.R14));

        ctx.WriteCPSR(IRQMode, 0b0001);
        Assert.Equal((0x3000u, 0x4000u), (ctx.R13, ctx.R14));
    }

    [Fact]
    public void BankedStackPointersCanBeSetFromAnotherMode()
    {
        var ctx = new RecompContext(Processor.ARM9) { R13 = 0x1000 };

        ctx.SetBankedR13(IRQMode, 0x3000);
        Assert.Equal(0x1000u, ctx.R13);

        ctx.WriteCPSR(IRQMode, 0b0001);
        Assert.Equal(0x3000u, ctx.R13);
    }

    [Fact]
    public void TheFieldMaskSelectsWhatAWriteChanges()
    {
        var ctx = new RecompContext(Processor.ARM7);

        ctx.WriteCPSR(0xF0000000 | IRQMode, 0b1000);
        Assert.True(ctx.N && ctx.Z && ctx.C && ctx.V);
        Assert.Equal(0xF0000000 | SystemMode, ctx.CPSR);

        ctx.WriteCPSR(IRQMode, 0b0001);
        Assert.Equal(0xF0000000 | IRQMode, ctx.CPSR);
    }

    [Theory]
    [InlineData(Processor.ARM9, true)]
    [InlineData(Processor.ARM7, false)]
    public void OnlyTheARM9HasTheStickyOverflowFlag(Processor processor, bool hasQ)
    {
        var ctx = new RecompContext(processor);

        ctx.WriteCPSR(StickyOverflow, 0b1000);
        Assert.Equal(hasQ, ctx.Q);

        ctx.WriteCPSR(IRQMode, 0b0001);
        ctx.WriteSPSR(StickyOverflow, 0b1000);
        Assert.Equal(hasQ ? StickyOverflow : 0, ctx.SPSR & StickyOverflow);
    }

    [Fact]
    public void AnExceptionSavesTheCPSRAndMasksIRQs()
    {
        var ctx = new RecompContext(Processor.ARM9) { Z = true, Q = true };
        Assert.True(ctx.AreIRQsEnabled);

        ctx.EnterException(IRQMode);
        Assert.False(ctx.AreIRQsEnabled);
        Assert.Equal(0x40000000 | StickyOverflow | SystemMode, ctx.SPSR);

        (ctx.Z, ctx.Q) = (false, false);
        ctx.ReturnFromException();
        Assert.True(ctx.AreIRQsEnabled);
        Assert.Equal(0x40000000 | StickyOverflow | SystemMode, ctx.CPSR);
    }

    [Fact]
    public void UserModeCanOnlyChangeTheFlags()
    {
        var ctx = new RecompContext(Processor.ARM7);
        ctx.WriteCPSR(0x10, 0b1001);

        ctx.WriteCPSR(0x80000000 | SystemMode, 0b1001);

        Assert.Equal(0x80000000 | 0x10u, ctx.CPSR);
    }

    [Fact]
    public void FIQModeHasItsOwnR8ToR12()
    {
        var ctx = new RecompContext(Processor.ARM7) { R8 = 8, R12 = 12 };

        ctx.WriteCPSR(0x11, 0b0001);
        Assert.Equal((0u, 0u), (ctx.R8, ctx.R12));
        ctx.R8 = 80;

        ctx.WriteCPSR(SystemMode, 0b0001);
        Assert.Equal((8u, 12u), (ctx.R8, ctx.R12));
    }

    [Fact]
    public void EnablingIRQsWithAnInterruptPendingStopsAtTheNextCheck()
    {
        var ctx = new RecompContext(Processor.ARM7);
        ctx.WriteCPSR(0x80 | SystemMode, 0b0001);
        NTRModern.ARM7.Scheduler.Cycles = 100;
        NTRModern.ARM7.IO.Write32(0x04000208, 1);
        NTRModern.ARM7.IO.Write32(0x04000210, (uint)Interrupt.Timer0);
        NTRModern.ARM7.IO.Interrupts.Raise(Interrupt.Timer0);
        NTRModern.ARM7.Scheduler.NextEvent = long.MaxValue;

        ctx.WriteCPSR(SystemMode, 0b0001);

        Assert.Equal(100, NTRModern.ARM7.Scheduler.NextEvent);
    }
}
