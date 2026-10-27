namespace NDSRenderer.Tests;

public class ScreenSizingTests
{
    [Fact]
    public void AutoShowsTheScreenEngineAIsOn()
    {
        bool[] positions = new bool[3];

        Assert.Equal(ScreenSizing.BottomOnly, FrameRenderer.Resolve(ScreenSizing.Auto, positions, isEngineAOnTop: false));
        Assert.Equal(ScreenSizing.TopOnly, FrameRenderer.Resolve(ScreenSizing.Auto, positions, isEngineAOnTop: true));
    }

    [Fact]
    public void AutoShowsBothScreensWhileTheGameSwapsThemEveryFrame()
    {
        bool[] positions = new bool[3];
        FrameRenderer.Resolve(ScreenSizing.Auto, positions, isEngineAOnTop: true);
        FrameRenderer.Resolve(ScreenSizing.Auto, positions, isEngineAOnTop: false);

        Assert.Equal(ScreenSizing.Even, FrameRenderer.Resolve(ScreenSizing.Auto, positions, isEngineAOnTop: true));
    }

    [Fact]
    public void OtherSizingsStayAsTheyAre()
    {
        Assert.Equal(ScreenSizing.TopOnly, FrameRenderer.Resolve(ScreenSizing.TopOnly, new bool[3], isEngineAOnTop: false));
    }
}
