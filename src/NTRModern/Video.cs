namespace NTRModern;

/// <summary><c>Video</c> is the DS's two screens. Subscribe to <see cref="FrameFinished"/> to get each frame as soon as it's done.</summary>
public static class Video
{
    public const int VisibleLines = 192;
    public const int TotalLines = 263;

    internal const long CyclesPerLine = DotsPerLine * BusCyclesPerDot * SystemClock.BusCycle;

    private const int RenderingStartLine = 214;
    private const long DotsPerLine = 355;
    private const long BusCyclesPerDot = 6;
    private const ushort VBlankFlag = 1 << 0;
    private const ushort HBlankFlag = 1 << 1;
    private const ushort VCountFlag = 1 << 2;
    private const ushort VBlankIRQEnable = 1 << 3;
    private const ushort HBlankIRQEnable = 1 << 4;
    private const ushort VCountIRQEnable = 1 << 5;
    private const ushort VCountSettingBit8 = 1 << 7;
    private const ushort Writable = 0xFFB8;

    private static readonly long[] CyclesUntilHBlank = [1606 * SystemClock.BusCycle, 1613 * SystemClock.BusCycle];
    private static readonly ushort[] Settings = new ushort[2];
    private static readonly bool[] IsInHBlank = new bool[2];
    private static readonly SystemClock.Event[] HBlankEvents =
    [
        SystemClock.CreateEvent(_ => StartHBlank(Processor.ARM9)),
        SystemClock.CreateEvent(_ => StartHBlank(Processor.ARM7)),
    ];

    private static readonly SystemClock.Event LineEvent = SystemClock.CreateEvent(StartLine);
    private static readonly VideoFrame Frame = new();

    private static FramePacer? _pacer;
    private static int _line;
    private static long _startedAt;

    /// <summary>
    /// Gives you each frame once its last visible line is drawn. Please note that this isn't called on your own
    /// thread, and that you get the same <see cref="VideoFrame"/> every time, so copy whatever you need before you
    /// return.
    /// </summary>
    public static event Action<VideoFrame>? FrameFinished;

    internal static ushort VCOUNT => (ushort)_line;

    internal static ushort ReadDISPSTAT(Processor processor)
    {
        int flags = Settings[(int)processor];
        if (_line is >= VisibleLines and < TotalLines - 1)
        {
            flags |= VBlankFlag;
        }

        if (IsInHBlank[(int)processor])
        {
            flags |= HBlankFlag;
        }

        if (_line == VCountSetting(processor))
        {
            flags |= VCountFlag;
        }

        return (ushort)flags;
    }

    internal static void WriteDISPSTAT(Processor processor, ushort value) => Settings[(int)processor] = (ushort)(value & Writable);

    internal static long NextVBlankAfter(long time)
    {
        long frame = TotalLines * CyclesPerLine;
        long firstVBlank = _startedAt + (VisibleLines * CyclesPerLine);
        long frames = time < firstVBlank ? 0 : ((time - firstVBlank) / frame) + 1;
        return firstVBlank + (frames * frame);
    }

    internal static void Start(bool paced)
    {
        _pacer = paced ? new FramePacer() : null;
        _line = 0;
        Array.Clear(IsInHBlank);
        _startedAt = SystemClock.CyclesOf(Processor.ARM9);
        BeginLine();
        ScheduleLine(_startedAt);
    }

    internal static void Reset()
    {
        (_line, _startedAt) = (0, 0);
        Array.Clear(Settings);
        Array.Clear(IsInHBlank);
    }

    internal static void BeforeVideoMemoryWrite(int kilobyte) => Frame.CopyBeforeWrite(kilobyte);

    private static int VCountSetting(Processor processor)
    {
        ushort settings = Settings[(int)processor];
        return (settings >> 8) | ((settings & VCountSettingBit8) << 1);
    }

    private static void ScheduleLine(long start)
    {
        HBlankEvents[(int)Processor.ARM9].ScheduleAt(start + CyclesUntilHBlank[(int)Processor.ARM9]);
        HBlankEvents[(int)Processor.ARM7].ScheduleAt(start + CyclesUntilHBlank[(int)Processor.ARM7]);
        LineEvent.ScheduleAt(start + CyclesPerLine);
    }

    private static void BeginLine()
    {
        if (_line == 0)
        {
            Engine2D.A.ReloadReferencePoints();
            Engine2D.B.ReloadReferencePoints();
        }

        Engine2D.A.UpdateWindows(_line);
        Engine2D.B.UpdateWindows(_line);
    }

    private static void StartHBlank(Processor processor)
    {
        IsInHBlank[(int)processor] = true;
        if ((Settings[(int)processor] & HBlankIRQEnable) != 0)
        {
            Hardware.InterruptsOf(processor).Raise(Interrupt.HBlank);
        }

        if (processor == Processor.ARM9 && _line < VisibleLines)
        {
            Frame.CaptureLine(_line);
            Engine2D.A.AdvanceReferencePoints();
            Engine2D.B.AdvanceReferencePoints();
            ARM9.IO.DMA.OnHBlank();
        }
    }

    private static void StartLine(long dueAt)
    {
        _line = (_line + 1) % TotalLines;
        Array.Clear(IsInHBlank);
        Keypad.CheckInterrupts();
        for (var processor = Processor.ARM9; processor <= Processor.ARM7; processor++)
        {
            ushort settings = Settings[(int)processor];
            if (_line == VCountSetting(processor) && (settings & VCountIRQEnable) != 0)
            {
                Hardware.InterruptsOf(processor).Raise(Interrupt.VCount);
            }

            if (_line == VisibleLines && (settings & VBlankIRQEnable) != 0)
            {
                Hardware.InterruptsOf(processor).Raise(Interrupt.VBlank);
            }
        }

        BeginLine();
        if (_line == VisibleLines)
        {
            Frame.CaptureMemory();
            FrameFinished?.Invoke(Frame);
            _pacer?.WaitForNextFrame();
            ARM9.IO.DMA.OnVBlank();
            ARM7.IO.DMA.OnVBlank();
        }
        else if (_line == RenderingStartLine)
        {
            Frame.CaptureRendering();
        }

        ScheduleLine(dueAt);
    }
}
