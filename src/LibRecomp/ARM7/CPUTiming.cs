using System.Runtime.CompilerServices;
using NTRModern;
using NTRModern.ARM7;

namespace LibRecomp.ARM7;

/// <summary>These are called by recompiled code, so you won't need them yourself.</summary>
public static class CPUTiming
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long JumpCycles(uint target, bool isThumb) => Memory.JumpCycles(target, isThumb);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SequentialSaving(uint address, int count) => (count - 1) * Memory.SequentialSaving(address);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Multiply(uint multiplier) => UnsignedMultiply(multiplier ^ (uint)((int)multiplier >> 31));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long UnsignedMultiply(uint multiplier) => SystemClock.BusCycle * (multiplier switch
    {
        < 0x100 => 1,
        < 0x10000 => 2,
        < 0x1000000 => 3,
        _ => 4,
    });
}
