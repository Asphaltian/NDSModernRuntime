using System.Numerics;
using System.Runtime.CompilerServices;
using NTRModern.ARM9;

namespace LibRecomp.ARM9;

/// <summary>These are called by recompiled code, so you won't need them yourself.</summary>
public static class Loads
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Word(uint address) => BitOperations.RotateRight(Memory.Read32(address), (int)(address & 3) * 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Halfword(uint address) => Memory.Read16(address);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SignedHalfword(uint address) => (uint)(short)Memory.Read16(address);
}
