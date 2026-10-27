using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace NTRModern;

internal static class Firmware
{
    public const int UserSettingsSize = 0x70;
    public const int RFChipType = 0x040;
    public const int RFBitsPerEntry = 0x041;
    public const int RFEntries = 0x042;
    public const int InitialConfigValuesOffset = 0x044;
    public const int InitialBBValuesOffset = 0x064;
    public const int BBRegisters = 0x69;
    public const int InitialRFValuesOffset = 0x0CE;
    public const int WifiCalibrationEnd = 0x200;
    public const byte MM3218 = 3;

    private const int LargestSize = 0x40000;
    private const int DSiSize = 0x20000;
    private const int ConsoleTypeOffset = 0x01D;
    private const int UserSettingsOffset = 0x020;
    private const int WifiCalibrationChecksum = 0x02A;
    private const int WifiCalibrationStart = 0x02C;
    private const int UserSettingsAreaSize = 0x100;
    private const ushort UserSettingsVersion = 5;
    private const ushort SettingsOkay = (1 << 10) | (1 << 11) | (1 << 13) | (1 << 14) | (1 << 15);
    private const int NicknameLength = 10;
    private const int MessageLength = 26;
    private const int ExtendedSettings = 0x74;
    private const int ExtendedSettingsChecksum = 0xFE;
    private const byte ExtendedSettingsPresent = 1 << 6;
    private const byte WriteInProgress = 1 << 0;
    private const byte WriteEnableLatch = 1 << 1;
    private const int PageSize = 0x100;
    private const int SectorSize = 0x10000;
    private const int ProtectedEnd = 256 * PageSize;
    private const long MicrosecondsPerSecond = 1_000_000;

    private const byte READ = 0x03;
    private const byte FAST = 0x0B;
    private const byte RDSR = 0x05;
    private const byte RDID = 0x9F;
    private const byte WREN = 0x06;
    private const byte WRDI = 0x04;
    private const byte PW = 0x0A;
    private const byte PP = 0x02;
    private const byte PE = 0xDB;
    private const byte SE = 0xD8;
    private const byte DP = 0xB9;
    private const byte RDP = 0xAB;

    private static readonly byte[] Image = new byte[LargestSize];
    private static readonly List<byte> PageData = new(PageSize);

    private static readonly ushort[] InitialConfigValues =
    [
        0x0002, 0x0017, 0x0026, 0x1818, 0x0048, 0x4840, 0x0058, 0x0042,
        0x0140, 0x8064, 0xE0E0, 0x2443, 0x000E, 0x0032, 0x01F4, 0x0101,
    ];

    private static readonly byte[] InitialBBValues =
    [
        0x03, 0x17, 0x40, 0x00, 0x1B, 0x6C, 0x48, 0x80, 0x38, 0x00, 0x35, 0x07, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC7, 0xBB, 0x01, 0x24, 0x7F,
        0x5A, 0x01, 0x3F, 0x01, 0x3F, 0x36, 0x1D, 0x00, 0x78, 0x35, 0x55, 0x12, 0x34, 0x1C, 0x00, 0x01,
        0x0E, 0x38, 0x03, 0x70, 0xC5, 0x2A, 0x0A, 0x08, 0x04, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFE,
        0xFE, 0xFE, 0xFE, 0xFC, 0xFC, 0xFA, 0xFA, 0xFA, 0xFA, 0xFA, 0xF8, 0xF8, 0xF6, 0x00, 0x12, 0x14,
        0x12, 0x41, 0x23, 0x03, 0x04, 0x70, 0x35, 0x0E, 0x2C, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x0E, 0x00, 0x00, 0x12, 0x28, 0x1C,
    ];

    private static readonly byte[] InitialRFValues =
    [
        0x31, 0x4C, 0x4F, 0x21, 0x00, 0x10, 0xB0, 0x08, 0xFA, 0x15, 0x26, 0xE6, 0xC1, 0x01, 0x0E, 0x50,
        0x05, 0x00, 0x6D, 0x12, 0x00, 0x00, 0x01, 0xFF, 0x0E, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x06,
        0x06, 0x00, 0x00, 0x00, 0x18, 0x00, 0x02, 0x00, 0x00,
    ];

    private static readonly byte[] ChannelValues =
    [
        0x1E, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x16,
        0x26, 0x1C, 0x1C, 0x1C, 0x1D, 0x1D, 0x1D, 0x1E, 0x1E, 0x1E, 0x1E, 0x1F, 0x1E, 0x1F, 0x18,
        0x01, 0x4B, 0x4B, 0x4B, 0x4B, 0x4C, 0x4C, 0x4C, 0x4C, 0x4C, 0x4C, 0x4C, 0x4D, 0x4D, 0x4D,
        0x02, 0x6C, 0x71, 0x76, 0x5B, 0x40, 0x45, 0x4A, 0x2F, 0x34, 0x39, 0x3E, 0x03, 0x08, 0x14,
    ];

    private static byte? _instruction;
    private static int _parameterBytes;
    private static uint _address;
    private static int _identificationByte;
    private static byte _status;
    private static long _busyUntil;
    private static bool _isPoweredDown;

    public static ReadOnlySpan<byte> CurrentUserSettings => Image.AsSpan(UserSettingsArea + UserSettingsAreaSize, UserSettingsSize);

    public static ReadOnlySpan<byte> Contents => Image.AsSpan(0, Size);

    public static bool IsModified { get; set; }

    private static int Size => Hardware.Model == Model.DSi ? DSiSize : LargestSize;

    private static int UserSettingsArea => Size - (2 * UserSettingsAreaSize);

    private static byte ConsoleType => Hardware.Model switch
    {
        Model.DS => 0xFF,
        Model.DSLite => 0x20,
        _ => 0x57,
    };

    private static byte[] Identification => Hardware.Model switch
    {
        Model.DS => [0x20, 0x40, 0x12],
        Model.DSLite => [0x20, 0x50, 0x12],
        _ => [0x20, 0x40, 0x11],
    };

    public static void Build(UserSettings settings, ReadOnlySpan<byte> firmware)
    {
        Array.Fill(Image, (byte)0xFF);
        if (!firmware.IsEmpty)
        {
            CheckFirmware(firmware);
            firmware.CopyTo(Image);
        }
        else if (Hardware.Model == Model.DSi)
        {
            Image.AsSpan(0x000, 0x1D).Clear();
            Image.AsSpan(0x022, 6).Clear();
            Image.AsSpan(0x200, 0xFF).Clear();
            Image[0x2FF] = 0x80;
        }

        Image[ConsoleTypeOffset] = ConsoleType;
        BinaryPrimitives.WriteUInt16LittleEndian(Image.AsSpan(UserSettingsOffset), (ushort)(UserSettingsArea / 8));
        if (firmware.IsEmpty)
        {
            WriteWifiCalibration();
        }

        var area = Image.AsSpan(UserSettingsArea, UserSettingsAreaSize);
        WriteUserSettings(area, settings, updateCounter: 0);
        area.CopyTo(Image.AsSpan(UserSettingsArea + UserSettingsAreaSize));
        WriteCounterAndCRC(Image.AsSpan(UserSettingsArea + UserSettingsAreaSize, UserSettingsAreaSize), updateCounter: 1);
        Wifi.LoadFirmwareSettings(Image.AsSpan(0, WifiCalibrationEnd));
        ClearInstruction();
        (_status, _busyUntil, _isPoweredDown, IsModified) = (0, 0, false, false);
    }

    public static byte Transfer(byte value)
    {
        if (_instruction is not { } instruction)
        {
            Start(value);
            return 0;
        }

        switch (instruction)
        {
            case DP:
                return 0;
            case RDP:
                throw new NotSupportedException("The ARM7 kept sending after firmware instruction RDP, which has no parameters.");
            case READ or FAST or PW or PP or PE or SE when _parameterBytes < (instruction == FAST ? 4 : 3):
                _address = _parameterBytes < 3 ? (_address << 8) | value : _address;
                _parameterBytes++;
                return 0;
            case READ or FAST:
                return Image[_address++ % Size];
            case PW or PP when (_address + (uint)PageData.Count) / PageSize != _address / PageSize:
                throw new NotSupportedException("The ARM7 wrote past the end of a firmware page.");
            case PW or PP:
                PageData.Add(value);
                return 0;
            case PE or SE:
                throw new NotSupportedException($"The ARM7 kept sending after the address of firmware instruction 0x{instruction:X2}, which only takes an address.");
            case RDSR:
                return (byte)(_status | (SystemClock.CyclesOf(Processor.ARM7) < _busyUntil ? WriteInProgress : 0));
            case RDID when _identificationByte < Identification.Length:
                return Identification[_identificationByte++];
            default:
                throw new NotSupportedException($"The ARM7 kept reading after firmware instruction 0x{instruction:X2}, which isn't supported.");
        }
    }

    public static void Deselect()
    {
        try
        {
            if (_instruction is PW or PP or PE or SE)
            {
                FinishWrite(_instruction.Value);
            }
        }
        finally
        {
            ClearInstruction();
        }
    }

    private static void ClearInstruction()
    {
        _instruction = null;
        (_parameterBytes, _address, _identificationByte) = (0, 0, 0);
        PageData.Clear();
    }

    private static void Start(byte instruction)
    {
        if (_isPoweredDown)
        {
            _isPoweredDown = instruction != RDP;
            _instruction = instruction == RDP ? RDP : DP;
            return;
        }

        if (instruction != RDSR && SystemClock.CyclesOf(Processor.ARM7) < _busyUntil)
        {
            throw new NotSupportedException($"The ARM7 sent firmware instruction 0x{instruction:X2} while a write was still in progress.");
        }

        switch (instruction)
        {
            case READ or FAST or RDSR or RDID:
                _instruction = instruction;
                break;
            case PW or PP or PE or SE when (_status & WriteEnableLatch) == 0:
                throw new NotSupportedException($"The ARM7 sent firmware instruction 0x{instruction:X2} without WREN first.");
            case PW or PP or PE or SE:
                _instruction = instruction;
                break;
            case DP:
                _isPoweredDown = true;
                break;
            case WREN:
                _status |= WriteEnableLatch;
                break;
            case WRDI:
                _status &= unchecked((byte)~WriteEnableLatch);
                break;
            default:
                throw new NotSupportedException($"The ARM7 sent firmware instruction 0x{instruction:X2}, which isn't supported.");
        }
    }

    private static void FinishWrite(byte instruction)
    {
        if (_parameterBytes < 3 || (instruction is PW or PP && PageData.Count == 0))
        {
            throw new NotSupportedException($"The ARM7 released the firmware chip before finishing instruction 0x{instruction:X2}.");
        }

        int address = (int)(_address % (uint)Size);
        int start = instruction == SE ? address & ~(SectorSize - 1) : address;
        if (start < ProtectedEnd)
        {
            throw new NotSupportedException($"The ARM7 wrote to read-only firmware address 0x{address:X5}.");
        }

        var page = Image.AsSpan(address & ~(PageSize - 1), PageSize);
        int offset = address & (PageSize - 1);
        long microseconds = 0;
        switch (instruction)
        {
            case PW:
                CollectionsMarshal.AsSpan(PageData).CopyTo(page[offset..]);
                microseconds = 11_000;
                break;
            case PP:
                for (int i = 0; i < PageData.Count; i++)
                {
                    page[offset + i] &= PageData[i];
                }

                microseconds = 1_200;
                break;
            case PE:
                page.Fill(0xFF);
                microseconds = 10_000;
                break;
            case SE:
                Image.AsSpan(start, SectorSize).Fill(0xFF);
                microseconds = 1_000_000;
                break;
        }

        _status &= unchecked((byte)~WriteEnableLatch);
        _busyUntil = SystemClock.CyclesOf(Processor.ARM7) + (microseconds * SystemClock.CyclesPerSecond / MicrosecondsPerSecond);
        IsModified = true;
    }

    private static void WriteWifiCalibration()
    {
        if (Hardware.Model == Model.DS)
        {
            throw new ArgumentException("An original DS needs a firmware dump.");
        }

        var header = Image.AsSpan(0, WifiCalibrationEnd);
        BinaryPrimitives.WriteUInt16LittleEndian(header[WifiCalibrationStart..], 0x0138);
        header[0x2E] = 0x00;
        header[0x2F] = (byte)(Hardware.Model == Model.DSLite ? 5 : 15);
        ReadOnlySpan<byte> unused = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        ReadOnlySpan<byte> macAddress = Hardware.Model == Model.DSLite ? [0x00, 0x16, 0x56, 0x11, 0x22, 0x33] : [0x00, 0x09, 0xBF, 0x11, 0x22, 0x33];
        unused.CopyTo(header[0x30..]);
        macAddress.CopyTo(header[0x36..]);
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x3C..], 0x3FFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x3E..], 0xFFFF);
        (header[RFChipType], header[RFBitsPerEntry], header[RFEntries], header[0x43]) = (MM3218, 0x94, 0x29, 0x02);
        for (int index = 0; index < InitialConfigValues.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header[(InitialConfigValuesOffset + (index * 2))..], InitialConfigValues[index]);
        }

        InitialBBValues.CopyTo(header[InitialBBValuesOffset..]);
        header[0xCD] = 0x00;
        InitialRFValues.CopyTo(header[InitialRFValuesOffset..]);
        header[0xF7] = 0x02;
        ChannelValues.CopyTo(header[0xF8..]);
        header[0x162] = 0x00;
        if (Hardware.Model == Model.DSi)
        {
            (header[0x1FD], header[0x1FE]) = (0x01, 0x20);
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(header[WifiCalibrationStart..]);
        BinaryPrimitives.WriteUInt16LittleEndian(header[WifiCalibrationChecksum..], CRC16.Compute(0, header.Slice(WifiCalibrationStart, length)));
    }

    private static void CheckFirmware(ReadOnlySpan<byte> firmware)
    {
        if (firmware.Length != Size)
        {
            throw new ArgumentException($"The firmware is 0x{firmware.Length:X} bytes, and a {Hardware.Model}'s firmware is 0x{Size:X}.", nameof(firmware));
        }

        Model? firmwareModel = firmware[ConsoleTypeOffset] switch
        {
            0xFF => Model.DS,
            0x20 => Model.DSLite,
            0x57 => Model.DSi,
            _ => null,
        };

        if (firmwareModel != Hardware.Model)
        {
            throw new ArgumentException($"The firmware has console type 0x{firmware[ConsoleTypeOffset]:X2}, which isn't a {Hardware.Model}.", nameof(firmware));
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(firmware[WifiCalibrationStart..]);
        if (WifiCalibrationStart + length > WifiCalibrationEnd
            || CRC16.Compute(0, firmware.Slice(WifiCalibrationStart, length)) != BinaryPrimitives.ReadUInt16LittleEndian(firmware[WifiCalibrationChecksum..]))
        {
            throw new ArgumentException("The wifi calibration in the firmware is damaged.", nameof(firmware));
        }

        if ((firmware[RFChipType] == MM3218) != (Hardware.Model != Model.DS))
        {
            throw new ArgumentException($"The firmware has RF chip type {firmware[RFChipType]}, which isn't the {Hardware.Model}'s RF chip.", nameof(firmware));
        }

        int rfBytes = firmware[RFEntries] * (firmware[RFChipType] == MM3218 ? 1 : ((firmware[RFBitsPerEntry] & 0x3F) + 7) / 8);
        if (InitialRFValuesOffset + rfBytes > WifiCalibrationEnd)
        {
            throw new ArgumentException("The RF values in the firmware's wifi calibration don't fit in it.", nameof(firmware));
        }
    }

    private static void WriteUserSettings(Span<byte> area, UserSettings settings, ushort updateCounter)
    {
        if (settings.Nickname.Length > NicknameLength || settings.Message.Length > MessageLength)
        {
            throw new ArgumentException($"The nickname can have up to {NicknameLength} characters and the message up to {MessageLength}.", nameof(settings));
        }

        if (settings.FavoriteColor is < 0 or > 15 || settings.BirthdayMonth is < 1 or > 12 || settings.BirthdayDay is < 1 or > 31)
        {
            throw new ArgumentException("The favorite color has to be from 0 to 15, the birthday month from 1 to 12 and the birthday day from 1 to 31.", nameof(settings));
        }

        area[..0x6C].Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(area, UserSettingsVersion);
        area[0x02] = (byte)settings.FavoriteColor;
        area[0x03] = (byte)settings.BirthdayMonth;
        area[0x04] = (byte)settings.BirthdayDay;
        Encoding.Unicode.GetBytes(settings.Nickname).CopyTo(area[0x06..]);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x1A..], (ushort)settings.Nickname.Length);
        Encoding.Unicode.GetBytes(settings.Message).CopyTo(area[0x1C..]);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x50..], (ushort)settings.Message.Length);
        TouchScreen.WriteCalibration(area[0x58..0x64]);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x64..], (ushort)((int)settings.Language | SettingsOkay));
        WriteCounterAndCRC(area, updateCounter);

        if ((ConsoleType & ExtendedSettingsPresent) != 0)
        {
            area[ExtendedSettings..ExtendedSettingsChecksum].Clear();
            area[ExtendedSettings] = 0x01;
            area[ExtendedSettings + 1] = (byte)settings.Language;
            BinaryPrimitives.WriteUInt16LittleEndian(area[(ExtendedSettings + 2)..], 0x007F);
            BinaryPrimitives.WriteUInt16LittleEndian(area[ExtendedSettingsChecksum..], CRC16.Compute(0xFFFF, area[ExtendedSettings..ExtendedSettingsChecksum]));
        }
    }

    private static void WriteCounterAndCRC(Span<byte> area, ushort updateCounter)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x70..], updateCounter);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x72..], CRC16.Compute(0xFFFF, area[..0x70]));
    }
}
