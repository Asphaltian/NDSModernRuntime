namespace NTRModern;

internal static class PowerManagement
{
    private const byte Read = 1 << 7;
    private const byte SoundAmplifierEnable = 1 << 0;
    private const byte SoundAmplifierMute = 1 << 1;
    private const byte LowerBacklight = 1 << 2;
    private const byte UpperBacklight = 1 << 3;
    private const byte PowerLEDBlink = (1 << 4) | (1 << 5);
    private const byte SystemPowerShutDown = 1 << 6;
    private const byte ControlWritable = 0x7F;
    private const byte BacklightLevelsUnknown = 4 << 4;
    private const byte DSiResetAndUnknown = 0x03;
    private const int Control = 0;
    private const int BatteryStatus = 1;
    private const int MicrophoneAmplifier = 2;
    private const int MicrophoneGain = 3;
    private const int BacklightLevels = 4;
    private const int DSiBacklightMirrors = 0x10;

    private static readonly byte[] Registers = new byte[5];

    private static int? _index;
    private static bool _isDataDone;
    private static bool _isGainKnown;

    public static byte Transfer(byte value)
    {
        if (_index is not { } index)
        {
            _index = value;
            return 0;
        }

        if (_isDataDone)
        {
            throw new NotSupportedException("The ARM7 sent a third byte to the power management device without releasing it.");
        }

        _isDataDone = true;
        int register = Locate(index & ~Read);
        if ((index & Read) != 0)
        {
            return ReadRegister(register);
        }

        WriteRegister(register, value);
        return 0;
    }

    public static void Deselect() => (_index, _isDataDone) = (null, false);

    public static void Reset()
    {
        Deselect();
        Array.Clear(Registers);
        Registers[Control] = SoundAmplifierEnable | LowerBacklight | UpperBacklight;
        _isGainKnown = false;
        if (Hardware.Model == Model.DSi)
        {
            Registers[Control] |= SoundAmplifierMute;
            Registers[MicrophoneGain] = 1;
            Registers[BacklightLevels] = 1;
            _isGainKnown = true;
        }
    }

    private static int Locate(int index) => Hardware.Model switch
    {
        Model.DS => index & 3,
        Model.DSLite => Math.Min(index & 7, BacklightLevels),
        _ => index,
    };

    private static byte ReadRegister(int register) => register switch
    {
        MicrophoneGain when !_isGainKnown => throw new NotSupportedException("The ARM7 read the microphone gain before setting it."),
        <= MicrophoneGain => Registers[register],
        BacklightLevels => (byte)(BacklightLevelsUnknown | Registers[BacklightLevels]),
        DSiBacklightMirrors => (byte)(DSiResetAndUnknown | (Registers[Control] & (LowerBacklight | UpperBacklight))),
        _ => 0x00,
    };

    private static void WriteRegister(int register, byte value)
    {
        switch (register)
        {
            case Control when (value & SystemPowerShutDown) != 0:
                throw new NotSupportedException("The ARM7 switched the console off, which isn't supported.");
            case Control when Hardware.Model == Model.DSi && (value & PowerLEDBlink) != 0:
                throw new NotSupportedException($"The ARM7 made the DSi's power LED blink with 0x{value:X2}, which isn't supported.");
            case Control:
                Registers[Control] = (byte)(value & (Hardware.Model == Model.DSLite ? ControlWritable & ~SoundAmplifierMute : ControlWritable));
                break;
            case BatteryStatus:
                break;
            case MicrophoneAmplifier:
                Registers[MicrophoneAmplifier] = (byte)(value & 1);
                break;
            case MicrophoneGain:
                Registers[MicrophoneGain] = (byte)(value & 3);
                _isGainKnown = true;
                break;
            case BacklightLevels:
                Registers[BacklightLevels] = (byte)(value & (Hardware.Model == Model.DSi ? 3 : 7));
                break;
            case DSiBacklightMirrors:
                throw new NotSupportedException($"The ARM7 wrote 0x{value:X2} to DSi power management register 10h, which isn't supported.");
            default:
                throw new NotSupportedException($"The ARM7 wrote 0x{value:X2} to unused DSi power management register 0x{register:X2}.");
        }
    }
}
