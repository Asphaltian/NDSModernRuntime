namespace NTRModern;

internal static class WifiBaseband
{
    private const byte ChipID = 0x6D;

    private static readonly byte[] Registers = new byte[0x100];

    private static bool _isLoaded;

    public static void Reset()
    {
        Array.Clear(Registers);
        _isLoaded = false;
    }

    public static void Load(ReadOnlySpan<byte> initialValues)
    {
        Reset();
        _isLoaded = true;
        for (int index = 0; index < initialValues.Length; index++)
        {
            if (IsWritable(index))
            {
                Registers[index] = initialValues[index];
            }
        }
    }

    public static byte Read(int index) => index switch
    {
        _ when !_isLoaded => throw NotLoaded(),
        0x00 => ChipID,
        0x4D or 0x64 => throw new NotSupportedException($"The ARM7 read baseband register 0x{index:X2}, which isn't supported."),
        0x5D => (byte)(Hardware.Model == Model.DS ? 0x01 : 0x00),
        _ when IsWritable(index) => Registers[index],
        _ => 0x00,
    };

    public static void Write(int index, byte value)
    {
        if (!_isLoaded)
        {
            throw NotLoaded();
        }

        if (IsWritable(index))
        {
            Registers[index] = value;
        }
    }

    private static NotSupportedException NotLoaded()
    {
        return new NotSupportedException("The ARM7 used the baseband chip before the firmware set it up.");
    }

    private static bool IsWritable(int index) => index is (>= 0x01 and <= 0x0C) or (>= 0x13 and <= 0x15) or (>= 0x1B and <= 0x26)
        or (>= 0x28 and <= 0x4C) or (>= 0x4E and <= 0x5C) or 0x62 or 0x63 or 0x65 or 0x67 or 0x68;
}
