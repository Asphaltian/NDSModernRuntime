using NTRModern;

namespace LibRecomp.Tests;

public class FunctionsTests
{
    private const uint OverlayAddress = 0x022DC240;

    private static readonly RecompFunc Main = _ => { };
    private static readonly RecompFunc First = _ => { };
    private static readonly RecompFunc Second = _ => { };

    private static Functions Create()
    {
        var functions = new Functions();
        functions.Register(
            [
                (0x00004000, 0x02000000, 0x100, [(0x10, Main)]),
                (0x00010000, OverlayAddress, 0x200, [(0x0, First)]),
                (0x00010200, OverlayAddress, 0x100, [(0x40, Second)]),
            ],
            [1, -1, 2]);
        return functions;
    }

    [Fact]
    public void EverythingButOverlaysIsThereFromTheStart()
    {
        var functions = Create();

        Assert.Same(Main, functions.Lookup(0x02000010));
        Assert.Throws<InvalidOperationException>(() => functions.Lookup(OverlayAddress));
    }

    [Fact]
    public void TheThumbBitIsIgnored()
    {
        Assert.Same(Main, Create().Lookup(0x02000011));
    }

    [Fact]
    public void LoadingAnOverlayRegistersItsFunctionsWhereItWasLoaded()
    {
        var functions = Create();

        functions.LoadOverlays(0x00010000, OverlayAddress, 0x200);

        Assert.Same(First, functions.Lookup(OverlayAddress));
    }

    [Fact]
    public void OnlyOverlaysEntirelyInsideTheReadAreLoaded()
    {
        var functions = Create();

        functions.LoadOverlays(0x00010200, OverlayAddress, 0x80);

        Assert.Throws<InvalidOperationException>(() => functions.Lookup(OverlayAddress + 0x40));
    }

    [Fact]
    public void LoadingAnOverlayOverAnotherReplacesIt()
    {
        var functions = Create();
        functions.LoadOverlays(0x00010000, OverlayAddress, 0x200);

        functions.LoadOverlays(0x00010200, OverlayAddress, 0x100);

        Assert.Same(Second, functions.Lookup(OverlayAddress + 0x40));
        Assert.Throws<InvalidOperationException>(() => functions.Lookup(OverlayAddress));
    }

    [Fact]
    public void UnloadingRemovesEveryOverlayTheAreaTouches()
    {
        var functions = Create();
        functions.LoadOverlays(0x00010000, OverlayAddress, 0x200);

        functions.UnloadOverlays(OverlayAddress + 0x1FF, 1);

        Assert.Throws<InvalidOperationException>(() => functions.Lookup(OverlayAddress));
    }

    [Fact]
    public void AnIndirectJumpToNoFunctionUnwindsToWhereItIsResumed()
    {
        var functions = Create();
        var ctx = new RecompContext(Processor.ARM9);

        functions.IndirectJump(ctx, 0x02000081);
        Assert.True(functions.IsUnwinding);
        Assert.Throws<InvalidOperationException>(functions.ThrowIfUnwinding);

        Assert.False(functions.TryResumeAt(0x02000084));
        Assert.True(functions.TryResumeAt(0x02000080));
        Assert.False(functions.IsUnwinding);
    }

    [Fact]
    public void AnIndirectJumpToAFunctionCallsIt()
    {
        var functions = new Functions();
        var ctx = new RecompContext(Processor.ARM9);
        functions.Register([(0x00004000, 0x02000000, 0x100, [(0x0, c => c.R0 = 5)])], []);

        functions.IndirectJump(ctx, 0x02000000);

        Assert.Equal(5u, ctx.R0);
        Assert.False(functions.IsUnwinding);
    }

    [Fact]
    public void ReadingTheROMLoadsTheOverlaysItBringsIn()
    {
        ARM9.Recomp.RegisterSections([(0x00010000, OverlayAddress, 0x200, [(0x0, First)])], [0]);
        var ctx = new RecompContext(Processor.ARM9) { R1 = 0x00010000, R2 = OverlayAddress, R3 = 0x200 };
        bool read = false;

        ARM9.Recomp.ReadRom(ctx, _ => read = true);

        Assert.True(read);
        Assert.Same(First, ARM9.Recomp.LookupFunc(OverlayAddress));
    }
}
