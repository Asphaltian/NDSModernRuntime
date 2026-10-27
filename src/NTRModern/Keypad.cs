namespace NTRModern;

/// <summary>The DS's buttons. You can combine them if you want to hold several of them at once.</summary>
[Flags]
public enum Buttons : ushort
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    Select = 1 << 2,
    Start = 1 << 3,
    Right = 1 << 4,
    Left = 1 << 5,
    Up = 1 << 6,
    Down = 1 << 7,
    R = 1 << 8,
    L = 1 << 9,
    X = 1 << 10,
    Y = 1 << 11,
}

/// <summary><c>Keypad</c> is the DS's buttons. This is where you pass the player's buttons to the game.</summary>
public static class Keypad
{
    private const ushort KeypadButtons = 0x3FF;
    private const ushort IRQEnable = 1 << 14;
    private const ushort AllSelected = 1 << 15;
    private const ushort KEYCNTWritable = 0xC3FF;
    private const ushort XReleased = 1 << 0;
    private const ushort YReleased = 1 << 1;
    private const ushort UnknownSet = (1 << 2) | (1 << 4) | (1 << 5);
    private const ushort DebugButtonReleased = 1 << 3;
    private const ushort PenUp = 1 << 6;

    private static readonly ushort[] Controls = new ushort[2];

    /// <summary>The buttons that are held right now. You can set this from any thread, so it's fine to set it right from your input handling.</summary>
    public static volatile Buttons Pressed;

    internal static ushort KEYINPUT => (ushort)(~(ushort)Pressed & KeypadButtons);

    internal static ushort EXTKEYIN
    {
        get
        {
            var pressed = Pressed;
            return (ushort)(UnknownSet | DebugButtonReleased | ((pressed & Buttons.X) == 0 ? XReleased : 0) | ((pressed & Buttons.Y) == 0 ? YReleased : 0)
                | (TouchScreen.IsPenDown ? 0 : PenUp));
        }
    }

    internal static ushort ReadKEYCNT(Processor processor) => Controls[(int)processor];

    internal static void WriteKEYCNT(Processor processor, ushort value)
    {
        Controls[(int)processor] = (ushort)(value & KEYCNTWritable);
        CheckInterrupt(processor);
    }

    internal static void CheckInterrupts()
    {
        CheckInterrupt(Processor.ARM9);
        CheckInterrupt(Processor.ARM7);
    }

    internal static void Reset() => Array.Clear(Controls);

    private static void CheckInterrupt(Processor processor)
    {
        ushort control = Controls[(int)processor];
        var selected = (Buttons)(control & KeypadButtons);
        var held = Pressed & selected;
        if ((control & IRQEnable) != 0 && ((control & AllSelected) != 0 ? held == selected : held != Buttons.None))
        {
            Hardware.InterruptsOf(processor).Raise(Interrupt.Keypad);
        }
    }
}
