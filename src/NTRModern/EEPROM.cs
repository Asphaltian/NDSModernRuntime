namespace NTRModern;

internal static class EEPROM
{
    private const byte WRSR = 0x01;
    private const byte WR = 0x02;
    private const byte RD = 0x03;
    private const byte WRDI = 0x04;
    private const byte RDSR = 0x05;
    private const byte WREN = 0x06;
    private const byte WRHI = 0x0A;
    private const byte RDHI = 0x0B;
    private const byte RDID = 0x9F;
    private const byte WriteEnableLatch = 1 << 1;
    private const byte WriteProtect = (1 << 2) | (1 << 3);
    private const byte StatusRegisterWriteDisable = 1 << 7;
    private const byte EEPROM512Ones = 0xF0;

    private static byte? _command;
    private static int _addressBytesLeft;
    private static uint _address;
    private static int _written;
    private static bool _isStatusWritten;
    private static byte _status;

    private static bool IsEEPROM512 => SaveMemory.Type == SaveType.EEPROM512;

    private static int AddressBytes => SaveMemory.Type switch
    {
        SaveType.EEPROM512 => 1,
        SaveType.EEPROM128K => 3,
        _ => 2,
    };

    private static int PageSize => SaveMemory.Type switch
    {
        SaveType.EEPROM512 => 16,
        SaveType.EEPROM8K => 32,
        SaveType.EEPROM64K => 128,
        _ => 256,
    };

    public static byte Transfer(byte value)
    {
        if (_command is not { } command)
        {
            Start(value);
            return SaveMemory.Undriven;
        }

        switch (command)
        {
            case RDSR:
                return (byte)(_status | (IsEEPROM512 ? EEPROM512Ones : 0));
            case WRSR:
                WriteStatus(value);
                return SaveMemory.Undriven;
            case RDID:
                return SaveMemory.Undriven;
            case WREN or WRDI:
                throw new NotSupportedException($"The ARM7 kept sending after EEPROM command 0x{command:X2}, which has no parameters.");
        }

        if (_addressBytesLeft > 0)
        {
            _address = (_address << 8) | value;
            _addressBytesLeft--;
            return SaveMemory.Undriven;
        }

        if (command is RD or RDHI)
        {
            return SaveMemory.Data[(int)(_address++ & (uint)(SaveMemory.Data.Length - 1))];
        }

        Write(value);
        return SaveMemory.Undriven;
    }

    public static void Deselect()
    {
        if (_command is WR or WRHI or WRSR)
        {
            _status &= unchecked((byte)~WriteEnableLatch);
        }

        _command = null;
    }

    public static void Reset()
    {
        _command = null;
        _status = 0;
    }

    private static void Start(byte command)
    {
        switch (command)
        {
            case WREN:
                _status |= WriteEnableLatch;
                break;
            case WRDI:
                _status &= unchecked((byte)~WriteEnableLatch);
                break;
            case RDSR or RDID:
                break;
            case WRSR:
                _isStatusWritten = false;
                break;
            case RD or WR:
            case RDHI or WRHI when IsEEPROM512:
                _addressBytesLeft = AddressBytes;
                _address = command is RDHI or WRHI ? 1u : 0u;
                _written = 0;
                break;
            default:
                throw new NotSupportedException($"The ARM7 sent command 0x{command:X2} to the {SaveMemory.Type} save chip, which isn't a command it has.");
        }

        _command = command;
    }

    private static void WriteStatus(byte value)
    {
        if (_isStatusWritten)
        {
            throw new NotSupportedException("The ARM7 kept sending after writing the EEPROM status register, which takes one byte.");
        }

        byte keptBits = (byte)(WriteProtect | (IsEEPROM512 ? 0 : StatusRegisterWriteDisable));
        if ((value & keptBits) != 0)
        {
            throw new NotSupportedException($"The ARM7 turned on EEPROM write protection with 0x{value:X2}, which isn't supported.");
        }

        _isStatusWritten = true;
    }

    private static void Write(byte value)
    {
        if ((_status & WriteEnableLatch) == 0)
        {
            throw new NotSupportedException("The ARM7 wrote to the EEPROM without WREN first.");
        }

        if (_written > 0 && _address % (uint)PageSize == 0)
        {
            throw new NotSupportedException($"The ARM7 wrote past the end of a {PageSize}-byte EEPROM page.");
        }

        SaveMemory.Data[(int)(_address++ & (uint)(SaveMemory.Data.Length - 1))] = value;
        _written++;
        SaveMemory.IsModified = true;
    }
}
