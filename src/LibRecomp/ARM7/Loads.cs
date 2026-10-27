using System.Numerics;
using System.Runtime.CompilerServices;
using NTRModern.ARM7;

namespace LibRecomp.ARM7;

/// <summary>These are called by recompiled code, so you won't need them yourself.</summary>
public static class Loads
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Word(uint address) => BitOperations.RotateRight(Memory.Read32(address), (int)(address & 3) * 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Halfword(uint address) => BitOperations.RotateRight(Memory.Read16(address), (int)(address & 1) * 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SignedHalfword(uint address) => (address & 1) == 0 ? (uint)(short)Memory.Read16(address) : (uint)(sbyte)Memory.Read8(address);
}
