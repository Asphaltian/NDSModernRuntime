using NTRModern;

namespace NDSRenderer;

/// <summary>
/// <c>FrameMargins</c> are extra columns of tiles on each side of the screens, for when the window is wider than the
/// DS. Set <see cref="Width"/>, and then fill in the columns with <see cref="Supply"/> on every frame. Engine 0 is
/// engine A, and engine 1 is engine B.
/// <para>
/// Keep in mind that a background stops at the edge of the screen if you don't supply any columns for it, and that
/// sprites only show up in the margins over tiles you've supplied. 3D shows up wherever the game draws it.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Every frame, carry engine A's BG3 on past both edges of the screen
/// for (int i = 0; i &lt; margins.ColumnsShown; i++)
/// {
///     CopyMapColumn(margins.Supply(0, 3, -i), firstColumn - i);
///     CopyMapColumn(margins.Supply(0, 3, 32 + i), firstColumn + 32 + i);
/// }
/// </code>
/// </example>
public sealed class FrameMargins
{
    /// <summary>The most columns you can supply on each side.</summary>
    public const int ColumnsPerSide = 12;

    /// <summary>How many tiles there are in each column, counting down from the top of the screen.</summary>
    public const int Rows = 32;

    /// <summary>The largest <see cref="Width"/> you can set, in pixels.</summary>
    public const int MaxWidth = ((ColumnsPerSide - 2) * 8) - 8;

    internal const uint NoWindow = 0x80008000;
    internal const int Engines = 2;
    internal const int Backgrounds = 4;
    internal const int Windows = 2;

    private const int ColumnsPerBackground = 2 * ColumnsPerSide;
    private const int ScreenColumns = 32;

    internal readonly ushort[] Tiles = new ushort[Engines * Backgrounds * ColumnsPerBackground * Rows];

    internal readonly uint[] SuppliedColumns = new uint[Engines * Backgrounds];

    internal readonly uint[] WindowEdges = [.. Enumerable.Repeat(NoWindow, Engines * Windows * Video.VisibleLines)];

    private int _width;
    private int _croppedWidth;

    /// <summary>How many pixels to show past each side of the screen, up to <see cref="MaxWidth"/>.</summary>
    public int Width
    {
        get => _width;
        set => _width = Math.Clamp(value, 0, MaxWidth);
    }

    /// <summary>How many columns you need to supply on each side to cover <see cref="Width"/>.</summary>
    public int ColumnsShown => _width == 0 ? 0 : ((_width + 7) / 8) + 2;

    /// <summary>
    /// How many pixels past each side of the screen <see cref="ScreenSizing.Cropped"/> shows, up to
    /// <see cref="Width"/>. Set it to how far the scene's art reaches, so the picture is exactly as wide as the art.
    /// Please note that it goes back to 0 when you call <see cref="Clear"/>.
    /// </summary>
    public int CroppedWidth
    {
        get => Math.Min(_croppedWidth, _width);
        set => _croppedWidth = Math.Max(value, 0);
    }

    /// <summary>How many lines <see cref="ScreenSizing.Cropped"/> shows for this <see cref="Width"/> and <see cref="CroppedWidth"/>.</summary>
    public int CroppedLines => LinesCroppedFor(_width, CroppedWidth);

    internal static int LinesCroppedFor(int width, int croppedWidth) => Video.VisibleLines * (FrameRenderer.Width + (2 * croppedWidth)) / (FrameRenderer.Width + (2 * width));

    /// <summary>
    /// Gives you one column of a background to fill with tilemap entries, starting from the top of the screen.
    /// Column 0 is the one with the screen's top left pixel, so the columns on the left go 0, -1, -2 and so on,
    /// and the ones on the right go 32, 33, 34 and so on.
    /// </summary>
    public Span<ushort> Supply(int engine, int bg, int column)
    {
        int index = column <= 0 ? -column : column - ScreenColumns + ColumnsPerSide;
        SuppliedColumns[(engine * Backgrounds) + bg] |= 1u << index;
        return Tiles.AsSpan(((((engine * Backgrounds) + bg) * ColumnsPerBackground) + index) * Rows, Rows);
    }

    /// <summary>
    /// Sets how far window 0 or 1 reaches into the margins on one line. Just like the columns, you need to do this
    /// on every frame. <paramref name="x1"/> and <paramref name="x2"/> are X1 and X2 of WIN0H or WIN1H, except that
    /// they can go past 0 and 256. On lines you don't supply, the window carries on the same way it is at the edge
    /// of the screen.
    /// </summary>
    /// <example>
    /// <code>
    /// // On line 40, engine A's window 1 covers everything left of -20 and everything from 270 on
    /// margins.SupplyWindow(0, window: 1, line: 40, x1: 270, x2: -20);
    /// </code>
    /// </example>
    public void SupplyWindow(int engine, int window, int line, int x1, int x2)
    {
        WindowEdges[(((engine * Windows) + window) * Video.VisibleLines) + line] = (ushort)x1 | ((uint)(ushort)x2 << 16);
    }

    /// <summary>Throws away every column and window that you've supplied, and sets <see cref="CroppedWidth"/> back to 0.</summary>
    public void Clear()
    {
        Array.Clear(SuppliedColumns);
        Array.Fill(WindowEdges, NoWindow);
        _croppedWidth = 0;
    }
}
