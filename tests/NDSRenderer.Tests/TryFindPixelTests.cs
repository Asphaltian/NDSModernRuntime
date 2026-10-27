namespace NDSRenderer.Tests;

public class TryFindPixelTests
{
    private static readonly FrameRenderer.ViewMargins NoMargins = new(0, 0);

    [Fact]
    public void APointOnTheBottomScreenFindsItsPixel()
    {
        Assert.True(FrameRenderer.FindPixel(10.5f, 200.5f, 256, 384, ScreenSizing.Even, NoMargins, 0, out int x, out int y));
        Assert.Equal((10, 200), (x, y));
    }

    [Fact]
    public void TheScreensAreCenteredAndScaledInAWideWindow()
    {
        Assert.True(FrameRenderer.FindPixel(256 + 20, 400, 1024, 768, ScreenSizing.Even, NoMargins, 0, out int x, out int y));
        Assert.Equal((10, 200), (x, y));
        Assert.False(FrameRenderer.FindPixel(100, 400, 1024, 768, ScreenSizing.Even, NoMargins, 0, out _, out _));
    }

    [Fact]
    public void TheBottomScreenAloneFillsTheWindow()
    {
        Assert.True(FrameRenderer.FindPixel(20, 20, 512, 384, ScreenSizing.BottomOnly, NoMargins, 0, out int x, out int y));
        Assert.Equal((10, 192 + 10), (x, y));
    }

    [Fact]
    public void MarginsHaveNoPixels()
    {
        var margins = new FrameRenderer.ViewMargins(40, 0);
        Assert.False(FrameRenderer.FindPixel(20, 20, 336, 192, ScreenSizing.TopOnly, margins, 0, out _, out _));
        Assert.True(FrameRenderer.FindPixel(50, 20, 336, 192, ScreenSizing.TopOnly, margins, 0, out int x, out int y));
        Assert.Equal((10, 20), (x, y));
    }

    [Fact]
    public void AScrolledViewStartsAtItsScroll()
    {
        Assert.True(FrameRenderer.FindPixel(20, 20, 512, 384, ScreenSizing.Scrolled, NoMargins, 100, out int x, out int y));
        Assert.Equal((10, 100 + 10), (x, y));
        Assert.True(FrameRenderer.FindPixel(20, 200, 512, 384, ScreenSizing.Scrolled, NoMargins, 100, out x, out y));
        Assert.Equal((10, 100 + 100), (x, y));
    }

    [Fact]
    public void AScrollBetweenLinesMovesThePictureByPartOfALine()
    {
        Assert.True(FrameRenderer.FindPixel(20, 20, 512, 384, ScreenSizing.Scrolled, NoMargins, 100.5f, out _, out int y));
        Assert.Equal(110, y);
        Assert.True(FrameRenderer.FindPixel(20, 21, 512, 384, ScreenSizing.Scrolled, NoMargins, 100.5f, out _, out y));
        Assert.Equal(111, y);
    }

    [Fact]
    public void ACroppedViewFillsTheWindowWithTheScreensOwnColumns()
    {
        Assert.True(FrameRenderer.FindPixel(40, 40, 1024, 576, ScreenSizing.Cropped, new(42, 0), 24, out int x, out int y));
        Assert.Equal((10, 24 + 10), (x, y));
        Assert.Equal(144, new FrameMargins { Width = 42 }.CroppedLines);
    }

    [Fact]
    public void ACroppedViewCanKeepPartOfTheMargins()
    {
        var margins = new FrameMargins { Width = 42, CroppedWidth = 27 };
        Assert.Equal(175, margins.CroppedLines);
        Assert.True(FrameRenderer.FindPixel(1024 / 2, 0, 1024, 576, ScreenSizing.Cropped, new(42, 27), 0, out int x, out _));
        Assert.Equal(128, x);
    }

    [Fact]
    public void AnEmptyWindowHasNoPixels()
    {
        Assert.False(FrameRenderer.FindPixel(0, 0, 1, 1, ScreenSizing.Even, NoMargins, 0, out _, out _));
    }
}
