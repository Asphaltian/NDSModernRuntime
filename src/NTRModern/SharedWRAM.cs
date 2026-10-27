namespace NTRModern;

internal static class SharedWRAM
{
    public const int Size = 0x8000;
    public const byte AllToARM7 = 3;

    private const uint HalfSize = Size / 2;

    public static readonly byte[] Data = new byte[Size];

    public static byte WRAMCNT { get; private set; } = AllToARM7;

    public static void WriteWRAMCNT(byte value) => WRAMCNT = (byte)(value & 3);

    public static void Reset()
    {
        WRAMCNT = AllToARM7;
        Array.Clear(Data);
    }

    public static bool TryMapARM9(uint address, out int offset) => TryMap(address, WRAMCNT switch
    {
        0 => (0, Size),
        1 => (HalfSize, HalfSize),
        2 => (0, HalfSize),
        _ => (0, 0),
    }, out offset);

    public static bool TryMapARM7(uint address, out int offset) => TryMap(address, WRAMCNT switch
    {
        0 => (0, 0),
        1 => (0, HalfSize),
        2 => (HalfSize, HalfSize),
        _ => (0, Size),
    }, out offset);

    private static bool TryMap(uint address, (uint Start, uint Size) part, out int offset)
    {
        offset = (int)(part.Start + (address & (part.Size - 1)));
        return part.Size != 0;
    }
}
