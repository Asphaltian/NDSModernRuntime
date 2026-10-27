namespace NTRModern;

internal static class SPI
{
    private const ushort Busy = 1 << 7;
    private const ushort Is16Bit = 1 << 10;
    private const ushort ChipselectHold = 1 << 11;
    private const ushort IRQEnable = 1 << 14;
    private const ushort Enable = 1 << 15;
    private const ushort Writable = 0xCF03;
    private const int PowerManagementDevice = 0;
    private const int FirmwareDevice = 1;
    private const int TouchScreenDevice = 2;

    private static readonly long[] BaudRates = [4_000_000, 2_000_000, 1_000_000, 512_000];
    private static readonly SystemClock.Event Finished = SystemClock.CreateEvent(_ => Finish());

    private static ushort _control;
    private static byte _data;
    private static long _busyUntil;
    private static int? _selected;

    public static ushort SPICNT => (ushort)(_control | (SystemClock.CyclesOf(Processor.ARM7) < _busyUntil ? Busy : 0));

    public static byte SPIDATA => _data;

    public static void WriteSPICNT(ushort value) => _control = (ushort)(value & Writable);

    public static void WriteSPIDATA(byte value)
    {
        if ((_control & Enable) == 0 || (_control & Is16Bit) != 0)
        {
            throw new NotSupportedException($"The ARM7 wrote SPIDATA with SPICNT set to 0x{_control:X4}, which isn't supported.");
        }

        int device = (_control >> 8) & 3;
        if (_selected is { } selected && selected != device)
        {
            Deselect();
        }

        _selected = device;
        _data = device switch
        {
            PowerManagementDevice => PowerManagement.Transfer(value),
            FirmwareDevice => Firmware.Transfer(value),
            TouchScreenDevice => TouchScreen.Transfer(value),
            _ => throw new NotSupportedException($"The ARM7 sent 0x{value:X2} to SPI device {device}, which isn't supported."),
        };

        if ((_control & ChipselectHold) == 0)
        {
            Deselect();
        }

        _busyUntil = SystemClock.CyclesOf(Processor.ARM7) + (8 * SystemClock.CyclesPerSecond / BaudRates[_control & 3]);
        if ((_control & IRQEnable) != 0)
        {
            Finished.ScheduleAt(_busyUntil);
        }
    }

    public static void Reset()
    {
        (_control, _data, _busyUntil, _selected) = (0, 0, 0, null);
        PowerManagement.Reset();
        Firmware.Deselect();
        TouchScreen.Reset();
    }

    private static void Deselect()
    {
        switch (_selected)
        {
            case PowerManagementDevice:
                PowerManagement.Deselect();
                break;
            case FirmwareDevice:
                Firmware.Deselect();
                break;
            case TouchScreenDevice:
                TouchScreen.Deselect();
                break;
        }

        _selected = null;
    }

    private static void Finish() => ARM7.IO.Interrupts.Raise(Interrupt.SPI);
}
