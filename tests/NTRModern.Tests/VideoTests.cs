namespace NTRModern.Tests;

public class VideoTests
{
    private const uint DISPSTAT = 0x04000004;
    private const uint VCOUNT = 0x04000006;
    private const uint IF = 0x04000214;
    private const uint BG0HOFS = 0x04000010;
    private const uint BG2PB = 0x04000022;
    private const uint BG2X = 0x04000028;
    private const uint WIN0V = 0x04000044;
    private const uint VRAMCNT_A = 0x04000240;
    private const uint VRAMCNT_H = 0x04000248;
    private const uint POWCNT1 = 0x04000304;
    private const uint EngineB = 0x1000;
    private const long CyclesPerLine = 355 * 6 * SystemClock.BusCycle;
    private const long CyclesPerFrame = Video.TotalLines * CyclesPerLine;
    private const long CyclesUntilHBlank = 1606 * SystemClock.BusCycle;
    private const int ReferencePointsWord = 28;
    private const int ActiveWindowsWord = 32;
    private const int EngineBWords = 33;
    private const int VRAMCNTWord = 66;
    private const ushort VBlankFlag = 1 << 0;
    private const ushort HBlankFlag = 1 << 1;
    private const ushort VCountFlag = 1 << 2;
    private const ushort VBlankIRQEnable = 1 << 3;
    private const ushort HBlankIRQEnable = 1 << 4;
    private const ushort VCountIRQEnable = 1 << 5;

    public VideoTests()
    {
        Hardware.Reset(Model.DS);
        Video.Start(paced: false);
    }

    private static void Advance(long cycles)
    {
        ARM9.Scheduler.Cycles += cycles;
        ARM7.Scheduler.Cycles = ARM9.Scheduler.Cycles;
        SystemClock.Synchronize(Processor.ARM9);
    }

    private static void AdvanceToLine(int line) => Advance(FrameStart() + (line * CyclesPerLine) - ARM9.Scheduler.Cycles);

    private static long FrameStart() => ARM9.Scheduler.Cycles / CyclesPerFrame * CyclesPerFrame;

    [Fact]
    public void VCOUNTMovesOnEveryLineAndWrapsAfterTheLast()
    {
        Advance(CyclesPerLine - 1);
        Assert.Equal(0, ARM9.IO.Read16(VCOUNT));

        Advance(1);
        Assert.Equal(1, ARM9.IO.Read16(VCOUNT));
        Assert.Equal(1, ARM7.IO.Read16(VCOUNT));

        AdvanceToLine(Video.TotalLines);
        Assert.Equal(0, ARM9.IO.Read16(VCOUNT));
    }

    [Theory]
    [InlineData(191, false)]
    [InlineData(192, true)]
    [InlineData(261, true)]
    [InlineData(262, false)]
    public void TheVBlankFlagIsSetFromLine192ToLine261(int line, bool isSet)
    {
        AdvanceToLine(line);

        Assert.Equal(isSet, (ARM9.IO.Read16(DISPSTAT) & VBlankFlag) != 0);
    }

    [Fact]
    public void TheARM7SeesHBlankLaterThanTheARM9()
    {
        Advance((1606 * SystemClock.BusCycle) - 1);
        Assert.Equal(0, ARM9.IO.Read16(DISPSTAT) & HBlankFlag);

        Advance(1);
        Assert.Equal(HBlankFlag, ARM9.IO.Read16(DISPSTAT) & HBlankFlag);
        Assert.Equal(0, ARM7.IO.Read16(DISPSTAT) & HBlankFlag);

        Advance((1613 - 1606) * SystemClock.BusCycle);
        Assert.Equal(HBlankFlag, ARM7.IO.Read16(DISPSTAT) & HBlankFlag);

        AdvanceToLine(1);
        Assert.Equal(0, ARM9.IO.Read16(DISPSTAT) & HBlankFlag);
    }

    [Fact]
    public void EachCPURequestsTheInterruptsItEnabled()
    {
        ARM7.IO.Write16(DISPSTAT, VBlankIRQEnable);
        ARM9.IO.Write16(DISPSTAT, HBlankIRQEnable);

        AdvanceToLine(Video.VisibleLines);

        Assert.Equal((uint)Interrupt.VBlank, ARM7.IO.Read32(IF));
        Assert.Equal((uint)Interrupt.HBlank, ARM9.IO.Read32(IF));
    }

    [Fact]
    public void TheVCountSettingHasANinthBit()
    {
        ARM9.IO.Write16(DISPSTAT, (ushort)(0x0580 | VCountIRQEnable));

        AdvanceToLine(0x104);
        Assert.Equal(0u, ARM9.IO.Read32(IF));

        AdvanceToLine(0x105);
        Assert.Equal((uint)Interrupt.VCount, ARM9.IO.Read32(IF));
        Assert.Equal(VCountFlag, ARM9.IO.Read16(DISPSTAT) & VCountFlag);
    }

    [Fact]
    public void OnlyTheSettingsInDISPSTATCanBeWritten()
    {
        ARM9.IO.Write16(DISPSTAT, 0xFFFF);

        Assert.Equal(0xFFB8, ARM9.IO.Read16(DISPSTAT) & ~(VBlankFlag | HBlankFlag | VCountFlag));
        Assert.Equal(0, ARM7.IO.Read16(DISPSTAT) & 0xFFB8);
    }

    [Fact]
    public void LinesDrawnBeforeAWriteToVideoMemoryKeepWhatWasThere()
    {
        ARM9.IO.Write8(VRAMCNT_A, 0x80);
        ARM9.Memory.Write16(0x05000000, 0x1111);
        ARM9.Memory.Write16(0x06800000, 0x2222);
        ARM9.Memory.Write16(0x07000000, 0x3333);

        var frame = CaptureFrame(() =>
        {
            AdvanceToLine(80);
            Advance(CyclesUntilHBlank);
            ARM9.Memory.Write16(0x05000000, 0x4444);
            ARM9.Memory.Write16(0x06800000, 0x5555);
            ARM9.Memory.Write16(0x07000000, 0x6666);
        });

        Assert.Equal(0x1111, BitConverter.ToUInt16(frame.OnLine(80, VideoFrame.PalettesKilobyte)));
        Assert.Equal(0x4444, BitConverter.ToUInt16(frame.OnLine(81, VideoFrame.PalettesKilobyte)));
        Assert.Equal(0x2222, BitConverter.ToUInt16(frame.OnLine(80, 0)));
        Assert.Equal(0x5555, BitConverter.ToUInt16(frame.OnLine(191, 0)));
        Assert.Equal(0x3333, BitConverter.ToUInt16(frame.OnLine(0, VideoFrame.OAMKilobyte)));
        Assert.Equal(0x6666, BitConverter.ToUInt16(frame.OAM));
        Assert.Equal(0x5555, BitConverter.ToUInt16(frame.VRAM));
    }

    [Fact]
    public void EachLineKeepsBothEnginesRegistersWithVRAMCNTAndPOWCNT1()
    {
        ARM9.IO.Write16(BG0HOFS + EngineB, 0x0123);
        ARM9.IO.Write32(VRAMCNT_A, 0x84838281);
        ARM9.IO.Write16(VRAMCNT_H, 0x8381);
        ARM9.IO.Write16(POWCNT1, 0x0203);

        var frame = CaptureFrame(() => { });

        var line = frame.Lines.AsSpan(191 * VideoFrame.WordsPerLine, VideoFrame.WordsPerLine);
        Assert.Equal(0x0123u, line[EngineBWords + 4] & 0xFFFF);
        Assert.Equal(0x84838281u, line[VRAMCNTWord]);
        Assert.Equal(0x81000000u, line[VRAMCNTWord + 1]);
        Assert.Equal(0x83u, line[VRAMCNTWord + 2]);
        Assert.Equal(0x0203u, line[^1]);
    }

    [Fact]
    public void ReferencePointsReloadAtTheFirstLineAndMoveByPBAndPD()
    {
        ARM9.IO.Write32(BG2X, 0x0FFFFF00);
        ARM9.IO.Write16(BG2PB, 0x0080);

        var frame = CaptureFrame(() =>
        {
            AdvanceToLine(100);
            ARM9.IO.Write32(BG2X, 0x00001000);
        });

        Assert.Equal(-0x100, ReferencePoint(frame, 0));
        Assert.Equal(-0x100 + (99 * 0x80), ReferencePoint(frame, 99));
        Assert.Equal(0x1000, ReferencePoint(frame, 100));
        Assert.Equal(0x1000 + 0x80, ReferencePoint(frame, 101));
    }

    [Fact]
    public void AWindowTurnsOnAtY1AndOffAtY2()
    {
        ARM9.IO.Write16(WIN0V, (10 << 8) | 20);

        var frame = CaptureFrame(() => { });

        Assert.Equal(0u, ActiveWindows(frame, 9));
        Assert.Equal(1u, ActiveWindows(frame, 10));
        Assert.Equal(1u, ActiveWindows(frame, 19));
        Assert.Equal(0u, ActiveWindows(frame, 20));
    }

    [Fact]
    public void Y1BelowSevenTurnsTheWindowOnDuringVBlank()
    {
        ARM9.IO.Write16(WIN0V, (3 << 8) | 50);
        CaptureFrame(() => { });

        var frame = CaptureFrame(() => { });

        Assert.Equal(1u, ActiveWindows(frame, 0));
        Assert.Equal(1u, ActiveWindows(frame, 49));
        Assert.Equal(0u, ActiveWindows(frame, 50));
    }

    private static VideoFrame CaptureFrame(Action whileDrawing)
    {
        VideoFrame? captured = null;
        void OnFrame(VideoFrame frame) => captured = frame;

        Video.FrameFinished += OnFrame;
        try
        {
            AdvanceToNextFrame();
            whileDrawing();
            AdvanceToLine(Video.VisibleLines);
            return captured!;
        }
        finally
        {
            Video.FrameFinished -= OnFrame;
        }
    }

    private static void AdvanceToNextFrame()
    {
        if (ARM9.Scheduler.Cycles != FrameStart())
        {
            Advance(FrameStart() + CyclesPerFrame - ARM9.Scheduler.Cycles);
        }
    }

    private static int ReferencePoint(VideoFrame frame, int line) => (int)frame.Lines[(line * VideoFrame.WordsPerLine) + ReferencePointsWord];

    private static uint ActiveWindows(VideoFrame frame, int line) => frame.Lines[(line * VideoFrame.WordsPerLine) + ActiveWindowsWord];
}
