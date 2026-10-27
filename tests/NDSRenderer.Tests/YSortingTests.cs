using NTRModern;

namespace NDSRenderer.Tests;

public class YSortingTests
{
    private const uint Solid = 31 << 16;
    private const uint Translucent = 16 << 16;
    private const uint ManualSort = 1;

    private readonly List<GeometryEngine.Vertex> _vertices = [];
    private readonly List<GeometryEngine.Polygon> _polygons = [];

    private void AddPolygon(uint attributes, int top, int bottom, int w = 0x1000)
    {
        int first = _vertices.Count;
        _vertices.Add(new GeometryEngine.Vertex(0, 0, 0, w, 0, 0, 0, 0, 0, false, 10, top));
        _vertices.Add(new GeometryEngine.Vertex(0, 0, 0, w, 0, 0, 0, 0, 0, false, 20, bottom));
        _vertices.Add(new GeometryEngine.Vertex(0, 0, 0, w, 0, 0, 0, 0, 0, false, 0, bottom));
        _polygons.Add(new GeometryEngine.Polygon(attributes | ((uint)_polygons.Count << 24), 0, 0, [first, first + 1, first + 2], true, [0, 0, 0], [w, w, w]));
    }

    private List<SortedPolygon> Sort(uint swapParameters) => YSorting.Sort(_polygons, _vertices, swapParameters);

    private int[] Order(uint swapParameters) => [.. Sort(swapParameters).Select(polygon => (int)(polygon.Polygon.Attributes >> 24))];

    [Fact]
    public void OpaquePolygonsComeFirstSortedByBottomThenTop()
    {
        AddPolygon(Translucent, 0, 10);
        AddPolygon(Solid, 20, 90);
        AddPolygon(Solid, 10, 50);
        AddPolygon(Solid, 5, 50);

        Assert.Equal([3, 2, 1, 0], Order(0));
    }

    [Fact]
    public void AutoSortSortsTranslucentPolygonsToo()
    {
        AddPolygon(Translucent, 0, 80);
        AddPolygon(Translucent, 0, 40);

        Assert.Equal([1, 0], Order(0));
    }

    [Fact]
    public void ManualSortKeepsTheGamesTranslucentOrder()
    {
        AddPolygon(Translucent, 0, 80);
        AddPolygon(Translucent, 0, 40);
        AddPolygon(Solid, 0, 60);
        AddPolygon(Solid, 0, 20);

        Assert.Equal([3, 2, 0, 1], Order(ManualSort));
    }

    [Fact]
    public void PolygonsWithAZeroWArentDrawn()
    {
        AddPolygon(Solid, 0, 50, w: 0);
        AddPolygon(Solid, 0, 192);

        Assert.Equal([1], Order(0));
    }

    [Fact]
    public void TheBottomVertexIsTheRightmostOfTheLowest()
    {
        AddPolygon(Solid, 0, 50);

        var polygon = Sort(0)[0];

        Assert.Equal((0, 1), (polygon.TopVertex, polygon.BottomVertex));
    }

    [Fact]
    public void APolygonWithAllVerticesOnOnePixelIsADot()
    {
        AddPolygon(Solid, 0, 50);
        _vertices.Add(new GeometryEngine.Vertex(0, 0, 0, 0x1000, 0, 0, 0, 0, 0, false, 7, 9));
        _polygons.Add(new GeometryEngine.Polygon(Solid | (1u << 24), 0, 0, [3, 3, 3], true, [0, 0, 0], [0x1000, 0x1000, 0x1000]));

        Assert.Equal([(1, true), (0, false)], Sort(0).Select(polygon => ((int)(polygon.Polygon.Attributes >> 24), polygon.IsDot)));
    }
}
