using LibRecomp;
using NDSRecomp;
using NTRModern;
using static Recompilation.Tests.RecompiledCode;
using Processor = NTRModern.Processor;

namespace Recompilation.Tests;

public class TwoCPUTests
{
    private const uint ARM7Address = 0x02380000;
    private const uint IRQMode = 0x12;
    private const uint SystemMode = 0x1F;
    private const uint DTCM = 0x027E0000;
    private const uint ARM9Received = 0x02001000;
    private const uint ARM7Received = 0x02001004;

    public TwoCPUTests() => Hardware.Reset(Model.DS);

    private static byte[] ARM9Program() =>
    [
        .. ThumbBytes(
            0xB510, // 00: push {r4, lr}
            0x4C0A, // 02: ldr r4, =0x04000000
            0x480A, // 04: ldr r0, =0x8400
            0x490B, // 06: ldr r1, =0x184
            0x5260, // 08: strh r0, [r4, r1]
            0x480B, // 0A: ldr r0, =0x40000
            0x490B, // 0C: ldr r1, =0x210
            0x5060, // 0E: str r0, [r4, r1]
            0x490B, // 10: ldr r1, =0x180
            0x5A60, // 12: ldrh r0, [r4, r1]
            0x220F, // 14: movs r2, #0xF
            0x4010, // 16: ands r0, r2
            0x2801, // 18: cmp r0, #1
            0xD1F9, // 1A: bne 0x10
            0x4809, // 1C: ldr r0, =0x1234
            0x490A, // 1E: ldr r1, =0x188
            0x5060, // 20: str r0, [r4, r1]
            0x2001, // 22: movs r0, #1
            0x4904, // 24: ldr r1, =0x40000
            0xDF04, // 26: svc 4
            0xBD10, // 28: pop {r4, pc}
            0x0000),
        .. ARMBytes(
            0x04000000,
            0x00008400,
            0x00000184,
            0x00040000,
            0x00000210,
            0x00000180,
            0x00001234,
            0x00000188,
            0xE3A03301, // 4C: mov r3, #0x04000000
            0xE5930214, // 50: ldr r0, [r3, #0x214]
            0xE5830214, // 54: str r0, [r3, #0x214]
            0xE3A01641, // 58: mov r1, #0x04100000
            0xE5911000, // 5C: ldr r1, [r1]
            0xE59F2014, // 60: ldr r2, =0x027E3FF8
            0xE592C000, // 64: ldr r12, [r2]
            0xE18CC000, // 68: orr r12, r12, r0
            0xE582C000, // 6C: str r12, [r2]
            0xE59F2008, // 70: ldr r2, =0x02001000
            0xE5821000, // 74: str r1, [r2]
            0xE12FFF1E, // 78: bx lr
            0x027E3FF8,
            0x02001000),
    ];

    private static byte[] ARM7Program() =>
    [
        .. ThumbBytes(
            0xB510, // 00: push {r4, lr}
            0x4C09, // 02: ldr r4, =0x04000000
            0x4809, // 04: ldr r0, =0x8400
            0x490A, // 06: ldr r1, =0x184
            0x5260, // 08: strh r0, [r4, r1]
            0x480A, // 0A: ldr r0, =0x40000
            0x490A, // 0C: ldr r1, =0x210
            0x5060, // 0E: str r0, [r4, r1]
            0x480A, // 10: ldr r0, =0x100
            0x490B, // 12: ldr r1, =0x180
            0x5260, // 14: strh r0, [r4, r1]
            0x2001, // 16: movs r0, #1
            0x4906, // 18: ldr r1, =0x40000
            0xDF04, // 1A: svc 4
            0x4A09, // 1C: ldr r2, =0x02001004
            0x6810, // 1E: ldr r0, [r2]
            0x3001, // 20: adds r0, #1
            0x4909, // 22: ldr r1, =0x188
            0x5060, // 24: str r0, [r4, r1]
            0xBD10), // 26: pop {r4, pc}
        .. ARMBytes(
            0x04000000,
            0x00008400,
            0x00000184,
            0x00040000,
            0x00000210,
            0x00000100,
            0x00000180,
            0x02001004,
            0x00000188,
            0xE3A03301, // 4C: mov r3, #0x04000000
            0xE5930214, // 50: ldr r0, [r3, #0x214]
            0xE5830214, // 54: str r0, [r3, #0x214]
            0xE3A01641, // 58: mov r1, #0x04100000
            0xE5911000, // 5C: ldr r1, [r1]
            0xE59F2014, // 60: ldr r2, =0x0380FFF8
            0xE592C000, // 64: ldr r12, [r2]
            0xE18CC000, // 68: orr r12, r12, r0
            0xE582C000, // 6C: str r12, [r2]
            0xE59F2008, // 70: ldr r2, =0x02001004
            0xE5821000, // 74: str r1, [r2]
            0xE12FFF1E, // 78: bx lr
            0x0380FFF8,
            0x02001004),
    ];

    [Fact]
    public void TheCPUsTradeWordsThroughTheIPCFIFOWithIRQsAndIntrWait()
    {
        NTRModern.ARM9.CP15.Write(0, 9, 1, 0, DTCM | 0x0A);
        NTRModern.ARM9.CP15.Write(0, 1, 0, 0, 1 << 16);
        NTRModern.ARM9.Memory.Write32(DTCM + 0x3FFC, Address + 0x4C);
        NTRModern.ARM7.Memory.Write32(0x0380FFFC, ARM7Address + 0x4C);

        var arm9 = RecompileProgram(
            Processor.ARM9,
            ARM9Program(),
            Address,
            new Symbol(Address, 0x4C, "Main9", IsThumb: true, Section: 0, IsFunction: true),
            new Symbol(Address + 0x4C, 0x38, "Handler9", IsThumb: false, Section: 0, IsFunction: true));
        var arm7 = RecompileProgram(
            Processor.ARM7,
            ARM7Program(),
            ARM7Address,
            new Symbol(ARM7Address, 0x4C, "Main7", IsThumb: true, Section: 0, IsFunction: true),
            new Symbol(ARM7Address + 0x4C, 0x38, "Handler7", IsThumb: false, Section: 0, IsFunction: true));

        var arm9Context = new RecompContext(Processor.ARM9) { R13 = DTCM + 0x3E00, R14 = ReturnAddress };
        arm9Context.SetBankedR13(IRQMode, DTCM + 0x3F80);
        var arm7Context = new RecompContext(Processor.ARM7) { R13 = 0x0380FD80, R14 = ReturnAddress };
        arm7Context.SetBankedR13(IRQMode, 0x0380FF80);
        (NTRModern.ARM9.Scheduler.Cycles, NTRModern.ARM7.Scheduler.Cycles) = (0, 0);

        SystemClock.Run(() => Function(arm9, "Main9")(arm9Context), () => Function(arm7, "Main7")(arm7Context));

        Assert.Equal(0x1234u, NTRModern.ARM9.Memory.Read32(ARM7Received));
        Assert.Equal(0x1235u, NTRModern.ARM9.Memory.Read32(ARM9Received));
        Assert.Equal((DTCM + 0x3E00, SystemMode), (arm9Context.R13, arm9Context.CPSR & 0xFF));
        Assert.Equal((0x0380FD80u, SystemMode), (arm7Context.R13, arm7Context.CPSR & 0xFF));
    }
}
