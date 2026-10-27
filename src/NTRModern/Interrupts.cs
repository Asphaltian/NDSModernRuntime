namespace NTRModern;

/// <summary>Everything that can interrupt a CPU. Each one has the same bit it has in IE and IF.</summary>
[Flags]
public enum Interrupt : uint
{
    VBlank = 1u << 0,
    HBlank = 1u << 1,
    VCount = 1u << 2,
    Timer0 = 1u << 3,
    Timer1 = 1u << 4,
    Timer2 = 1u << 5,
    Timer3 = 1u << 6,
    RTC = 1u << 7,
    DMA0 = 1u << 8,
    DMA1 = 1u << 9,
    DMA2 = 1u << 10,
    DMA3 = 1u << 11,
    Keypad = 1u << 12,
    GBASlot = 1u << 13,
    IPCSync = 1u << 16,
    IPCSendFIFOEmpty = 1u << 17,
    IPCReceiveFIFONotEmpty = 1u << 18,
    CardTransferComplete = 1u << 19,
    CardIREQ = 1u << 20,
    GeometryCommandFIFO = 1u << 21,
    ScreensUnfolding = 1u << 22,
    SPI = 1u << 23,
    Wifi = 1u << 24,
}

internal sealed class Interrupts(Processor processor)
{
    public uint IME { get; private set; }

    public uint IE { get; private set; }

    public uint IF { get; private set; }

    public bool IsRequested => (IE & IF) != 0;

    public bool IsPending => (IME & 1) != 0 && IsRequested;

    public void Raise(Interrupt interrupt)
    {
        IF |= (uint)interrupt;
        CheckPending();
    }

    public void WriteIME(uint value)
    {
        IME = value & 1;
        CheckPending();
    }

    public void WriteIE(uint value)
    {
        IE = value;
        CheckPending();
    }

    public void Acknowledge(uint value) => IF &= ~value;

    public void Reset() => (IME, IE, IF) = (0, 0, 0);

    private void CheckPending()
    {
        if (IsPending)
        {
            SystemClock.NextEventOf(processor) = SystemClock.CyclesOf(processor);
        }
    }
}
