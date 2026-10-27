using System.Numerics;
using NTRModern;

namespace LibRecomp;

internal static partial class BIOS
{
    private const uint IRQMode = 0x12;
    private const uint ARM9Vectors = 0xFFFF0000;
    private const uint ARM7Vectors = 0x00000000;
    private const uint SWIVectorOffset = 0x08;
    private const uint IRQVectorOffset = 0x18;
    private const uint ARM9HandlerOffset = 0x3FFC;
    private const uint ARM9CheckFlagsOffset = 0x3FF8;
    private const uint ARM7Handler = 0x0380FFFC;
    private const uint ARM7CheckFlags = 0x0380FFF8;
    private const uint ARM7BIOSEnd = 0x00004000;
    private const uint IME = 0x04000208;
    private const uint POSTFLG = 0x04000300;
    private const uint HALTCNT = 0x04000301;
    private const byte Halt = 0x80;
    private const long ARM7CyclesPerWaitLoop = 8;
    private const long ARM9CyclesPerWaitLoop = 16;

    private static readonly int[] ARM9Functions = [0x00, 0x03, 0x04, 0x05, 0x06, 0x09, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x18, 0x1F];
    private static readonly int[] ARM7Functions = [0x00, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x1A, 0x1B, 0x1C, 0x1D, 0x1F];

    public static void Call(CPU cpu, RecompContext ctx, int function)
    {
        if (!(cpu.Processor == Processor.ARM9 ? ARM9Functions : ARM7Functions).Contains(function))
        {
            throw new InvalidOperationException($"SWI 0x{function:X2} is not a BIOS function!");
        }

        switch (function)
        {
            case 0x03:
                WaitByLoop(cpu, ctx);
                break;
            case 0x04:
                IntrWait(cpu, ctx, discardOldFlags: ctx.R0 != 0, ctx.R1);
                break;
            case 0x05:
                (ctx.R0, ctx.R1) = (1, 1);
                IntrWait(cpu, ctx, discardOldFlags: true, (uint)Interrupt.VBlank);
                break;
            case 0x06:
                HaltCPU(cpu);
                cpu.HandleEvents(ctx, address: null);
                break;
            case 0x09:
                Div(cpu, ctx, (int)ctx.R0, (int)ctx.R1);
                break;
            case 0x0B:
                CpuSet(cpu, ctx, ctx.R0, ctx.R1, ctx.R2);
                break;
            case 0x0C:
                CpuFastSet(cpu, ctx, ctx.R0, ctx.R1, ctx.R2);
                break;
            case 0x0D:
                ctx.R0 = Sqrt(ctx.R0);
                break;
            case 0x0E:
                GetCRC16(cpu, ctx, (ushort)ctx.R0, ctx.R1, ctx.R2);
                break;
            case 0x0F:
                ctx.R0 = 0;
                break;
            case 0x10:
                BitUnPack(cpu, ctx, ctx.R0, ctx.R1, ctx.R2);
                break;
            case 0x11:
                LZ77UnComp(new Source(cpu, ctx, ctx.R0), new Output(cpu, ctx, ctx.R1, halfwords: false));
                break;
            case 0x12:
                LZ77UnComp(new Source(cpu, ctx, ctx.R0, ctx.R3, ctx.R2), new Output(cpu, ctx, ctx.R1, halfwords: true));
                break;
            case 0x13:
                HuffUnComp(cpu, ctx, new Source(cpu, ctx, ctx.R0, ctx.R3, ctx.R2), ctx.R1);
                break;
            case 0x14:
                RLUnComp(new Source(cpu, ctx, ctx.R0), new Output(cpu, ctx, ctx.R1, halfwords: false));
                break;
            case 0x15:
                RLUnComp(new Source(cpu, ctx, ctx.R0, ctx.R3, ctx.R2), new Output(cpu, ctx, ctx.R1, halfwords: true));
                break;
            case 0x1A:
                ctx.R0 = TableEntry(SineTable, ctx.R0, "GetSineTable");
                break;
            case 0x1B:
                ctx.R0 = TableEntry(PitchTable, ctx.R0, "GetPitchTable");
                break;
            case 0x1C:
                ctx.R0 = TableEntry(VolumeTable, ctx.R0, "GetVolumeTable");
                break;
            case 0x1F when cpu.Processor == Processor.ARM9:
                cpu.Write32(POSTFLG, ctx.R0);
                break;
            case 0x1F:
                cpu.Write8(HALTCNT, (byte)ctx.R2);
                cpu.HandleEvents(ctx, address: null);
                break;
            default:
                throw new NotSupportedException($"SWI 0x{function:X2} isn't supported on the {cpu.Processor}.");
        }
    }

    public static void EnterIRQ(CPU cpu, RecompContext ctx, uint? returnAddress)
    {
        uint interruptedSP = ctx.R13;
        ctx.EnterException(IRQMode);
        if (returnAddress is { } address)
        {
            ctx.R14 = address + 4;
        }

        uint interruptedLR = ctx.R14;
        uint sp = ctx.R13 - 24;
        cpu.Write32(sp, ctx.R0);
        cpu.Write32(sp + 4, ctx.R1);
        cpu.Write32(sp + 8, ctx.R2);
        cpu.Write32(sp + 12, ctx.R3);
        cpu.Write32(sp + 16, ctx.R12);
        cpu.Write32(sp + 20, ctx.R14);
        ctx.R13 = sp;

        bool isARM9 = cpu.Processor == Processor.ARM9;
        uint vector = (isARM9 ? ARM9Vectors : ARM7Vectors) + IRQVectorOffset;
        ctx.R14 = vector;
        uint handler = cpu.Read32(isARM9 ? NTRModern.ARM9.CP15.DTCMBase + ARM9HandlerOffset : ARM7Handler);
        cpu.Functions.Lookup(handler)(ctx);
        cpu.Functions.TryResumeAt(vector);
        cpu.Functions.ThrowIfUnwinding();

        sp = ctx.R13;
        ctx.R0 = cpu.Read32(sp);
        ctx.R1 = cpu.Read32(sp + 4);
        ctx.R2 = cpu.Read32(sp + 8);
        ctx.R3 = cpu.Read32(sp + 12);
        ctx.R12 = cpu.Read32(sp + 16);
        ctx.R14 = cpu.Read32(sp + 20);
        ctx.R13 = sp + 24;

        uint returnLR = ctx.R14;
        ctx.ReturnFromException();
        cpu.Threads.ContinueAt(ctx, returnLR - 4, ((interruptedLR - 4) & ~1u, interruptedSP));
    }

    private static uint SWIVector(CPU cpu) => (cpu.Processor == Processor.ARM9 ? ARM9Vectors : ARM7Vectors) + SWIVectorOffset;

    private static void WaitByLoop(CPU cpu, RecompContext ctx)
    {
        int count = (int)ctx.R0;
        long remaining = Math.Max(count, 1) * (cpu.Processor == Processor.ARM9 ? ARM9CyclesPerWaitLoop : ARM7CyclesPerWaitLoop);
        ref long now = ref SystemClock.CyclesOf(cpu.Processor);
        while (remaining > 0)
        {
            long step = Math.Clamp(SystemClock.NextEventOf(cpu.Processor) - now, 1, remaining);
            now += step;
            remaining -= step;
            CheckEvents(cpu, ctx);
        }

        ctx.R0 = count > 0 ? 0 : (uint)(count - 1);
    }

    private static void IntrWait(CPU cpu, RecompContext ctx, bool discardOldFlags, uint flags)
    {
        cpu.Write32(IME, 1);

        uint checkFlags = cpu.Processor == Processor.ARM9 ? NTRModern.ARM9.CP15.DTCMBase + ARM9CheckFlagsOffset : ARM7CheckFlags;
        if (discardOldFlags)
        {
            cpu.Write32(checkFlags, cpu.Read32(checkFlags) & ~flags);
        }

        bool waitsForAnyInterrupt = cpu.Processor == Processor.ARM9 && !discardOldFlags;
        while (waitsForAnyInterrupt || (cpu.Read32(checkFlags) & flags) == 0)
        {
            waitsForAnyInterrupt = false;
            HaltCPU(cpu);
            cpu.HandleEvents(ctx, address: null);
        }

        cpu.Write32(checkFlags, cpu.Read32(checkFlags) & ~flags);
    }

    private static void HaltCPU(CPU cpu)
    {
        if (cpu.Processor == Processor.ARM9)
        {
            NTRModern.ARM9.CP15.Write(0, 7, 0, 4, 0);
        }
        else
        {
            cpu.Write8(HALTCNT, Halt);
        }
    }

    private static void Div(CPU cpu, RecompContext ctx, int number, int denominator)
    {
        if (denominator == 0)
        {
            throw new InvalidOperationException($"The {cpu.Processor} divided by zero with SWI 0x09.");
        }

        long quotient = (long)number / denominator;
        ctx.R0 = (uint)quotient;
        ctx.R1 = (uint)(number - (quotient * denominator));
        ctx.R3 = (uint)Math.Abs(quotient);
    }

    private static uint TableEntry<T>(T[] table, uint index, string function)
        where T : IBinaryInteger<T>
    {
        return index < table.Length
            ? uint.CreateTruncating(table[index])
            : throw new NotSupportedException($"The ARM7 called {function} with index 0x{index:X}, which is past the end of the table.");
    }

    private static uint Sqrt(uint value)
    {
        uint root = (uint)Math.Sqrt(value);
        while ((ulong)root * root > value)
        {
            root--;
        }

        while ((ulong)(root + 1) * (root + 1) <= value)
        {
            root++;
        }

        return root;
    }

    private static void CpuSet(CPU cpu, RecompContext ctx, uint source, uint destination, uint control)
    {
        uint count = control & 0x1FFFFF;
        bool fill = (control & (1 << 24)) != 0;
        uint unit = (control & (1 << 26)) != 0 ? 4u : 2u;
        if (ReadsARM7BIOS(cpu, source, count * unit))
        {
            return;
        }

        uint filler = !fill ? 0 : unit == 4 ? cpu.Read32(source) : cpu.Read16(source);
        for (uint i = 0; i < count; i++)
        {
            if (unit == 4)
            {
                cpu.Write32(destination + (i * 4), fill ? filler : cpu.Read32(source + (i * 4)));
            }
            else
            {
                cpu.Write16(destination + (i * 2), (ushort)(fill ? filler : cpu.Read16(source + (i * 2))));
            }

            CheckEvents(cpu, ctx);
        }
    }

    private static void CpuFastSet(CPU cpu, RecompContext ctx, uint source, uint destination, uint control)
    {
        uint count = control & 0x1FFFFF;
        bool fill = (control & (1 << 24)) != 0;
        if (ReadsARM7BIOS(cpu, source, count * 4))
        {
            return;
        }

        uint filler = fill ? cpu.Read32(source) : 0;
        for (uint i = 0; i < count; i++)
        {
            cpu.Write32(destination + (i * 4), fill ? filler : cpu.Read32(source + (i * 4)));
            CheckEvents(cpu, ctx);
        }
    }

    private static bool ReadsARM7BIOS(CPU cpu, uint source, uint length)
    {
        return cpu.Processor == Processor.ARM7 && (source < ARM7BIOSEnd || source + length < ARM7BIOSEnd);
    }

    private static void GetCRC16(CPU cpu, RecompContext ctx, ushort crc, uint start, uint length)
    {
        for (uint offset = 0; offset < length; offset += 2)
        {
            ushort halfword = cpu.Read16(start + offset);
            crc = CRC16.Add(CRC16.Add(crc, (byte)halfword), (byte)(halfword >> 8));
            ctx.R3 = halfword;
            CheckEvents(cpu, ctx);
        }

        ctx.R0 = crc;
    }

    private static void CheckEvents(CPU cpu, RecompContext ctx)
    {
        if (SystemClock.CyclesOf(cpu.Processor) >= SystemClock.NextEventOf(cpu.Processor))
        {
            cpu.HandleEvents(ctx, address: null);
        }
    }
}
