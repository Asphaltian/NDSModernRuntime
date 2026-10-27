namespace NTRModern;

internal static class MainMemory
{
    public const int Size = 0x400000;
    public const uint Mask = Size - 1;

    public static readonly byte[] Data = new byte[Size];
}
