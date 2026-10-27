using System.Runtime.InteropServices;

namespace NTRModern;

/// <summary>
/// <c>VideoFrame</c> holds everything you need to draw one frame of both screens. You get one from
/// <see cref="Video.FrameFinished"/>.
/// </summary>
public sealed class VideoFrame
{
    /// <summary>How many words each line takes up in <see cref="Lines"/>.</summary>
    public const int WordsPerLine = (2 * Engine2D.WordsPerLine) + VRAMCNTWords + 1;

    internal const int KilobyteSize = 0x400;
    internal const int VideoMemoryKilobytes = PalettesKilobyte + (2 * EngineKilobytes);
    internal const int PalettesKilobyte = NTRModern.VRAM.Size / KilobyteSize;
    internal const int OAMKilobyte = PalettesKilobyte + EngineKilobytes;

    private const int EngineKilobytes = 2;
    private const int VRAMCNTWords = 3;

    /// <summary>The display registers on each of the 192 visible lines, the way they were when that line was drawn.</summary>
    public readonly uint[] Lines = new uint[Video.VisibleLines * WordsPerLine];

    internal readonly byte[] LayoutOfLine = new byte[Video.VisibleLines];

    private readonly List<EarlierCopy> _earlierCopies = [];
    private readonly int[] _copiedAt = new int[VideoMemoryKilobytes];
    private int _linesCaptured;

    /// <summary>
    /// VRAM banks A to I at the end of the frame. They're one after another in the same order as their LCDC
    /// addresses, so bank A comes first.
    /// </summary>
    public ReadOnlySpan<byte> VRAM => Copies.AsSpan(0, NTRModern.VRAM.Size);

    /// <summary>Palette memory of both engines at the end of the frame.</summary>
    public ReadOnlySpan<byte> Palettes => Copies.AsSpan(PalettesKilobyte * KilobyteSize, EngineKilobytes * KilobyteSize);

    /// <summary>OAM of both engines at the end of the frame.</summary>
    public ReadOnlySpan<byte> OAM => Copies.AsSpan(OAMKilobyte * KilobyteSize, EngineKilobytes * KilobyteSize);

    internal byte[] Copies { get; private set; } = new byte[VideoMemoryKilobytes * KilobyteSize];

    internal int CopyCount { get; private set; } = VideoMemoryKilobytes;

    internal ushort[] Layouts { get; private set; } = [.. Enumerable.Range(0, VideoMemoryKilobytes).Select(kilobyte => (ushort)kilobyte)];

    internal int LayoutCount { get; private set; } = 1;

    internal RenderingInput Rendering { get; private set; } = new([], [], [], [], null, 0, new byte[RenderingEngine.ControlRegistersSize]);

    internal void CaptureRendering()
    {
        Rendering = new RenderingInput(
            GeometryEngine.RenderingVertices,
            GeometryEngine.RenderingPolygons,
            GeometryEngine.RenderingMarginVertices,
            GeometryEngine.RenderingMarginPolygons,
            GeometryEngine.RenderingSwapParameters,
            Engine2D.A.RegisterAt(Engine2D.DISP3DCNT),
            RenderingEngine.ControlRegisters.ToArray());
    }

    internal void CaptureLine(int line)
    {
        if (line == 0)
        {
            _earlierCopies.Clear();
            Array.Clear(_copiedAt);
            CopyCount = VideoMemoryKilobytes;
        }

        var words = Lines.AsSpan(line * WordsPerLine, WordsPerLine);
        Engine2D.A.CaptureLine(words[..Engine2D.WordsPerLine]);
        Engine2D.B.CaptureLine(words.Slice(Engine2D.WordsPerLine, Engine2D.WordsPerLine));

        var vramcnt = MemoryMarshal.AsBytes(words.Slice(2 * Engine2D.WordsPerLine, VRAMCNTWords));
        vramcnt.Clear();
        NTRModern.VRAM.VRAMCNT.CopyTo(vramcnt);
        words[^1] = ARM9.IO.GraphicsPowerControl;
        _linesCaptured = line + 1;
    }

    internal void CopyBeforeWrite(int kilobyte)
    {
        if (_linesCaptured == 0 || _copiedAt[kilobyte] == _linesCaptured)
        {
            return;
        }

        _copiedAt[kilobyte] = _linesCaptured;
        if ((CopyCount + 1) * KilobyteSize > Copies.Length)
        {
            Copies = [.. Copies, .. new byte[Copies.Length]];
        }

        VideoMemory(kilobyte).CopyTo(Copies.AsSpan(CopyCount * KilobyteSize));
        _earlierCopies.Add(new EarlierCopy(kilobyte, _linesCaptured, CopyCount));
        CopyCount++;
    }

    internal void CaptureMemory()
    {
        for (int kilobyte = 0; kilobyte < VideoMemoryKilobytes; kilobyte++)
        {
            VideoMemory(kilobyte).CopyTo(Copies.AsSpan(kilobyte * KilobyteSize));
        }

        Span<ushort> layout = stackalloc ushort[VideoMemoryKilobytes];
        for (int kilobyte = 0; kilobyte < VideoMemoryKilobytes; kilobyte++)
        {
            layout[kilobyte] = (ushort)kilobyte;
        }

        LayoutCount = 0;
        int next = _earlierCopies.Count - 1;
        for (int line = Video.VisibleLines - 1; line >= 0; line--)
        {
            bool changed = LayoutCount == 0;
            for (; next >= 0 && _earlierCopies[next].LinesBefore > line; next--)
            {
                layout[_earlierCopies[next].Kilobyte] = (ushort)_earlierCopies[next].Copy;
                changed = true;
            }

            if (changed)
            {
                if ((LayoutCount + 1) * VideoMemoryKilobytes > Layouts.Length)
                {
                    Layouts = [.. Layouts, .. new ushort[Layouts.Length]];
                }

                layout.CopyTo(Layouts.AsSpan(LayoutCount * VideoMemoryKilobytes));
                LayoutCount++;
            }

            LayoutOfLine[line] = (byte)(LayoutCount - 1);
        }

        _linesCaptured = 0;
    }

    internal ReadOnlySpan<byte> OnLine(int line, int kilobyte)
    {
        int copy = Layouts[(LayoutOfLine[line] * VideoMemoryKilobytes) + kilobyte];
        return Copies.AsSpan(copy * KilobyteSize, KilobyteSize);
    }

    private static Span<byte> VideoMemory(int kilobyte) => kilobyte switch
    {
        >= OAMKilobyte => ARM9.Memory.OAM.AsSpan((kilobyte - OAMKilobyte) * KilobyteSize, KilobyteSize),
        >= PalettesKilobyte => ARM9.Memory.Palettes.AsSpan((kilobyte - PalettesKilobyte) * KilobyteSize, KilobyteSize),
        _ => NTRModern.VRAM.Data.AsSpan(kilobyte * KilobyteSize, KilobyteSize),
    };

    internal sealed record RenderingInput(
        IReadOnlyList<GeometryEngine.Vertex> Vertices,
        IReadOnlyList<GeometryEngine.Polygon> Polygons,
        IReadOnlyList<GeometryEngine.Vertex> MarginVertices,
        IReadOnlyList<GeometryEngine.Polygon> MarginPolygons,
        uint? SwapParameters,
        ushort DISP3DCNT,
        byte[] ControlRegisters);

    private readonly record struct EarlierCopy(int Kilobyte, int LinesBefore, int Copy);
}
