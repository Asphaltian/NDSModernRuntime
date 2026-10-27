namespace NTRModern;

internal static class WifiRF
{
    private const int ResetRegister = 0x1F;
    private const int Type3Registers = 0x29;
    private const ushort Type2Read = 1 << 7;
    private const int Type3Write = 5;
    private const int Type3Read = 6;

    private static readonly int[] RF9008PowerOn =
    [
        0x00007, 0x09003, 0x00022, 0x1FF78, 0x09003, 0x01780, 0x00000, 0x14578,
        0x1E742, 0x00120, 0x00000, 0x00000, 0x00000, 0x00000, 0x00000, 0x00000,
        0x00000, 0x00000, 0x00000, 0x00000, 0x00000, 0x00000, 0x00000, 0x00000,
        0x00000, 0x00000, 0x00000, 0x0000F, 0x00000, 0x00000, 0x00000, 0x00001,
    ];

    private static readonly int[] Registers = new int[0x40];

    private static bool _isLoaded;

    private static bool IsRF9008 => Hardware.Model == Model.DS;

    public static void Reset()
    {
        Array.Clear(Registers);
        if (IsRF9008)
        {
            RF9008PowerOn.CopyTo(Registers, 0);
        }

        _isLoaded = false;
    }

    public static void Load(ReadOnlySpan<byte> header)
    {
        Reset();
        _isLoaded = true;
        int entries = header[Firmware.RFEntries];
        if (!IsRF9008)
        {
            for (int index = 0; index < entries; index++)
            {
                Registers[index] = header[Firmware.InitialRFValuesOffset + index];
            }

            return;
        }

        int bytesPerEntry = ((header[Firmware.RFBitsPerEntry] & 0x3F) + 7) / 8;
        for (int entry = 0; entry < entries; entry++)
        {
            int value = 0;
            for (int part = 0; part < bytesPerEntry; part++)
            {
                value |= header[Firmware.InitialRFValuesOffset + (entry * bytesPerEntry) + part] << (part * 8);
            }

            Registers[(value >> 18) & 0x1F] = value & 0x3FFFF;
        }
    }

    public static void Transfer(ref ushort data1, ref ushort data2)
    {
        if (IsRF9008)
        {
            TransferRF9008(ref data1, ref data2);
        }
        else if (_isLoaded)
        {
            TransferMM3218(ref data1, data2);
        }
        else
        {
            throw new NotSupportedException("The ARM7 used the RF chip before the firmware set it up.");
        }
    }

    private static void TransferRF9008(ref ushort data1, ref ushort data2)
    {
        if ((data2 & 0xFF00) != 0)
        {
            throw new NotSupportedException($"The ARM7 wrote 0x{data2:X4} to W_RF_DATA2, which isn't supported.");
        }

        int index = (data2 >> 2) & 0x1F;
        if ((data2 & Type2Read) != 0)
        {
            int value = Registers[index];
            data1 = (ushort)value;
            data2 = (ushort)((data2 & ~3) | ((value >> 16) & 3));
        }
        else if (index == ResetRegister)
        {
            RF9008PowerOn.CopyTo(Registers, 0);
        }
        else
        {
            Registers[index] = ((data2 & 3) << 16) | data1;
        }
    }

    private static void TransferMM3218(ref ushort data1, ushort data2)
    {
        int index = (data1 >> 8) & 0x3F;
        if ((data2 & 0xFFF0) != 0 || index >= Type3Registers)
        {
            throw new NotSupportedException($"The ARM7 started an RF transfer with W_RF_DATA1 0x{data1:X4} and W_RF_DATA2 0x{data2:X4}, which isn't supported.");
        }

        switch (data2 & 0xF)
        {
            case Type3Write:
                Registers[index] = data1 & 0xFF;
                break;
            case Type3Read:
                data1 = (ushort)((data1 & 0xFF00) | Registers[index]);
                break;
            default:
                throw new NotSupportedException($"The ARM7 sent RF command {data2 & 0xF}, which isn't supported.");
        }
    }
}
