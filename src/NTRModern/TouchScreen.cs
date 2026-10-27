using System.Buffers.Binary;

namespace NTRModern;

/// <summary><c>TouchScreen</c> is the touchscreen on the lower screen. This is where you pass the player's mouse or finger to the game.</summary>
/// <example>
/// <code>
/// // Tap the lower screen 40 pixels from the left and 100 from the top
/// TouchScreen.Touch(40, 100);
/// TouchScreen.Release();
/// </code>
/// </example>
public static class TouchScreen
{
    private const int Width = 256;
    private const int Height = 192;
    private const byte FirstPoint = 1;
    private const byte LastX = 255;
    private const byte LastY = 191;
    private const int NotTouched = -1;
    private const byte StartBit = 1 << 7;
    private const byte EightBitMode = 1 << 3;
    private const byte SingleEnded = 1 << 2;
    private const int YPosition = 1;
    private const int Battery = 2;
    private const int Z1Position = 3;
    private const int Z2Position = 4;
    private const int XPosition = 5;
    private const int AUX = 6;

    private static int _touch = NotTouched;
    private static int _powerDownMode;
    private static uint _reply;
    private static bool _lastWasCommand;

    internal static bool IsPenDown => Volatile.Read(ref _touch) != NotTouched && IsPenIRQEnabled;

    private static bool IsPenIRQEnabled => Hardware.Model == Model.DSLite ? _powerDownMode <= 2 : _powerDownMode is 0 or 2;

    /// <summary>
    /// Puts the stylus on the lower screen, <paramref name="x"/> pixels from the left (0 to 255) and <paramref name="y"/>
    /// pixels from the top (0 to 191). The stylus stays there until you call <see cref="Release"/> or touch somewhere
    /// else. You can call this from any thread, so it's fine to call it right from your input handling.
    /// </summary>
    public static void Touch(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        Volatile.Write(ref _touch, (y << 8) | x);
    }

    /// <summary>Lifts the stylus off the screen. Same as <see cref="Touch"/>, you can call this from any thread.</summary>
    public static void Release() => Volatile.Write(ref _touch, NotTouched);

    internal static byte Transfer(byte value)
    {
        byte output = (byte)(_reply >> 24);
        _reply <<= 8;
        bool lastWasCommand = _lastWasCommand;
        _lastWasCommand = (value & StartBit) != 0;
        if (_lastWasCommand)
        {
            if (lastWasCommand)
            {
                throw new NotSupportedException($"The ARM7 sent touchscreen command 0x{value:X2} right after another one.");
            }

            _powerDownMode = value & 3;
            int result = Convert(value);
            _reply = (value & EightBitMode) != 0 ? (uint)(result >> 4) << 23 : (uint)result << 19;
        }

        return output;
    }

    internal static void Deselect() => (_reply, _lastWasCommand) = (0, false);

    internal static void Reset() => (_powerDownMode, _reply, _lastWasCommand) = (0, 0, false);

    internal static void WriteCalibration(Span<byte> calibration)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(calibration, ADC(FirstPoint));
        BinaryPrimitives.WriteUInt16LittleEndian(calibration[2..], ADC(FirstPoint));
        calibration[4] = FirstPoint;
        calibration[5] = FirstPoint;
        BinaryPrimitives.WriteUInt16LittleEndian(calibration[6..], ADC(LastX));
        BinaryPrimitives.WriteUInt16LittleEndian(calibration[8..], ADC(LastY));
        calibration[10] = LastX;
        calibration[11] = LastY;
    }

    private static int Convert(byte command)
    {
        int channel = (command >> 4) & 7;
        bool isSingleEnded = (command & SingleEnded) != 0;
        if (Hardware.Model == Model.DSLite && channel == AUX && !isSingleEnded)
        {
            return 0xFFF;
        }

        if (Hardware.Model != Model.DSi && !isSingleEnded && channel is not (YPosition or Z1Position or Z2Position or XPosition))
        {
            throw new NotSupportedException($"The ARM7 read touchscreen channel {channel} in differential mode, which isn't supported.");
        }

        int touch = Volatile.Read(ref _touch);
        return channel switch
        {
            XPosition => touch == NotTouched ? 0x000 : (touch & 0xFF) << 4,
            YPosition => touch == NotTouched ? 0xFFF : (touch >> 8) << 4,
            AUX => throw new NotSupportedException("The ARM7 read the microphone, which isn't supported."),
            Battery when Hardware.Model == Model.DS => 0x000,
            _ => 0xFFF,
        };
    }

    private static ushort ADC(byte point) => (ushort)((point - 1) << 4);
}
