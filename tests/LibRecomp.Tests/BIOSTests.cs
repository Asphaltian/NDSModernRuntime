using NTRModern;

namespace LibRecomp.Tests;

public class BIOSTests
{
    private const uint IRQMode = 0x12;
    private const uint SystemMode = 0x1F;
    private const uint ARM9Handler = 0x02000100;
    private const uint ARM7Handler = 0x02380100;
    private const uint DTCM = 0x027E0000;
    private const uint ARM9CheckFlags = DTCM + 0x3FF8;
    private const uint ARM7CheckFlags = 0x0380FFF8;
    private const uint IPCSYNC = 0x04000180;
    private const uint IME = 0x04000208;
    private const uint IE = 0x04000210;
    private const uint IF = 0x04000214;
    private const ushort SyncIRQEnable = 1 << 14;
    private const ushort SyncSendIRQ = 1 << 13;

    private readonly List<(Processor Processor, uint CPSR, uint R14)> _handled = [];

    public BIOSTests()
    {
        Hardware.Reset(Model.DS);
        NTRModern.ARM9.CP15.Write(0, 9, 1, 0, DTCM | 0x0A);
        NTRModern.ARM9.CP15.Write(0, 1, 0, 0, 1 << 16);
        NTRModern.ARM9.Memory.Write32(DTCM + 0x3FFC, ARM9Handler);
        NTRModern.ARM7.Memory.Write32(0x0380FFFC, ARM7Handler);
        CPU.ARM9.Functions.Register([(0x00004000, ARM9Handler, 0x100, [(0x0, HandleIRQ)])], []);
        CPU.ARM7.Functions.Register([(0x00008000, ARM7Handler, 0x100, [(0x0, HandleIRQ)])], []);
        (NTRModern.ARM9.Scheduler.Cycles, NTRModern.ARM7.Scheduler.Cycles) = (0, 0);
    }

    private void HandleIRQ(RecompContext ctx)
    {
        _handled.Add((ctx.Processor, ctx.CPSR, ctx.R14));
        var cpu = ctx.Processor == Processor.ARM9 ? CPU.ARM9 : CPU.ARM7;
        uint checkFlags = ctx.Processor == Processor.ARM9 ? ARM9CheckFlags : ARM7CheckFlags;
        uint flags = cpu.Read32(IF);
        cpu.Write32(IF, flags);
        cpu.Write32(checkFlags, cpu.Read32(checkFlags) | flags);
        (ctx.R0, ctx.R1, ctx.R2, ctx.R3, ctx.R12, ctx.R14) = (0, 0, 0, 0, 0, 0);
    }

    private static RecompContext CreateARM9Context(uint r0, uint r1)
    {
        var ctx = new RecompContext(Processor.ARM9) { R0 = r0, R1 = r1, R2 = 2, R3 = 3, R12 = 12, R13 = DTCM + 0x3E00, R14 = 14 };
        ctx.SetBankedR13(IRQMode, DTCM + 0x3F80);
        NTRModern.ARM9.IO.Write32(IE, (uint)Interrupt.IPCSync);
        NTRModern.ARM9.IO.Write16(IPCSYNC, SyncIRQEnable);
        return ctx;
    }

    private static void RunUntil(Processor processor, long cycles)
    {
        ref long now = ref SystemClock.CyclesOf(processor);
        while (now < cycles)
        {
            now += 100;
            if (now >= SystemClock.NextEventOf(processor))
            {
                SystemClock.Synchronize(processor);
            }
        }
    }

    private static void InterruptARM9At(long cycles)
    {
        RunUntil(Processor.ARM7, cycles);
        NTRModern.ARM7.IO.Write16(IPCSYNC, SyncSendIRQ);
    }

    [Theory]
    [InlineData(Processor.ARM9, 0x01)]
    [InlineData(Processor.ARM9, 0x07)]
    [InlineData(Processor.ARM9, 0x1A)]
    [InlineData(Processor.ARM7, 0x01)]
    [InlineData(Processor.ARM7, 0x16)]
    [InlineData(Processor.ARM7, 0x20)]
    public void ANumberThatIsNoBIOSFunctionThrows(Processor processor, int function)
    {
        var cpu = processor == Processor.ARM9 ? CPU.ARM9 : CPU.ARM7;

        Assert.Throws<InvalidOperationException>(() => BIOS.Call(cpu, new RecompContext(processor), function));
    }

    [Theory]
    [InlineData(Processor.ARM9, 0x00)]
    [InlineData(Processor.ARM7, 0x07)]
    public void AFunctionThatIsNotSupportedYetSaysSo(Processor processor, int function)
    {
        var cpu = processor == Processor.ARM9 ? CPU.ARM9 : CPU.ARM7;

        Assert.Throws<NotSupportedException>(() => BIOS.Call(cpu, new RecompContext(processor), function));
    }

    [Fact]
    public void TheARM7HaltPassesThroughWithIMEOff()
    {
        NTRModern.ARM7.IO.Write32(IE, (uint)Interrupt.Timer0);
        NTRModern.ARM7.IO.Interrupts.Raise(Interrupt.Timer0);

        BIOS.Call(CPU.ARM7, new RecompContext(Processor.ARM7), 0x06);

        Assert.Equal(0u, NTRModern.ARM7.IO.Read32(IME));
    }

    [Fact]
    public void IntrWaitRunsTheIRQHandlerAndReturnsOnceItReportsTheInterrupt()
    {
        var ctx = CreateARM9Context(1, (uint)Interrupt.IPCSync);
        long returnedAt = -1;

        SystemClock.Run(
            () =>
            {
                BIOS.Call(CPU.ARM9, ctx, 0x04);
                returnedAt = NTRModern.ARM9.Scheduler.Cycles;
            },
            () => InterruptARM9At(5000));

        Assert.Equal([(Processor.ARM9, 0x80 | IRQMode, 0xFFFF0018u)], _handled);
        Assert.True(returnedAt >= 5000);
        Assert.Equal((1u, (uint)Interrupt.IPCSync, 2u, 3u, 12u, 14u), (ctx.R0, ctx.R1, ctx.R2, ctx.R3, ctx.R12, ctx.R14));
        Assert.Equal((DTCM + 0x3E00, SystemMode), (ctx.R13, ctx.CPSR));
        Assert.Equal(0u, NTRModern.ARM9.Memory.Read32(ARM9CheckFlags));
    }

    [Fact]
    public void TheARM9IntrWaitWaitsForAnInterruptEvenIfTheFlagWasAlreadySet()
    {
        var ctx = CreateARM9Context(0, (uint)Interrupt.IPCSync);
        NTRModern.ARM9.Memory.Write32(ARM9CheckFlags, (uint)Interrupt.IPCSync);
        long returnedAt = -1;

        SystemClock.Run(
            () =>
            {
                BIOS.Call(CPU.ARM9, ctx, 0x04);
                returnedAt = NTRModern.ARM9.Scheduler.Cycles;
            },
            () => InterruptARM9At(5000));

        Assert.Single(_handled);
        Assert.True(returnedAt >= 5000);
    }

    [Fact]
    public void TheARM7IntrWaitReturnsAtOnceIfTheFlagWasAlreadySet()
    {
        var ctx = new RecompContext(Processor.ARM7) { R0 = 0, R1 = (uint)Interrupt.IPCSync };
        NTRModern.ARM7.Memory.Write32(ARM7CheckFlags, (uint)(Interrupt.IPCSync | Interrupt.Timer0));

        BIOS.Call(CPU.ARM7, ctx, 0x04);

        Assert.Empty(_handled);
        Assert.Equal(1u, NTRModern.ARM7.IO.Read32(IME));
        Assert.Equal((uint)Interrupt.Timer0, NTRModern.ARM7.Memory.Read32(ARM7CheckFlags));
    }

    [Fact]
    public void VBlankIntrWaitSetsR0AndR1ToOneAndWaitsForTheNextVBlank()
    {
        var ctx = new RecompContext(Processor.ARM7) { R13 = 0x0380FD80 };
        ctx.SetBankedR13(IRQMode, 0x0380FF80);
        NTRModern.ARM7.IO.Write32(IE, (uint)Interrupt.VBlank);
        NTRModern.ARM7.Memory.Write32(ARM7CheckFlags, (uint)Interrupt.VBlank);
        (NTRModern.ARM9.Scheduler.Cycles, NTRModern.ARM7.Scheduler.Cycles) = (0, 0);
        SystemClock.CreateEvent(_ => NTRModern.ARM7.IO.Interrupts.Raise(Interrupt.VBlank)).ScheduleAt(5000);
        long returnedAt = -1;

        SystemClock.Run(
            () => RunUntil(Processor.ARM9, 10000),
            () =>
            {
                BIOS.Call(CPU.ARM7, ctx, 0x05);
                returnedAt = NTRModern.ARM7.Scheduler.Cycles;
            });

        Assert.Equal([(Processor.ARM7, 0x80 | IRQMode, 0x00000018u)], _handled);
        Assert.Equal((1u, 1u), (ctx.R0, ctx.R1));
        Assert.True(returnedAt >= 5000);
    }
}
