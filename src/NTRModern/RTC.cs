namespace NTRModern;

/// <summary><c>RTC</c> is the DS's real-time clock, which is where games get the date and time from.</summary>
public static class RTC
{
    private const ushort DataPin = 1 << 0;
    private const ushort ClockPin = 1 << 1;
    private const ushort SelectPin = 1 << 2;
    private const ushort DataDirection = 1 << 4;
    private const int FixedCode = 0x6;
    private const int ReadBit = 0x80;
    private const byte ResetBit = 1 << 0;
    private const byte Hour24 = 1 << 1;
    private const byte Status1Writable = 0x0E;
    private const byte Status1Flags = 0xF0;
    private const byte Interrupt1Mode = 0x0F;
    private const byte Alarm1Mode = 1 << 2;
    private const byte Interrupt2Enable = 1 << 6;
    private const byte TestMode = 1 << 7;
    private const byte PM = 0x40;

    private static readonly byte[] Parameters = new byte[7];
    private static readonly byte[] Alarm1 = new byte[3];
    private static readonly byte[] Alarm2 = new byte[3];
    private static readonly byte[] AlarmWritable = [0x87, 0xFF, 0xFF];

    private static ushort _pins;
    private static int _output;
    private static bool _hasCommand;
    private static Command _command;
    private static bool _isReading;
    private static int _byte;
    private static int _bit;
    private static int _shift;
    private static byte _status1 = Hour24;
    private static byte _status2;
    private static byte _adjustment;
    private static byte _free;
    private static TimeSpan _offset;
    private static int _dayOfWeekOffset;

    private enum Command
    {
        Status1 = 0,
        Interrupt1 = 1,
        DateTime = 2,
        ClockAdjustment = 3,
        Status2 = 4,
        Interrupt2 = 5,
        Time = 6,
        Free = 7,
    }

    /// <summary>
    /// Where the clock gets its time from. By default it's your computer's clock, but you can swap it out with your
    /// own <see cref="TimeProvider"/>, for example if you want to try a game at another time of day.
    /// </summary>
    public static TimeProvider Host { get; set; } = TimeProvider.System;

    private static DateTime HostNow => Host.GetLocalNow().DateTime;

    private static DateTime Now => HostNow + _offset;

    private static bool IsHour24 => (_status1 & Hour24) != 0;

    private static int Length => _command switch
    {
        Command.DateTime => 7,
        Command.Time or Command.Interrupt2 => 3,
        Command.Interrupt1 => (_status2 & Alarm1Mode) != 0 ? 3 : 1,
        _ => 1,
    };

    internal static ushort Read() => (ushort)((_pins & DataDirection) != 0 ? _pins : (_pins & ~DataPin) | _output);

    internal static void Write(ushort value)
    {
        ushort previous = _pins;
        _pins = value;
        Signal(previous, value);
    }

    internal static void Reset()
    {
        (_pins, _output) = (0, 0);
        ResetTransfer();
        (_status1, _status2, _adjustment, _free) = (Hour24, 0, 0, 0);
        Array.Clear(Alarm1);
        Array.Clear(Alarm2);
        (_offset, _dayOfWeekOffset) = (TimeSpan.Zero, 0);
    }

    private static void ResetTransfer()
    {
        (_hasCommand, _isReading, _byte, _bit, _shift) = (false, false, 0, 0, 0);
    }

    private static void Signal(ushort previous, ushort pins)
    {
        bool wasSelected = (previous & SelectPin) != 0;
        bool isSelected = (pins & SelectPin) != 0;
        if (!isSelected || !wasSelected)
        {
            if (isSelected && (pins & ClockPin) != 0)
            {
                ResetTransfer();
            }

            return;
        }

        bool clockFell = (previous & ClockPin) != 0 && (pins & ClockPin) == 0;
        bool clockRose = (previous & ClockPin) == 0 && (pins & ClockPin) != 0;
        if (clockFell && _isReading)
        {
            if (_byte == Length)
            {
                throw new NotSupportedException($"The ARM7 kept reading after RTC command {_command}.");
            }

            _output = (Parameters[_byte] >> _bit) & 1;
        }

        if (!clockRose)
        {
            return;
        }

        if (!_isReading)
        {
            _shift |= (pins & DataPin) << _bit;
        }

        if (++_bit == 8)
        {
            FinishByte();
        }
    }

    private static void FinishByte()
    {
        _bit = 0;
        if (!_hasCommand)
        {
            StartCommand(_shift);
        }
        else if (_isReading)
        {
            _byte++;
        }
        else
        {
            if (_byte == Length)
            {
                throw new NotSupportedException($"The ARM7 kept writing after RTC command {_command}.");
            }

            Parameters[_byte++] = (byte)_shift;
            if (_byte == Length)
            {
                Store();
            }
        }

        _shift = 0;
    }

    private static void StartCommand(int value)
    {
        if ((value & 0xF) != FixedCode)
        {
            throw new NotSupportedException($"The ARM7 sent 0x{value:X2} to the RTC, which isn't a command the DS's RTC has.");
        }

        _hasCommand = true;
        _command = (Command)((value >> 4) & 7);
        _isReading = (value & ReadBit) != 0;
        _byte = 0;
        if (!_isReading)
        {
            return;
        }

        switch (_command)
        {
            case Command.Status1:
                Parameters[0] = _status1;
                _status1 &= unchecked((byte)~Status1Flags);
                break;
            case Command.Status2:
                Parameters[0] = _status2;
                break;
            case Command.DateTime:
                Load(Now, Parameters);
                break;
            case Command.Time:
                Load(Now, Parameters);
                Parameters.AsSpan(4, 3).CopyTo(Parameters);
                break;
            case Command.Interrupt1 when Length == 1:
                Parameters[0] = Alarm1[2];
                break;
            case Command.Interrupt1:
                Alarm1.CopyTo(Parameters, 0);
                break;
            case Command.Interrupt2:
                Alarm2.CopyTo(Parameters, 0);
                break;
            case Command.ClockAdjustment:
                Parameters[0] = _adjustment;
                break;
            case Command.Free:
                Parameters[0] = _free;
                break;
        }
    }

    private static void Store()
    {
        switch (_command)
        {
            case Command.Status1:
                if ((Parameters[0] & ResetBit) != 0)
                {
                    throw new NotSupportedException("The ARM7 reset the RTC, which isn't supported.");
                }

                _status1 = (byte)((_status1 & ~Status1Writable) | (Parameters[0] & Status1Writable));
                break;
            case Command.Status2:
                if ((Parameters[0] & (Interrupt1Mode | Interrupt2Enable | TestMode)) != 0)
                {
                    throw new NotSupportedException($"The ARM7 set RTC status register 2 to 0x{Parameters[0]:X2}, which isn't supported.");
                }

                _status2 = Parameters[0];
                break;
            case Command.DateTime when ToDateTime(Parameters) is { } time:
                Set(time, Parameters[3] & 7);
                break;
            case Command.Time when ToTime(Parameters) is { } time:
                Set(Now.Date + time, DayOfWeek(Now));
                break;
            case Command.Interrupt1 when Length == 1:
                Alarm1[2] = Parameters[0];
                break;
            case Command.Interrupt1:
                StoreAlarm(Alarm1);
                break;
            case Command.Interrupt2:
                StoreAlarm(Alarm2);
                break;
            case Command.ClockAdjustment:
                _adjustment = Parameters[0];
                break;
            case Command.Free:
                _free = Parameters[0];
                break;
        }
    }

    private static void StoreAlarm(byte[] alarm)
    {
        for (int index = 0; index < alarm.Length; index++)
        {
            alarm[index] = (byte)(Parameters[index] & AlarmWritable[index]);
        }
    }

    private static int DayOfWeek(DateTime time) => ((((int)time.DayOfWeek + _dayOfWeekOffset) % 7) + 7) % 7;

    private static void Set(DateTime time, int dayOfWeek)
    {
        (_offset, _dayOfWeekOffset) = (time - HostNow, dayOfWeek - (int)time.DayOfWeek);
    }

    private static void Load(DateTime time, Span<byte> parameters)
    {
        parameters[0] = BCD(time.Year % 100);
        parameters[1] = BCD(time.Month);
        parameters[2] = BCD(time.Day);
        parameters[3] = (byte)DayOfWeek(time);
        parameters[4] = (byte)(BCD(IsHour24 ? time.Hour : time.Hour % 12) | (time.Hour >= 12 ? PM : 0));
        parameters[5] = BCD(time.Minute);
        parameters[6] = BCD(time.Second);
    }

    private static DateTime? ToDateTime(ReadOnlySpan<byte> parameters)
    {
        if (FromBCD(parameters[0]) is not { } year
            || FromBCD(parameters[1] & 0x1F) is not ({ } month and >= 1 and <= 12)
            || FromBCD(parameters[2] & 0x3F) is not { } day
            || day < 1 || day > DateTime.DaysInMonth(2000 + year, month)
            || ToTime(parameters[4..]) is not { } time)
        {
            return null;
        }

        return new DateTime(2000 + year, month, day) + time;
    }

    private static TimeSpan? ToTime(ReadOnlySpan<byte> parameters)
    {
        if (FromBCD(parameters[0] & 0x3F) is not { } hour
            || FromBCD(parameters[1] & 0x7F) is not ({ } minute and < 60)
            || FromBCD(parameters[2] & 0x7F) is not ({ } second and < 60))
        {
            return null;
        }

        if (!IsHour24)
        {
            hour = hour < 12 ? hour + ((parameters[0] & PM) != 0 ? 12 : 0) : 24;
        }

        return hour < 24 ? new TimeSpan(hour, minute, second) : null;
    }

    private static byte BCD(int value) => (byte)(((value / 10) << 4) | (value % 10));

    private static int? FromBCD(int value) => (value >> 4) < 10 && (value & 0xF) < 10 ? ((value >> 4) * 10) + (value & 0xF) : null;
}
