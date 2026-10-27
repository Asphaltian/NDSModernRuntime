using System.Runtime.CompilerServices;
using NTRModern.ARM9;

namespace LibRecomp.ARM9;

/// <summary>These are called by recompiled code, so you won't need them yourself.</summary>
public static class CPUTiming
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long JumpCycles(uint target, bool isThumb) => Memory.JumpCycles(target, isThumb);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long FetchCycles(uint address, int opcodes, bool isThumb) => Memory.FetchCycles(address, opcodes, isThumb);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadWordCycles(uint address) => Memory.ReadWordCycles(address);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SequentialSaving(uint address, int count, bool isWrite) => (count - 1) * Memory.SequentialSaving(address, isWrite);
}
