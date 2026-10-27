namespace NTRModern.Tests;

public class InterruptsTests
{
    private const uint IME = 0x04000208;
    private const uint IE = 0x04000210;
    private const uint IF = 0x04000214;

    public InterruptsTests() => Hardware.Reset(Model.DS);

    [Fact]
    public void WritingOnesToIFAcknowledgesThoseInterrupts()
    {
        ARM9.IO.Interrupts.Raise(Interrupt.Timer0 | Interrupt.IPCSync);
        ARM9.IO.Write32(IF, (uint)Interrupt.Timer0);

        Assert.Equal((uint)Interrupt.IPCSync, ARM9.IO.Read32(IF));
    }

    [Fact]
    public void OnlyBit0OfIMEIsKept()
    {
        ARM9.IO.Write32(IME, 0xFFFFFFFF);

        Assert.Equal(1u, ARM9.IO.Read32(IME));
    }

    [Fact]
    public void EachCPUHasItsOwnInterrupts()
    {
        ARM9.IO.Write32(IE, (uint)Interrupt.VBlank);
        ARM7.IO.Interrupts.Raise(Interrupt.VBlank);

        Assert.Equal(0u, ARM7.IO.Read32(IE));
        Assert.Equal(0u, ARM9.IO.Read32(IF));
    }

    [Fact]
    public void APendingInterruptStopsRecompiledCodeAtItsNextCheck()
    {
        ARM9.Scheduler.Cycles = 100;
        ARM9.IO.Write32(IE, (uint)Interrupt.Timer0);
        ARM9.IO.Interrupts.Raise(Interrupt.Timer0);
        Assert.Equal(long.MaxValue, ARM9.Scheduler.NextEvent);

        ARM9.IO.Write32(IME, 1);
        Assert.Equal(100, ARM9.Scheduler.NextEvent);
    }
}
