using NTRModern;

namespace NDSRenderer;

internal static class YSorting
{
    private const uint ManualSort = 1 << 0;

    public static List<SortedPolygon> Sort(IReadOnlyList<GeometryEngine.Polygon> input, IReadOnlyList<GeometryEngine.Vertex> vertices, uint? swapParameters)
    {
        var polygons = input.Select(polygon => SortedPolygon.From(polygon, vertices)).Where(polygon => !polygon.HasZeroW).ToList();
        var opaque = polygons.Where(polygon => !polygon.IsTranslucent).OrderBy(polygon => polygon.SortKey);
        var translucent = polygons.Where(polygon => polygon.IsTranslucent);
        bool isManual = ((swapParameters ?? 0) & ManualSort) != 0;
        return [.. opaque, .. isManual ? translucent : translucent.OrderBy(polygon => polygon.SortKey)];
    }
}

internal sealed record SortedPolygon(GeometryEngine.Polygon Polygon, int Top, int Bottom, int TopVertex, int BottomVertex, bool IsTranslucent, bool IsDot, bool HasZeroW)
{
    private const int TextureFormatShift = 26;
    private const int A3I5 = 1;
    private const int A5I3 = 6;
    private const int AlphaShift = 16;
    private const int Solid = 31;

    public int SortKey => (Bottom << 8) | Top;

    public static SortedPolygon From(GeometryEngine.Polygon polygon, IReadOnlyList<GeometryEngine.Vertex> vertices)
    {
        var first = vertices[polygon.Vertices[0]];
        int top = Video.VisibleLines, bottom = 0, bottomX = 0;
        int topVertex = 0, bottomVertex = 0;
        bool isDot = true, hasZeroW = false;
        for (int i = 0; i < polygon.Vertices.Length; i++)
        {
            var vertex = vertices[polygon.Vertices[i]];
            if (vertex.ScreenY < top)
            {
                (top, topVertex) = (vertex.ScreenY, i);
            }

            if (vertex.ScreenY > bottom || (vertex.ScreenY == bottom && vertex.ScreenX > bottomX))
            {
                (bottom, bottomX, bottomVertex) = (vertex.ScreenY, vertex.ScreenX, i);
            }

            isDot &= (vertex.ScreenX, vertex.ScreenY) == (first.ScreenX, first.ScreenY);
            hasZeroW |= vertex.W == 0;
        }

        int format = (int)(polygon.TextureParameters >> TextureFormatShift) & 7;
        int alpha = (int)(polygon.Attributes >> AlphaShift) & 0x1F;
        bool isTranslucent = format is A3I5 or A5I3 || alpha is > 0 and < Solid;
        return new SortedPolygon(polygon, top, bottom, topVertex, bottomVertex, isTranslucent, isDot, hasZeroW);
    }
}
