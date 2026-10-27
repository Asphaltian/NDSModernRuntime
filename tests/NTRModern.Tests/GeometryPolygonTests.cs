namespace NTRModern.Tests;

public class GeometryPolygonTests
{
    private const uint MTX_MODE = 0x04000440;
    private const uint MTX_IDENTITY = 0x04000454;
    private const uint COLOR = 0x04000480;
    private const uint VTX_16 = 0x0400048C;
    private const uint VTX_10 = 0x04000490;
    private const uint VTX_DIFF = 0x040004A0;
    private const uint POLYGON_ATTR = 0x040004A4;
    private const uint TEXIMAGE_PARAM = 0x040004A8;
    private const uint BEGIN_VTXS = 0x04000500;
    private const uint SWAP_BUFFERS = 0x04000540;
    private const uint VIEWPORT = 0x04000580;
    private const uint GXSTAT = 0x04000600;
    private const uint RAM_COUNT = 0x04000604;
    private const uint DISP_1DOT_DEPTH = 0x04000610;
    private const uint DISP3DCNT = 0x04000060;
    private const uint BothSurfaces = (1 << 6) | (1 << 7);
    private const uint FrontOnly = 1 << 7;
    private const uint FarPlaneRendered = 1 << 12;
    private const uint OneDotRendered = 1 << 13;
    private const uint Solid = 31 << 16;
    private const uint Triangles = 0;
    private const uint TriangleStrips = 2;
    private const uint QuadStrips = 3;
    private const short Half = 2048;

    public GeometryPolygonTests()
    {
        Hardware.Reset(Model.DS);
        Video.Start(paced: false);
        ARM9.IO.Write16(DISP3DCNT, 0);
        ARM9.IO.Write32(MTX_MODE, 0);
        ARM9.IO.Write32(MTX_IDENTITY, 0);
        ARM9.IO.Write32(MTX_MODE, 2);
        ARM9.IO.Write32(MTX_IDENTITY, 0);
        ARM9.IO.Write32(TEXIMAGE_PARAM, 0);
        ARM9.IO.Write32(VIEWPORT, 0xBFFF0000);
        ARM9.IO.Write32(COLOR, 0x7FFF);
        ARM9.IO.Write32(DISP_1DOT_DEPTH, 0x7FFF);
        Swap();
    }

    private static void Swap(uint parameters = 0)
    {
        ARM9.IO.Write32(SWAP_BUFFERS, parameters);
        ARM9.Scheduler.Cycles = Video.NextVBlankAfter(ARM9.Scheduler.Cycles) + 1000;
        ARM9.IO.Read32(RAM_COUNT);
    }

    private static void Begin(uint primitive, uint attributes = BothSurfaces | FarPlaneRendered | OneDotRendered | Solid)
    {
        ARM9.IO.Write32(POLYGON_ATTR, attributes);
        ARM9.IO.Write32(BEGIN_VTXS, primitive);
    }

    private static void Vertex(short x, short y, short z = 0)
    {
        ARM9.IO.Write32(VTX_16, (uint)(ushort)x | ((uint)(ushort)y << 16));
        ARM9.IO.Write32(VTX_16, (ushort)z);
    }

    private static (uint Polygons, uint Vertices) Count()
    {
        ARM9.Scheduler.Cycles += 1_000_000;
        uint count = ARM9.IO.Read32(RAM_COUNT);
        return (count & 0xFFF, count >> 16);
    }

    [Fact]
    public void ATriangleInsideTheViewIsStored()
    {
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(Half, -Half);
        Vertex(0, Half);

        Assert.Equal((1u, 3u), Count());
    }

    [Fact]
    public void BackFacesAreHiddenUnlessEnabled()
    {
        Begin(Triangles, FrontOnly | Solid | OneDotRendered);
        Vertex(-Half, -Half);
        Vertex(0, Half);
        Vertex(Half, -Half);

        Assert.Equal((0u, 0u), Count());
    }

    [Fact]
    public void LineSegmentsAreAlwaysShown()
    {
        Begin(Triangles, Solid | OneDotRendered);
        Vertex(-Half, 0);
        Vertex(Half, 0);
        Vertex(Half, 0);

        Assert.Equal((1u, 3u), Count());
    }

    [Fact]
    public void StripsShareTheirVertices()
    {
        Begin(TriangleStrips);
        Vertex(-Half, -Half);
        Vertex(-Half, Half);
        Vertex(0, -Half);
        Vertex(0, Half);

        Assert.Equal((2u, 4u), Count());
    }

    [Fact]
    public void QuadStripsShareTheirVertices()
    {
        Begin(QuadStrips);
        Vertex(-Half, -Half);
        Vertex(-Half, Half);
        Vertex(0, -Half);
        Vertex(0, Half);
        Vertex(Half, -Half);
        Vertex(Half, Half);

        Assert.Equal((2u, 6u), Count());
    }

    [Fact]
    public void ClippingReplacesAnOutsideVertexWithTwo()
    {
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(6000, -Half);
        Vertex(0, Half);

        Assert.Equal((1u, 4u), Count());
    }

    [Fact]
    public void AnOutsideVertexIsReplacedByTheEdgesTowardsItsNeighbors()
    {
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(6000, -Half);
        Vertex(0, Half);

        Swap();

        Assert.Equal(
            [(-Half, -Half), (4096, -Half), (4096, -749), (0, Half)],
            GeometryEngine.RenderingVertices.Select(vertex => (vertex.X, vertex.Y)));
    }

    [Fact]
    public void VerticesKeepWhereTheyLandOnScreen()
    {
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(Half, -Half);
        Vertex(0, Half);

        Swap();

        var vertex = GeometryEngine.RenderingVertices[0];
        Assert.Equal((64, 144), (vertex.ScreenX, vertex.ScreenY));
    }

    [Fact]
    public void APolygonPastTheLeftEdgeIsKeptOnlyForTheMargins()
    {
        Begin(Triangles);
        Vertex(-8000, -Half);
        Vertex(-6000, -Half);
        Vertex(-7000, Half);

        Swap();

        Assert.Empty(GeometryEngine.RenderingPolygons);
        Assert.Single(GeometryEngine.RenderingMarginPolygons);
        Assert.Equal(-122, GeometryEngine.RenderingMarginVertices[0].ScreenX);
    }

    [Fact]
    public void APolygonCrossingTheEdgeCarriesOnForTheMargins()
    {
        Begin(Triangles);
        Vertex(-8000, -Half);
        Vertex(Half, -Half);
        Vertex(0, Half);

        Swap();

        Assert.Equal(0, GeometryEngine.RenderingVertices.Min(vertex => vertex.ScreenX));
        Assert.Equal(-122, GeometryEngine.RenderingMarginVertices.Min(vertex => vertex.ScreenX));
    }

    [Fact]
    public void APolygonOnScreenIsTheSameForTheMargins()
    {
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(Half, -Half);
        Vertex(0, Half);

        Swap();

        Assert.Equal(
            GeometryEngine.RenderingVertices.Select(vertex => (vertex.ScreenX, vertex.ScreenY)),
            GeometryEngine.RenderingMarginVertices.Select(vertex => (vertex.ScreenX, vertex.ScreenY)));
    }

    [Fact]
    public void AVertexBetweenTwoLinesLandsOnTheUpperOne()
    {
        Begin(Triangles);
        Vertex(-Half, 1);
        Vertex(Half, 1);
        Vertex(0, Half);

        Swap();

        Assert.Equal(95, GeometryEngine.RenderingVertices[0].ScreenY);
    }

    [Fact]
    public void ZBufferingKeepsDepthsFromZAndW()
    {
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(Half, -Half);
        Vertex(0, Half);

        Swap();

        var polygon = GeometryEngine.RenderingPolygons[0];
        Assert.Equal(0x7FFE00, polygon.Depths[0]);
        Assert.Equal(4096, polygon.NormalizedWs[0]);
    }

    [Fact]
    public void AListUsesTheSwapBitsInEffectWhenItsPolygonsWereSent()
    {
        Swap(2);
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(Half, -Half);
        Vertex(0, Half);

        Swap(1);

        Assert.Equal(2u, GeometryEngine.RenderingSwapParameters);
        Assert.Equal(4096, GeometryEngine.RenderingPolygons[0].Depths[0]);
    }

    [Fact]
    public void PolygonsFullyOutsideAreDropped()
    {
        Begin(Triangles);
        Vertex(5000, -Half);
        Vertex(7000, -Half);
        Vertex(6000, Half);

        Assert.Equal((0u, 0u), Count());
    }

    [Fact]
    public void FarPlaneCrossingIsHiddenUnlessEnabled()
    {
        Begin(Triangles, BothSurfaces | Solid | OneDotRendered);
        Vertex(-Half, -Half);
        Vertex(Half, -Half, 6000);
        Vertex(0, Half);

        Assert.Equal((0u, 0u), Count());
    }

    [Fact]
    public void ClippedStripPolygonsAreStoredSeparately()
    {
        Begin(TriangleStrips);
        Vertex(-Half, -Half);
        Vertex(-Half, Half);
        Vertex(0, -Half);
        Vertex(6000, Half);

        Assert.Equal((2u, 3u + 4u), Count());
    }

    [Fact]
    public void OneDotPolygonsBehindTheDepthAreHidden()
    {
        ARM9.IO.Write32(DISP_1DOT_DEPTH, 0);
        Begin(Triangles, BothSurfaces | FarPlaneRendered | Solid);
        Vertex(0, 0);
        Vertex(1, 0);
        Vertex(0, -1);

        Assert.Equal((0u, 0u), Count());
    }

    [Fact]
    public void OneDotPolygonsAtTheDepthAreShown()
    {
        ARM9.IO.Write32(DISP_1DOT_DEPTH, 4096 >> 9);
        Begin(Triangles, BothSurfaces | FarPlaneRendered | Solid);
        Vertex(0, 0);
        Vertex(1, 0);
        Vertex(0, -1);

        Assert.Equal((1u, 3u), Count());
    }

    [Fact]
    public void TheOneDotDepthStartsAtTheMostDistant()
    {
        Hardware.Reset(Model.DS);

        Assert.Equal(0x7FFF, GeometryEngine.OneDotDepth);
    }

    [Fact]
    public void RAMOverflowIsFlaggedInDISP3DCNT()
    {
        Begin(TriangleStrips);
        for (int i = 0; i < 2051; i++)
        {
            Vertex((short)(i % 2 == 0 ? -Half : Half), (short)((i % 4 < 2) ? -Half : Half));
        }

        Assert.Equal(2048u, Count().Polygons);
        Assert.Equal(1 << 13, ARM9.IO.Read16(DISP3DCNT) & (1 << 13));
    }

    [Fact]
    public void SwappingHandsTheListToTheRenderer()
    {
        ARM9.IO.Write32(COLOR, 31 | (1 << 10));
        Begin(Triangles);
        Vertex(-Half, -Half);
        Vertex(Half, -Half);
        Vertex(0, Half);

        Swap();

        Assert.Equal((0u, 0u), Count());
        Assert.Single(GeometryEngine.RenderingPolygons);
        var vertex = GeometryEngine.RenderingVertices[0];
        Assert.Equal((-Half, -Half, 4096), (vertex.X, vertex.Y, vertex.W));
        Assert.Equal((63, 0, 3), (vertex.Red, vertex.Green, vertex.Blue));
    }

    [Fact]
    public void ShortVertexFormatsDecodeToTwelveBitFractions()
    {
        Begin(Triangles);
        ARM9.IO.Write32(VTX_10, 0x010u | (0x3C0u << 10));
        ARM9.IO.Write32(VTX_DIFF, 0x3FFu);
        Vertex(0, Half);

        Swap();

        var (first, second) = (GeometryEngine.RenderingVertices[0], GeometryEngine.RenderingVertices[1]);
        Assert.Equal((0x010 << 6, -0x40 << 6), (first.X, first.Y));
        Assert.Equal(((0x010 << 6) - 1, -0x40 << 6), (second.X, second.Y));
    }

    [Fact]
    public void TheVertexColorIsUnknownUntilSet()
    {
        Hardware.Reset(Model.DS);
        ARM9.IO.Write32(MTX_MODE, 0);
        ARM9.IO.Write32(MTX_IDENTITY, 0);
        ARM9.IO.Write32(MTX_MODE, 2);
        ARM9.IO.Write32(MTX_IDENTITY, 0);
        ARM9.IO.Write32(TEXIMAGE_PARAM, 0);
        Begin(Triangles);

        Assert.Throws<NotSupportedException>(() =>
        {
            Vertex(0, 0);
            ARM9.Scheduler.Cycles += 1000;
            ARM9.IO.Read32(GXSTAT);
        });
    }

    [Fact]
    public void SwappingMidPolygonLocksUpTheHardware()
    {
        Begin(Triangles);
        Vertex(-Half, -Half);

        Assert.Throws<NotSupportedException>(() =>
        {
            ARM9.IO.Write32(SWAP_BUFFERS, 0);
            Count();
        });
    }
}
