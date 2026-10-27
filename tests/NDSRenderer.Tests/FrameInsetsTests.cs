namespace NDSRenderer.Tests;

public class FrameInsetsTests
{
    [Fact]
    public void AFrameTakesUpToMaxInsets()
    {
        var insets = new FrameInsets();
        for (int index = 0; index < FrameInsets.Max; index++)
        {
            insets.Add(1, ScreenLayers.BG2, 0, 0, 8, 8, 0, 0, 1);
        }

        Assert.Throws<InvalidOperationException>(() => insets.Add(1, ScreenLayers.BG2, 0, 0, 8, 8, 0, 0, 1));
    }

    [Fact]
    public void ClearingShowsEveryLayerAgain()
    {
        var insets = new FrameInsets();
        var words = new uint[FrameInsets.Words];
        insets.Hide(0, ScreenLayers.Objects);
        insets.Cover(1, ScreenLayers.BG2 | ScreenLayers.BG3);
        insets.Add(0, ScreenLayers.Objects, 1, 2, 3, 4, 5.5f, 6, 0.5f);
        insets.CopyTo(words);
        Assert.Equal((1u, (uint)ScreenLayers.Objects, 0u), (words[0], words[1], words[2]));
        Assert.Equal((0u, (uint)(ScreenLayers.BG2 | ScreenLayers.BG3)), (words[3], words[4]));
        Assert.Equal(5.5f, BitConverter.UInt32BitsToSingle(words[5 + 5]));

        insets.Clear();
        insets.CopyTo(words);
        Assert.Equal((0u, 0u, 0u, 0u, 0u), (words[0], words[1], words[2], words[3], words[4]));
    }
}
