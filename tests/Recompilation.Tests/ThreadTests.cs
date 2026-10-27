using LibRecomp;
using NDSRecomp;
using NTRModern;
using static Recompilation.Tests.RecompiledCode;
using Processor = NTRModern.Processor;

namespace Recompilation.Tests;

public class ThreadTests
{
    private const uint IRQMode = 0x12;
    private const uint SupervisorMode = 0x13;
    private const uint SystemMode = 0x1F;
    private const uint IRQDisable = 0x80;
    private const uint DTCM = 0x027E0000;
    private const uint ContextA = 0x02001000;
    private const uint ContextB = 0x02001100;
    private const uint Flag = 0x02001200;
    private const uint MainSP = 0x02001300;
    private const uint TaskSP = 0x02001304;
    private const uint StackB = 0x02080000;
    private const uint SupervisorStackB = 0x02090000;

    public ThreadTests()
    {
        Hardware.Reset(Model.DS);
        CPU.ARM9.Threads.Reset();
    }

    private static readonly byte[] Program = ARMBytes(
        0xE92D4010, // 00: push {r4, lr}
        0xE59F001C, // 04: ldr r0, =ContextA
        0xEB000011, // 08: bl Save
        0xE3500000, // 0C: cmp r0, #0
        0x1A000001, // 10: bne 0x1C
        0xE59F0010, // 14: ldr r0, =ContextB
        0xEB00001A, // 18: bl Load
        0xE59F100C, // 1C: ldr r1, =Flag
        0xE5910000, // 20: ldr r0, [r1]
        0xE8BD8010, // 24: pop {r4, pc}
        ContextA,
        ContextB,
        Flag,
        0xE59F1010, // 34: ldr r1, =Flag
        0xE3A02007, // 38: mov r2, #7
        0xE5812000, // 3C: str r2, [r1]
        0xE59F0008, // 40: ldr r0, =ContextA
        0xEB00000F, // 44: bl Load
        0xEAFFFFF9, // 48: b 0x34
        Flag,
        ContextA,
        0xE2801000, // 54: add r1, r0, #0
        0xE10F2000, // 58: mrs r2, cpsr
        0xE4812004, // 5C: str r2, [r1], #4
        0xE3A000D3, // 60: mov r0, #0xD3
        0xE121F000, // 64: msr cpsr_c, r0
        0xE581D040, // 68: str sp, [r1, #0x40]
        0xE121F002, // 6C: msr cpsr_c, r2
        0xE3A00001, // 70: mov r0, #1
        0xE8817FFF, // 74: stmia r1, {r0-lr}
        0xE28F0008, // 78: add r0, pc, #8
        0xE581003C, // 7C: str r0, [r1, #0x3C]
        0xE3A00000, // 80: mov r0, #0
        0xE12FFF1E, // 84: bx lr
        0xE10F1000, // 88: mrs r1, cpsr
        0xE3C1101F, // 8C: bic r1, r1, #0x1F
        0xE38110D3, // 90: orr r1, r1, #0xD3
        0xE121F001, // 94: msr cpsr_c, r1
        0xE4901004, // 98: ldr r1, [r0], #4
        0xE16FF001, // 9C: msr spsr_fsxc, r1
        0xE590D040, // A0: ldr sp, [r0, #0x40]
        0xE590E03C, // A4: ldr lr, [r0, #0x3C]
        0xE8D07FFF, // A8: ldmia r0, {r0-lr}^
        0xE1A00000, // AC: nop
        0xE25EF004, // B0: subs pc, lr, #4
        0xE59F100C, // B4: ldr r1, =Flag
        0xE5910000, // B8: ldr r0, [r1]
        0xE3500000, // BC: cmp r0, #0
        0x0AFFFFFC, // C0: beq 0xB8
        0xE12FFF1E, // C4: bx lr
        Flag,
        0xE52DE004, // CC: push {lr}
        0xE3A0C301, // D0: mov r12, #0x04000000
        0xE59C1214, // D4: ldr r1, [r12, #0x214]
        0xE58C1214, // D8: str r1, [r12, #0x214]
        0xE59F1050, // DC: ldr r1, =ContextB
        0xE59F0050, // E0: ldr r0, =ContextA
        0xE14F2000, // E4: mrs r2, spsr
        0xE5A02000, // E8: str r2, [r0, #0]!
        0xE9BD000C, // EC: ldmib sp!, {r2, r3}
        0xE9A0000C, // F0: stmib r0!, {r2, r3}
        0xE9BD500C, // F4: ldmib sp!, {r2, r3, r12, lr}
        0xE9E07FFC, // F8: stmib r0!, {r2-lr}^
        0xE9A04000, // FC: stmib r0!, {lr}
        0xE3A030D3, // 100: mov r3, #0xD3
        0xE121F003, // 104: msr cpsr_c, r3
        0xE9A02000, // 108: stmib r0!, {sp}
        0xE591D044, // 10C: ldr sp, [r1, #0x44]
        0xE3A030D2, // 110: mov r3, #0xD2
        0xE121F003, // 114: msr cpsr_c, r3
        0xE5B12000, // 118: ldr r2, [r1, #0]!
        0xE169F002, // 11C: msr spsr_fc, r2
        0xE591E040, // 120: ldr lr, [r1, #0x40]
        0xE9D17FFF, // 124: ldmib r1, {r0-lr}^
        0xE1A00000, // 128: nop
        0xE82D500F, // 12C: stmda sp!, {r0-r3, r12, lr}
        0xE8BD8000, // 130: ldmia sp!, {pc}
        ContextB,
        ContextA);

    private static Type Recompile()
    {
        var funcs = RecompileProgram(
            Processor.ARM9,
            Program,
            Address,
            new InputConfig { ReturnsTwiceFuncs = ["Save"] },
            new Symbol(Address, 0x34, "Main", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0x34, 0x20, "ThreadB", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0x54, 0x34, "Save", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0x88, 0x2C, "Load", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0xB4, 0x18, "WaitForFlag", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0xCC, 0x70, "Handler", IsThumb: false, Section: 0, IsFunction: true));

        var save = Function(funcs.GetNestedType("Original")!, "Save");
        funcs.GetNestedType("Patches")!.GetField("Save")!.SetValue(null, (RecompFunc)(ctx => LibRecomp.ARM9.Recomp.SaveContext(ctx, save)));
        return funcs;
    }

    private static readonly byte[] SwitchingProgram = ARMBytes(
        0xE92D4010, // 00: push {r4, lr}
        0xE59F001C, // 04: ldr r0, =MainSP
        0xE59F101C, // 08: ldr r1, =TaskSP
        0xEB000018, // 0C: bl Switch
        0xE59F0010, // 10: ldr r0, =MainSP
        0xE59F1010, // 14: ldr r1, =TaskSP
        0xEB000015, // 18: bl Switch
        0xE59F100C, // 1C: ldr r1, =Flag
        0xE5910000, // 20: ldr r0, [r1]
        0xE8BD8010, // 24: pop {r4, pc}
        MainSP,
        TaskSP,
        Flag,
        0xE59F402C, // 34: ldr r4, =Flag
        0xE3A02001, // 38: mov r2, #1
        0xE5842000, // 3C: str r2, [r4]
        0xE59F0024, // 40: ldr r0, =TaskSP
        0xE59F1024, // 44: ldr r1, =MainSP
        0xEB000009, // 48: bl Switch
        0xE5942000, // 4C: ldr r2, [r4]
        0xE2822006, // 50: add r2, r2, #6
        0xE5842000, // 54: str r2, [r4]
        0xE59F000C, // 58: ldr r0, =TaskSP
        0xE59F100C, // 5C: ldr r1, =MainSP
        0xEB000003, // 60: bl Switch
        0xEAFFFFFE, // 64: b 0x64
        Flag,
        TaskSP,
        MainSP,
        0xE92D4010, // 74: push {r4, lr}
        0xE580D000, // 78: str sp, [r0]
        0xE591D000, // 7C: ldr sp, [r1]
        0xE8BD8010); // 80: pop {r4, pc}

    private static void Switch(RecompContext ctx)
    {
        (uint resumePC, uint resumeSP) = (ctx.R14, ctx.R13);
        ctx.R13 -= 8;
        NTRModern.ARM9.Memory.Write32(ctx.R13, ctx.R4);
        NTRModern.ARM9.Memory.Write32(ctx.R13 + 4, ctx.R14);
        NTRModern.ARM9.Memory.Write32(ctx.R0, ctx.R13);
        ctx.R13 = NTRModern.ARM9.Memory.Read32(ctx.R1);
        ctx.R4 = NTRModern.ARM9.Memory.Read32(ctx.R13);
        uint pc = NTRModern.ARM9.Memory.Read32(ctx.R13 + 4);
        ctx.R13 += 8;
        LibRecomp.ARM9.Recomp.ContinueAt(ctx, pc, resumePC, resumeSP);
    }

    private static void CreateThreadB(uint cpsr)
    {
        NTRModern.ARM9.Memory.Write32(ContextB, cpsr);
        NTRModern.ARM9.Memory.Write32(ContextB + 0x38, StackB);
        NTRModern.ARM9.Memory.Write32(ContextB + 0x40, Address + 0x34 + 4);
        NTRModern.ARM9.Memory.Write32(ContextB + 0x44, SupervisorStackB);
    }

    [Fact]
    public void LoadingASavedContextReturnsFromTheSaveASecondTime()
    {
        var main = Function(Recompile(), "Main");
        CreateThreadB(SystemMode);
        var ctx = new RecompContext(Processor.ARM9) { R13 = Stack, R14 = ReturnAddress };

        SystemClock.Run(() => main(ctx), () => { });

        Assert.Equal((7u, Stack, SystemMode), (ctx.R0, ctx.R13, ctx.CPSR & 0xFF));
    }

    [Fact]
    public void AThreadSwitchedAwayFromInAnInterruptCarriesOnWhereItWasInterrupted()
    {
        var funcs = Recompile();
        NTRModern.ARM9.CP15.Write(0, 9, 1, 0, DTCM | 0x0A);
        NTRModern.ARM9.CP15.Write(0, 1, 0, 0, 1 << 16);
        NTRModern.ARM9.Memory.Write32(DTCM + 0x3FFC, Address + 0xCC);
        NTRModern.ARM9.Memory.Write32(0x04000208, 1);
        NTRModern.ARM9.Memory.Write32(0x04000210, (uint)Interrupt.Timer0);
        CreateThreadB(SystemMode | IRQDisable);
        var waitForFlag = Function(funcs, "WaitForFlag");
        var ctx = new RecompContext(Processor.ARM9) { R13 = Stack, R14 = ReturnAddress };
        ctx.SetBankedR13(IRQMode, DTCM + 0x3F80);
        ctx.SetBankedR13(SupervisorMode, DTCM + 0x3FC0);
        SystemClock.CreateEvent(_ => Hardware.InterruptsOf(Processor.ARM9).Raise(Interrupt.Timer0)).ScheduleAt(1000);

        SystemClock.Run(() => waitForFlag(ctx), () => { });

        Assert.Equal((7u, Stack, SystemMode), (ctx.R0, ctx.R13, ctx.CPSR & 0xFF));
        Assert.Equal(Address + 0xB8 + 4, NTRModern.ARM9.Memory.Read32(ContextA + 0x40));
    }

    [Fact]
    public void APatchedStackSwitchCarriesOnWhereEachStackLeftOff()
    {
        var funcs = RecompileProgram(
            Processor.ARM9,
            SwitchingProgram,
            Address,
            new InputConfig(),
            new Symbol(Address, 0x34, "Main", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0x34, 0x40, "Task", IsThumb: false, Section: 0, IsFunction: true),
            new Symbol(Address + 0x74, 0x10, "Switch", IsThumb: false, Section: 0, IsFunction: true));
        funcs.GetNestedType("Patches")!.GetField("Switch")!.SetValue(null, (RecompFunc)Switch);
        var main = Function(funcs, "Main");
        NTRModern.ARM9.Memory.Write32(TaskSP, StackB - 8);
        NTRModern.ARM9.Memory.Write32(StackB - 4, Address + 0x34);
        var ctx = new RecompContext(Processor.ARM9) { R13 = Stack, R14 = ReturnAddress };

        SystemClock.Run(() => main(ctx), () => { });

        Assert.Equal((7u, Stack), (ctx.R0, ctx.R13));
    }
}
