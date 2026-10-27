namespace NTRModern;

internal static partial class GeometryEngine
{
    private const int MaxPolygons = 2048;
    private const int MaxVertices = 6144;
    private const int Triangles = 0;
    private const int Quads = 1;
    private const int TriangleStrips = 2;
    private const int QuadStrips = 3;
    private const uint BackSurface = 1 << 6;
    private const uint FrontSurface = 1 << 7;
    private const uint FarPlaneIntersecting = 1 << 12;
    private const uint OneDotPolygonsBehind = 1 << 13;
    private const int TextureFormatShift = 26;
    private const int DirectTexture = 7;
    private const int TransformationModeShift = 30;
    private const int TexCoordSource = 1;
    private const int VertexSource = 3;
    private const int OneDotDepthToW = 9;
    private const long RAMCountResetDelay = 10;
    private const uint WBuffering = 1 << 1;
    private const int ScreenWMask = 0xFFFFFF;
    private const int WSizeStep = 4;
    private const int NormalizedWBits = 16;
    private const long ZScale = 0x4000;
    private const long ZBias = 0x3FFF;
    private const long DepthScale = 0x200;
    private const long ZeroWDepth = 0x7FFE00;
    private const long MaxDepth = 0xFFFFFF;
    private const int ColorFractionBits = 12;
    private const int ColorFraction = (1 << ColorFractionBits) - 1;
    private const long MarginReach = 256;

    private static readonly List<Vertex> VertexRAM = new(MaxVertices);
    private static readonly List<Polygon> PolygonRAM = new(MaxPolygons);
    private static readonly List<Vertex> MarginVertices = new(MaxVertices);
    private static readonly List<Polygon> MarginPolygons = new(MaxPolygons);
    private static readonly (Vertex Vertex, int Index)[] Pending = new (Vertex, int)[4];

    private static int? _primitive;
    private static uint _attributesInUse;
    private static int _pending;
    private static int _stripPolygons;
    private static (byte Red, byte Green, byte Blue)? _color;
    private static (short S, short T)? _rawTexCoord;
    private static (short S, short T)? _texCoord;
    private static (short X, short Y, short Z)? _position;
    private static bool _isRAMCountKnown;
    private static long? _ramCountResetAt;
    private static uint? _listSwapParameters;

    public static IReadOnlyList<Vertex> RenderingVertices { get; private set; } = [];

    public static IReadOnlyList<Polygon> RenderingPolygons { get; private set; } = [];

    public static uint? RenderingSwapParameters { get; private set; }

    public static IReadOnlyList<Vertex> RenderingMarginVertices { get; private set; } = [];

    public static IReadOnlyList<Polygon> RenderingMarginPolygons { get; private set; } = [];

    private static int TextureFormat => (int)((TextureParameters ?? throw Unknown("TEXIMAGE_PARAM")) >> TextureFormatShift) & 7;

    private static int TransformationMode => (int)((TextureParameters ?? throw Unknown("TEXIMAGE_PARAM")) >> TransformationModeShift);

    private static void ResetPolygons()
    {
        VertexRAM.Clear();
        PolygonRAM.Clear();
        MarginVertices.Clear();
        MarginPolygons.Clear();
        (RenderingVertices, RenderingPolygons, RenderingSwapParameters, _listSwapParameters) = ([], [], null, null);
        (RenderingMarginVertices, RenderingMarginPolygons) = ([], []);
        (_primitive, _attributesInUse, _pending, _stripPolygons) = (null, 0, 0, 0);
        (_color, _rawTexCoord, _texCoord, _position) = (null, null, null, null);
        (_isRAMCountKnown, _ramCountResetAt) = (false, null);
    }

    private static uint ReadRAMCount() => _isRAMCountKnown
        ? (uint)PolygonRAM.Count | ((uint)VertexRAM.Count << 16)
        : throw new NotSupportedException("The ARM9 read RAM_COUNT before the first SWAP_BUFFERS.");

    private static NotSupportedException Unknown(string state)
    {
        return new NotSupportedException($"The geometry engine used {state} before the game set it.");
    }

    private static void SetColor(int parameter)
    {
        _color = (Expand(parameter & 0x1F), Expand((parameter >> 5) & 0x1F), Expand((parameter >> 10) & 0x1F));

        static byte Expand(int value) => (byte)((value * 2) + ((value + 31) / 32));
    }

    private static void SetTexCoord(int parameter)
    {
        _rawTexCoord = ((short)parameter, (short)(parameter >> 16));
        switch (TransformationMode)
        {
            case 0:
                _texCoord = _rawTexCoord;
                break;
            case TexCoordSource:
                int[] m = KnownTexture().Values;
                long s = (short)parameter;
                long t = (short)(parameter >> 16);
                _texCoord = ((short)(((s * m[0]) + (t * m[4]) + m[8] + m[12]) >> 12), (short)(((s * m[1]) + (t * m[5]) + m[9] + m[13]) >> 12));
                break;
            default:
                _texCoord = null;
                break;
        }
    }

    private static Matrix KnownTexture() => Texture.IsKnown ? Texture : throw Unknown("the texture matrix");

    private static void BeginVertexList(int parameter)
    {
        _attributesInUse = PolygonAttributes ?? throw Unknown("POLYGON_ATTR");
        _primitive = parameter & 3;
        (_pending, _stripPolygons) = (0, 0);
    }

    private static void SetVertex(byte command, ReadOnlySpan<int> parameters)
    {
        int primitive = _primitive ?? throw new NotSupportedException("The geometry engine got a vertex before BEGIN_VTXS.");
        int p = parameters[0];
        _position = command switch
        {
            VTX_16 => ((short)p, (short)(p >> 16), (short)parameters[1]),
            VTX_10 => (TenBits(p, 0, 6), TenBits(p, 10, 6), TenBits(p, 20, 6)),
            VTX_XY => ((short)p, (short)(p >> 16), KnownPosition().Z),
            VTX_XZ => ((short)p, KnownPosition().Y, (short)(p >> 16)),
            VTX_YZ => (KnownPosition().X, (short)p, (short)(p >> 16)),
            _ => ((short)(KnownPosition().X + TenBits(p, 0, 0)), (short)(KnownPosition().Y + TenBits(p, 10, 0)), (short)(KnownPosition().Z + TenBits(p, 20, 0))),
        };

        Pending[_pending++] = (Transform(_position.Value), -1);
        switch (primitive)
        {
            case Triangles when _pending == 3:
                Submit([0, 1, 2]);
                _pending = 0;
                break;
            case Quads when _pending == 4:
                Submit([0, 1, 2, 3]);
                _pending = 0;
                break;
            case TriangleStrips when _pending == 3:
                Submit(_stripPolygons % 2 == 0 ? [0, 1, 2] : [1, 0, 2]);
                (Pending[0], Pending[1], _pending) = (Pending[1], Pending[2], 2);
                _stripPolygons++;
                break;
            case QuadStrips when _pending == 4:
                Submit([0, 1, 3, 2]);
                (Pending[0], Pending[1], _pending) = (Pending[2], Pending[3], 2);
                _stripPolygons++;
                break;
        }

        static short TenBits(int value, int shift, int fractionWidening) => (short)(((value << (22 - shift)) >> 22) << fractionWidening);
    }

    private static (short X, short Y, short Z) KnownPosition() => _position ?? throw Unknown("the last vertex position");

    private static Vertex Transform((short X, short Y, short Z) position)
    {
        var clip = ClipMatrix();
        if (!clip.IsKnown)
        {
            throw Unknown("the position or projection matrix");
        }

        int[] m = clip.Values;
        long x = position.X;
        long y = position.Y;
        long z = position.Z;
        int Row(int column) => (int)(((x * m[column]) + (y * m[4 + column]) + (z * m[8 + column]) + ((long)One * m[12 + column])) >> 12);

        var color = _color ?? throw Unknown("the vertex color");
        var texCoord = TransformationMode == VertexSource ? VertexSourceTexCoord(position) : _texCoord;
        return new Vertex(Row(0), Row(1), Row(2), Row(3), color.Red, color.Green, color.Blue, texCoord?.S ?? 0, texCoord?.T ?? 0, texCoord is not null);
    }

    private static (short S, short T) VertexSourceTexCoord((short X, short Y, short Z) position)
    {
        int[] m = KnownTexture().Values;
        var raw = _rawTexCoord ?? throw Unknown("TEXCOORD");
        long S(int column, short start) => ((position.X * (long)m[column]) + (position.Y * (long)m[4 + column]) + (position.Z * (long)m[8 + column]) + ((long)start << 20)) >> 20;
        return ((short)S(0, raw.S), (short)S(1, raw.T));
    }

    private static void Submit(int[] order)
    {
        var polygon = order.Select(index => Pending[index].Vertex).ToList();
        Int128 facing = Facing(polygon);
        bool isShown = facing == 0 || (_attributesInUse & (facing > 0 ? FrontSurface : BackSurface)) != 0;
        if (!isShown || ((_attributesInUse & FarPlaneIntersecting) == 0 && polygon.Any(vertex => vertex.Z > vertex.W)))
        {
            Forget();
            return;
        }

        AddMarginPolygon(polygon, facing >= 0);

        bool isClipped = polygon.Any(vertex => IsOutside(vertex, Reach.Screen));
        if (isClipped)
        {
            polygon = Clip(polygon, Reach.Screen);
        }

        if (polygon.Count == 0)
        {
            Forget();
            return;
        }

        uint viewport = Viewport ?? throw Unknown("VIEWPORT");
        polygon = [.. polygon.Select(vertex => OnScreen(vertex, viewport))];
        if (IsHiddenOneDot(polygon))
        {
            Forget();
            return;
        }

        int[] shared = isClipped ? [] : [.. order.Where(index => Pending[index].Index >= 0)];
        if (PolygonRAM.Count >= MaxPolygons || VertexRAM.Count + polygon.Count - shared.Length > MaxVertices)
        {
            Engine2D.A.SetPolygonVertexRAMOverflow();
            Forget();
            return;
        }

        uint textureParameters = TextureParameters ?? throw Unknown("TEXIMAGE_PARAM");
        int format = TextureFormat;
        if (format != 0 && polygon.Any(vertex => !vertex.HasTexCoord))
        {
            throw Unknown("TEXCOORD");
        }

        uint paletteBase = format is 0 or DirectTexture ? 0 : PaletteBase ?? throw Unknown("PLTT_BASE");
        bool usesWBuffer = ((SwapParameters ?? throw Unknown("the SWAP_BUFFERS parameters")) & WBuffering) != 0;
        int[] indices = new int[polygon.Count];
        for (int i = 0; i < polygon.Count; i++)
        {
            int pendingIndex = isClipped ? -1 : order[i];
            if (pendingIndex >= 0 && Pending[pendingIndex].Index >= 0)
            {
                indices[i] = Pending[pendingIndex].Index;
                continue;
            }

            indices[i] = VertexRAM.Count;
            VertexRAM.Add(polygon[i]);
            if (pendingIndex >= 0)
            {
                Pending[pendingIndex].Index = indices[i];
            }
        }

        if (isClipped)
        {
            Forget();
        }

        var (depths, ws) = Depths(polygon, usesWBuffer);
        PolygonRAM.Add(new Polygon(_attributesInUse, textureParameters, paletteBase, indices, facing >= 0, depths, ws));
    }

    private static void AddMarginPolygon(List<Vertex> polygon, bool isFrontFacing)
    {
        if (Viewport is not { } viewport || TextureParameters is not { } textureParameters || SwapParameters is not { } swapParameters)
        {
            return;
        }

        int format = (int)(textureParameters >> TextureFormatShift) & 7;
        uint? paletteBase = format is 0 or DirectTexture ? 0 : PaletteBase;
        if (paletteBase is null || (format != 0 && polygon.Any(vertex => !vertex.HasTexCoord)))
        {
            return;
        }

        long viewportWidth = ((viewport >> 16) & 0xFF) - (viewport & 0xFF) + 1;
        var reach = new Reach(viewportWidth + (2 * MarginReach), viewportWidth);
        var clipped = polygon.Any(vertex => IsOutside(vertex, reach)) ? Clip(polygon, reach) : polygon;
        if (clipped.Count == 0)
        {
            return;
        }

        clipped = [.. clipped.Select(vertex => OnScreen(vertex, viewport))];
        if (IsHiddenOneDot(clipped) || MarginPolygons.Count >= MaxPolygons || MarginVertices.Count + clipped.Count > MaxVertices)
        {
            return;
        }

        int[] indices = new int[clipped.Count];
        for (int i = 0; i < clipped.Count; i++)
        {
            indices[i] = MarginVertices.Count;
            MarginVertices.Add(clipped[i]);
        }

        var (depths, ws) = Depths(clipped, (swapParameters & WBuffering) != 0);
        MarginPolygons.Add(new Polygon(_attributesInUse, textureParameters, paletteBase.Value, indices, isFrontFacing, depths, ws));
    }

    private static void Forget()
    {
        for (int i = 0; i < Pending.Length; i++)
        {
            Pending[i].Index = -1;
        }
    }

    private static Int128 Facing(List<Vertex> polygon)
    {
        var (a, b, c) = (polygon[0], polygon[1], polygon[2]);
        return (a.X * (((Int128)b.Y * c.W) - ((Int128)c.Y * b.W)))
            - (a.Y * (((Int128)b.X * c.W) - ((Int128)c.X * b.W)))
            + (a.W * (((Int128)b.X * c.Y) - ((Int128)c.X * b.Y)));
    }

    private static bool IsOutside(Vertex vertex, Reach reachX)
    {
        var clipVertex = ClipVertex.From(vertex);
        return clipVertex.IsOutside(0, 1, reachX) || clipVertex.IsOutside(0, -1, reachX) || Math.Abs((long)vertex.Y) > vertex.W || Math.Abs((long)vertex.Z) > vertex.W;
    }

    private static List<Vertex> Clip(List<Vertex> polygon, Reach reachX)
    {
        var vertices = polygon.Select(ClipVertex.From).ToList();
        foreach (int axis in (int[])[2, 1, 0])
        {
            var reach = axis == 0 ? reachX : Reach.Screen;
            vertices = [.. ClipAgainst(ClipAgainst(vertices, axis, 1, reach), axis, -1, reach).Select(vertex => vertex.WithWholeColors())];
        }

        return [.. vertices.Select(vertex => vertex.ToVertex())];
    }

    private static List<ClipVertex> ClipAgainst(List<ClipVertex> vertices, int axis, int side, Reach reach)
    {
        var clipped = new List<ClipVertex>(vertices.Count + 2);
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (!vertex.IsOutside(axis, side, reach))
            {
                clipped.Add(vertex);
                continue;
            }

            foreach (var neighbor in (ClipVertex[])[vertices[(i + vertices.Count - 1) % vertices.Count], vertices[(i + 1) % vertices.Count]])
            {
                if (!neighbor.IsOutside(axis, side, reach))
                {
                    clipped.Add(vertex.TowardsInside(neighbor, axis, side, reach));
                }
            }
        }

        return clipped;
    }

    private static Vertex OnScreen(Vertex vertex, uint viewport)
    {
        int w = vertex.W & ScreenWMask;
        if (w == 0)
        {
            return vertex with { W = 0, ScreenX = 0, ScreenY = 0 };
        }

        long x1 = viewport & 0xFF;
        long y1 = (viewport >> 8) & 0xFF;
        long x2 = (viewport >> 16) & 0xFF;
        long y2 = viewport >> 24;
        if (x2 < x1 || y2 < y1)
        {
            throw new NotSupportedException($"The geometry engine used the inverted VIEWPORT 0x{viewport:X8}.");
        }

        long x = ((vertex.X + (long)w) * (x2 - x1 + 1) / (2L * w)) + x1;
        long line = (((long)w - vertex.Y) * (y2 - y1 + 1) / (2L * w)) + (Video.VisibleLines - 1 - y2);
        return vertex with { W = w, ScreenX = (int)x, ScreenY = (int)line };
    }

    private static bool IsHiddenOneDot(List<Vertex> polygon)
    {
        if ((_attributesInUse & OneDotPolygonsBehind) != 0)
        {
            return false;
        }

        long depth = (long)OneDotDepth << OneDotDepthToW;
        return polygon.All(vertex => (vertex.ScreenX, vertex.ScreenY) == (polygon[0].ScreenX, polygon[0].ScreenY) && vertex.W > depth);
    }

    private static (int[] Depths, int[] NormalizedWs) Depths(List<Vertex> polygon, bool usesWBuffer)
    {
        int size = 0;
        foreach (var vertex in polygon)
        {
            while (((uint)vertex.W >> size) != 0)
            {
                size += WSizeStep;
            }
        }

        int[] depths = new int[polygon.Count];
        int[] normalizedWs = new int[polygon.Count];
        for (int i = 0; i < polygon.Count; i++)
        {
            var vertex = polygon[i];
            (int w, int shifted) = size < NormalizedWBits
                ? (vertex.W << (NormalizedWBits - size), vertex.W)
                : (vertex.W >> (size - NormalizedWBits), (vertex.W >> (size - NormalizedWBits)) << (size - NormalizedWBits));
            long depth = usesWBuffer ? shifted
                : vertex.W != 0 ? ((((long)vertex.Z * ZScale) / vertex.W) + ZBias) * DepthScale
                : ZeroWDepth;
            depths[i] = (int)Math.Clamp(depth, 0, MaxDepth);
            normalizedWs[i] = w;
        }

        return (depths, normalizedWs);
    }

    private static long SwapBuffers(uint parameter, long at)
    {
        bool isIncomplete = _pending != 0 && !(_primitive is TriangleStrips or QuadStrips && _stripPolygons > 0 && _pending == 2);
        if (isIncomplete)
        {
            throw new NotSupportedException("The geometry engine ran SWAP_BUFFERS in the middle of a polygon.");
        }

        (_listSwapParameters, SwapParameters) = (SwapParameters, parameter & 3);
        long vblank = Video.NextVBlankAfter(at);
        _ramCountResetAt = vblank + (RAMCountResetDelay * SystemClock.BusCycle);
        return vblank - at;
    }

    private static void CompleteSwap(long now)
    {
        if (_ramCountResetAt is not { } resetAt || resetAt > now)
        {
            return;
        }

        (RenderingVertices, RenderingPolygons, RenderingSwapParameters) = ([.. VertexRAM], [.. PolygonRAM], _listSwapParameters);
        (RenderingMarginVertices, RenderingMarginPolygons) = ([.. MarginVertices], [.. MarginPolygons]);
        VertexRAM.Clear();
        PolygonRAM.Clear();
        MarginVertices.Clear();
        MarginPolygons.Clear();
        (_isRAMCountKnown, _ramCountResetAt) = (true, null);
    }

    internal readonly record struct Vertex(int X, int Y, int Z, int W, byte Red, byte Green, byte Blue, short S, short T, bool HasTexCoord, int ScreenX = 0, int ScreenY = 0);

    internal sealed record Polygon(uint Attributes, uint TextureParameters, uint PaletteBase, int[] Vertices, bool IsFrontFacing, int[] Depths, int[] NormalizedWs);

    private readonly record struct Reach(long Numerator, long Denominator)
    {
        public static readonly Reach Screen = new(1, 1);
    }

    private readonly record struct ClipVertex(int X, int Y, int Z, int W, int Red, int Green, int Blue, short S, short T, bool HasTexCoord)
    {
        public static ClipVertex From(Vertex vertex) => new(vertex.X, vertex.Y, vertex.Z, vertex.W, Fixed(vertex.Red), Fixed(vertex.Green), Fixed(vertex.Blue), vertex.S, vertex.T, vertex.HasTexCoord);

        public bool IsOutside(int axis, int side, Reach reach)
        {
            long coordinate = Coordinate(axis) * reach.Denominator;
            long limit = W * reach.Numerator;
            return side > 0 ? coordinate > limit : coordinate < -limit;
        }

        public ClipVertex TowardsInside(ClipVertex inside, int axis, int side, Reach reach)
        {
            long numerator = (W * reach.Numerator) - (side * Coordinate(axis) * reach.Denominator);
            long denominator = numerator - ((inside.W * reach.Numerator) - (side * inside.Coordinate(axis) * reach.Denominator));
            int Along(int from, int to) => (int)(from + ((long)(to - from) * numerator / denominator));

            int w = Along(W, inside.W);
            int limit = (int)(side * w * reach.Numerator / reach.Denominator);
            return new ClipVertex(
                axis == 0 ? limit : Along(X, inside.X),
                axis == 1 ? limit : Along(Y, inside.Y),
                axis == 2 ? limit : Along(Z, inside.Z),
                w,
                Along(Red, inside.Red),
                Along(Green, inside.Green),
                Along(Blue, inside.Blue),
                (short)Along(S, inside.S),
                (short)Along(T, inside.T),
                HasTexCoord && inside.HasTexCoord);
        }

        public ClipVertex WithWholeColors() => this with { Red = Whole(Red), Green = Whole(Green), Blue = Whole(Blue) };

        public Vertex ToVertex() => new(X, Y, Z, W, Expanded(Red), Expanded(Green), Expanded(Blue), S, T, HasTexCoord);

        private static int Fixed(byte color) => ((color >> 1) << ColorFractionBits) + ColorFraction;

        private static int Whole(int color) => (color & ~ColorFraction) + ColorFraction;

        private static byte Expanded(int color) => (byte)(color >> ColorFractionBits == 0 ? 0 : ((color >> ColorFractionBits) * 2) + 1);

        private int Coordinate(int axis) => axis switch
        {
            0 => X,
            1 => Y,
            _ => Z,
        };
    }
}
