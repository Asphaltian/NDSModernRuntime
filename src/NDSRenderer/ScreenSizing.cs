namespace NDSRenderer;

/// <summary>Which screens the renderer shows, and how.</summary>
public enum ScreenSizing
{
    /// <summary>Both screens, with the top screen above the bottom one.</summary>
    Even,

    /// <summary>Only the top screen.</summary>
    TopOnly,

    /// <summary>Only the bottom screen.</summary>
    BottomOnly,

    /// <summary>
    /// Only the screen that the game draws 3D on. If the game keeps swapping which screen gets the 3D, you get both
    /// screens instead.
    /// </summary>
    Auto,

    /// <summary>
    /// A view that's one screen tall and scrolls between the top screen and the bottom one. Pass how far down to
    /// scroll to <see cref="FrameRenderer.Submit"/>, where 0 shows the top screen and 192 shows the bottom one.
    /// Please note that while both screens are in view, sprites on the top screen carry on past its bottom edge,
    /// so the bottom screen leaves out any sprites that start above it.
    /// </summary>
    Scrolled,

    /// <summary>
    /// Same as <see cref="Scrolled"/>, but it only shows <see cref="FrameMargins.CroppedWidth"/> past each side of the
    /// screens, and only as many lines as it takes to fill a window that's wider than that.
    /// <see cref="FrameMargins.CroppedLines"/> tells you how many lines that is.
    /// </summary>
    Cropped,
}
