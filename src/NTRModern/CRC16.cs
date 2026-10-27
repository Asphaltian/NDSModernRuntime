namespace NTRModern;

internal static class CRC16
{
    private static readonly ushort[] Values = [0xC0C1, 0xC181, 0xC301, 0xC601, 0xCC01, 0xD801, 0xF001, 0xA001];

    public static ushort Compute(ushort crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = Add(crc, value);
        }

        return crc;
    }

    public static ushort Add(ushort crc, byte value)
    {
        uint result = (uint)(crc ^ value);
        for (int bit = 0; bit < 8; bit++)
        {
            bool carry = (result & 1) != 0;
            result >>= 1;
            if (carry)
            {
                result ^= (uint)Values[bit] << (7 - bit);
            }
        }

        return (ushort)result;
    }
}
