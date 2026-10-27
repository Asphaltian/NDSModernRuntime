namespace NTRModern;

internal static class RenderingEngine
{
    public const uint RegistersStart = 0x320;
    public const uint RegistersEnd = 0x400;
    public const int ControlRegistersSize = (int)(ToonTableEnd - EDGE_COLOR);

    private const uint RDLINES_COUNT = 0x320;
    private const uint EDGE_COLOR = 0x330;
    private const uint ALPHA_TEST_REF = 0x340;
    private const uint CLEAR_COLOR = 0x350;
    private const uint FOG_OFFSET = 0x35C;
    private const uint FOG_TABLE = 0x360;
    private const uint ToonTableEnd = 0x3C0;

    private static readonly byte[] Registers = new byte[RegistersEnd - RegistersStart];

    public static ReadOnlySpan<byte> ControlRegisters => Registers.AsSpan((int)(EDGE_COLOR - RegistersStart), ControlRegistersSize);

    public static uint Read(uint word, uint mask)
    {
        for (uint index = 0; index < 4; index++)
        {
            if ((mask & (0xFFu << (int)(index * 8))) == 0)
            {
                continue;
            }

            uint offset = word + index;
            if (offset == RDLINES_COUNT)
            {
                throw new NotSupportedException("The ARM9 read RDLINES_COUNT, which isn't supported.");
            }

            if (IsDefined(offset))
            {
                throw new NotSupportedException($"The ARM9 read the write-only 3D rendering register at 0x{0x04000000 + offset:X8}.");
            }
        }

        return 0;
    }

    public static void Write(uint word, uint value, uint mask)
    {
        if (!ARM9.IO.IsRenderingEnginePowered)
        {
            return;
        }

        for (uint index = 0; index < 4; index++)
        {
            uint offset = word + index;
            if ((mask & (0xFFu << (int)(index * 8))) == 0 || !IsDefined(offset))
            {
                continue;
            }

            if (offset == RDLINES_COUNT)
            {
                throw new NotSupportedException("The ARM9 wrote the read-only RDLINES_COUNT.");
            }

            Registers[offset - RegistersStart] = (byte)(value >> (int)(index * 8));
        }
    }

    public static void Reset() => Array.Clear(Registers);

    private static bool IsDefined(uint offset) => offset is RDLINES_COUNT or ALPHA_TEST_REF
        or (>= EDGE_COLOR and < ALPHA_TEST_REF)
        or (>= CLEAR_COLOR and < FOG_OFFSET + 2)
        or (>= FOG_TABLE and < ToonTableEnd);
}
