namespace NDSRenderer;

/// <summary>Layers of one 2D engine. You can combine them to pick several at once.</summary>
[Flags]
public enum ScreenLayers
{
    /// <summary>No layers.</summary>
    None = 0,

    /// <summary>BG0.</summary>
    BG0 = 1 << 0,

    /// <summary>BG1.</summary>
    BG1 = 1 << 1,

    /// <summary>BG2.</summary>
    BG2 = 1 << 2,

    /// <summary>BG3.</summary>
    BG3 = 1 << 3,

    /// <summary>Sprites.</summary>
    Objects = 1 << 4,
}

/// <summary>
/// <c>FrameInsets</c> lets you draw parts of the screens again somewhere else in the picture, at their own size and on
/// top of everything else. Each part only shows the layers you pick, so nothing behind them on their screen comes
/// along. Just like <see cref="FrameMargins"/>, you need to add them on every frame.
/// </summary>
/// <example>
/// <code>
/// // Draw the top screen's BG2 from line 32 down at half size over the top of the picture,
/// // and leave the bottom screen's sprites out of the picture
/// insets.Add(1, ScreenLayers.BG2, 0, 32, 256, 112, x: 64, y: 4, scale: 0.5f);
/// insets.Hide(0, ScreenLayers.Objects);
/// </code>
/// </example>
public sealed class FrameInsets
{
    /// <summary>The most parts you can add in one frame.</summary>
    public const int Max = 4;

    internal const int Words = HeaderWords + (Max * InsetWords);

    private const int HeaderWords = 5;
    private const int InsetWords = 8;

    private readonly uint[] _hidden = new uint[FrameMargins.Engines];
    private readonly uint[] _covered = new uint[FrameMargins.Engines];
    private readonly Inset[] _insets = new Inset[Max];
    private int _count;

    /// <summary>
    /// Draws the <paramref name="layers"/> of <paramref name="engine"/> (0 for engine A, 1 for engine B) that are
    /// inside the rectangle starting at <paramref name="sourceX"/>, <paramref name="sourceY"/> on its screen.
    /// <paramref name="x"/> and <paramref name="y"/> are where its top left corner goes, in screen pixels from the top
    /// left of the picture with the margins included, and <paramref name="scale"/> is how big it gets.
    /// <para>
    /// Keep in mind that a part drawn over the other engine's screen gets that screen's brightness, and that this
    /// throws if you've already added <see cref="Max"/> parts on this frame.
    /// </para>
    /// </summary>
    public void Add(int engine, ScreenLayers layers, int sourceX, int sourceY, int width, int height, float x, float y, float scale)
    {
        if (_count == Max)
        {
            throw new InvalidOperationException($"A frame can only have {Max} insets.");
        }

        _insets[_count++] = new Inset(engine, layers, sourceX, sourceY, width, height, x, y, scale);
    }

    /// <summary>Leaves the <paramref name="layers"/> of <paramref name="engine"/> out of the picture. Parts you've added with <see cref="Add"/> still show them.</summary>
    public void Hide(int engine, ScreenLayers layers)
    {
        _hidden[engine] |= (uint)layers;
    }

    /// <summary>
    /// Makes the <paramref name="layers"/> of <paramref name="engine"/> bigger, so they fill the whole width of the
    /// picture with the margins included. Their top and bottom get cut off. The rest of that screen is drawn on top of
    /// them at its own size.
    /// </summary>
    public void Cover(int engine, ScreenLayers layers)
    {
        _covered[engine] |= (uint)layers;
    }

    /// <summary>Throws away every part that you've added, and shows every layer again.</summary>
    public void Clear()
    {
        _count = 0;
        Array.Clear(_hidden);
        Array.Clear(_covered);
    }

    internal void CopyTo(Span<uint> words)
    {
        words.Clear();
        words[0] = (uint)_count;
        words[1] = _hidden[0];
        words[2] = _hidden[1];
        words[3] = _covered[0];
        words[4] = _covered[1];
        for (int index = 0; index < _count; index++)
        {
            var inset = _insets[index];
            var target = words[(HeaderWords + (index * InsetWords))..];
            target[0] = (uint)inset.Engine | ((uint)inset.Layers << 8);
            target[1] = (uint)inset.SourceX;
            target[2] = (uint)inset.SourceY;
            target[3] = (uint)inset.Width;
            target[4] = (uint)inset.Height;
            target[5] = BitConverter.SingleToUInt32Bits(inset.X);
            target[6] = BitConverter.SingleToUInt32Bits(inset.Y);
            target[7] = BitConverter.SingleToUInt32Bits(inset.Scale);
        }
    }

    private readonly record struct Inset(int Engine, ScreenLayers Layers, int SourceX, int SourceY, int Width, int Height, float X, float Y, float Scale);
}
