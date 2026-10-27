#include "Common.slang"

static const uint PolygonWords = 55;
static const uint VertexWords = 5;
static const uint FirstVertexWord = 5;

static const uint EDGE_COLOR = 0x00;
static const uint ALPHA_TEST_REF = 0x10;
static const uint CLEAR_COLOR = 0x20;
static const uint CLEAR_DEPTH = 0x24;
static const uint CLRIMAGE_OFFSET = 0x26;
static const uint FOG_COLOR = 0x28;
static const uint FOG_OFFSET = 0x2C;
static const uint FOG_TABLE = 0x30;
static const uint TOON_TABLE = 0x50;

static const uint TextureMapping = 1u << 0;
static const uint HighlightShading = 1u << 1;
static const uint AlphaTest = 1u << 2;
static const uint AlphaBlending = 1u << 3;
static const uint AntiAliasing = 1u << 4;
static const uint EdgeMarking = 1u << 5;
static const uint FogAlphaOnly = 1u << 6;
static const uint FogEnable = 1u << 7;
static const uint RearPlaneBitmap = 1u << 14;
static const uint WBuffering = 1u << 1;

static const uint DepthEqual = 1u << 14;
static const uint UpdateTranslucentDepth = 1u << 11;
static const uint FogFlag = 1u << 15;
static const uint TranslucentFlag = 1u << 22;
static const uint EdgeFlags = 0xF;
static const uint CoverageShift = 8;
static const uint XMajorCoverage = 1u << 31;

static const uint FacingFlag = 1u << 4;
static const uint DotFlag = 1u << 5;
static const uint ShadowMaskFlag = 1u << 6;
static const uint ShadowFlag = 1u << 7;

static const int Width3DSigned = 256;
static const uint RearColorSlot = 0x40000;
static const uint RearDepthSlot = 0x60000;

groupshared uint drawsOpaque[256];

uint ControlByte(uint offset)
{
    return (frame[ControlRegistersStart + offset / 4] >> ((offset & 3) * 8)) & 0xFF;
}

uint ControlHalf(uint offset)
{
    return (frame[ControlRegistersStart + offset / 4] >> ((offset & 2) * 8)) & 0xFFFF;
}

uint ControlWord(uint offset)
{
    return frame[ControlRegistersStart + offset / 4];
}

uint DISP3DCNT()
{
    return frame[DISP3DCNTWord] & 0xFFFF;
}

bool UsesWBuffer()
{
    return ((frame[DISP3DCNTWord] >> 16) & WBuffering) != 0;
}

uint Expand(uint color5)
{
    return color5 == 0 ? 0 : color5 * 2 + 1;
}

uint ExpandColor(uint color)
{
    return Expand(color & 0x1F) | (Expand((color >> 5) & 0x1F) << 8) | (Expand((color >> 10) & 0x1F) << 16);
}

uint RearPlaneDepth(uint depth)
{
    return depth * 0x200 + ((depth + 1) / 0x8000) * 0x1FF;
}

uint ClearPolygonID()
{
    return ControlWord(CLEAR_COLOR) & 0x3F000000;
}

uint ClearDepth()
{
    return RearPlaneDepth(ControlHalf(CLEAR_DEPTH) & 0x7FFF);
}

struct Interpolator
{
    int x0;
    int xdiff;
    int x;
    int shift;
    bool linear;
    bool wbuffer;
    bool alongY;
    int xrecipZ;
    int w0n;
    int w0d;
    int w1d;
    uint yfactor;
};

Interpolator MakeInterpolator(int x0, int x1, int w0, int w1, bool wbuffer, bool alongY)
{
    Interpolator i;
    i.x0 = x0;
    i.xdiff = x1 - x0;
    i.x = 0;
    i.wbuffer = wbuffer;
    i.alongY = alongY;
    i.xrecipZ = i.xdiff != 0 ? (1 << 22) / i.xdiff : 0;
    int mask = alongY ? 0x7E : 0x7F;
    i.linear = w0 == w1 && (w0 & mask) == 0 && (w1 & mask) == 0;
    if (alongY)
    {
        i.w0n = w0 >> 1;
        i.w0d = (w0 + ((w0 & ~w1) & 1)) >> 1;
        i.w1d = w1 >> 1;
        i.shift = 9;
    }
    else
    {
        i.w0n = w0;
        i.w0d = w0;
        i.w1d = w1;
        i.shift = 8;
    }

    i.yfactor = 0;
    return i;
}

void SetX(inout Interpolator i, int x)
{
    x -= i.x0;
    i.x = x;
    if (i.xdiff != 0 && (!i.linear || i.wbuffer))
    {
        uint numerator = uint(x * i.w0n) << i.shift;
        uint denominator = uint(x * i.w0d) + uint((i.xdiff - x) * i.w1d);
        i.yfactor = denominator == 0 ? 0 : numerator / denominator;
    }
}

int Interpolate(Interpolator i, int y0, int y1)
{
    if (i.xdiff == 0 || y0 == y1)
    {
        return y0;
    }

    if (!i.linear)
    {
        return y0 < y1
            ? y0 + int((uint(y1 - y0) * i.yfactor) >> i.shift)
            : y1 + int((uint(y0 - y1) * ((1u << i.shift) - i.yfactor)) >> i.shift);
    }

    return y0 < y1
        ? y0 + int(int64_t(y1 - y0) * i.x / i.xdiff)
        : y1 + int(int64_t(y0 - y1) * (i.xdiff - i.x) / i.xdiff);
}

int InterpolateZ(Interpolator i, int z0, int z1)
{
    if (i.xdiff == 0 || z0 == z1)
    {
        return z0;
    }

    if (i.wbuffer)
    {
        return z0 < z1
            ? z0 + int((int64_t(z1 - z0) * int64_t(i.yfactor)) >> i.shift)
            : z1 + int((int64_t(z0 - z1) * int64_t((1u << i.shift) - i.yfactor)) >> i.shift);
    }

    int base = z0 < z1 ? z0 : z1;
    int displacement = z0 < z1 ? z1 - z0 : z0 - z1;
    int factor = z0 < z1 ? i.x : i.xdiff - i.x;
    if (i.alongY)
    {
        int shift = 0;
        while (displacement > 0x3FF)
        {
            displacement >>= 1;
            shift++;
        }

        return base + int(((int64_t(displacement) * factor * i.xrecipZ) >> 22) << shift);
    }

    displacement >>= 9;
    return base + int((int64_t(displacement) * factor * i.xrecipZ) >> 13);
}

struct Slope
{
    int increment;
    bool negative;
    bool xMajor;
    Interpolator interp;
    int x0;
    int xmin;
    int xmax;
    int xlen;
    int ylen;
    int dx;
    int xcovIncrement;
};

Slope DummySlope(int x0, bool wbuffer)
{
    Slope s;
    s.increment = 0;
    s.negative = false;
    s.xMajor = false;
    s.interp = MakeInterpolator(0, 0, 0, 0, wbuffer, true);
    SetX(s.interp, 0);
    s.x0 = x0;
    s.xmin = x0;
    s.xmax = x0;
    s.xlen = 0;
    s.ylen = 0;
    s.dx = 0;
    s.xcovIncrement = 0;
    return s;
}

Slope MakeSlope(int x0, int x1, int y0, int y1, int w0, int w1, int y, bool wbuffer, bool isRight)
{
    Slope s;
    s.x0 = x0;
    if (x1 > x0)
    {
        s.xmin = x0;
        s.xmax = x1 - 1;
        s.negative = false;
    }
    else if (x1 < x0)
    {
        s.xmin = x1;
        s.xmax = x0 - 1;
        s.negative = true;
    }
    else
    {
        s.xmin = x0;
        s.xmax = x0;
        s.negative = false;
    }

    s.xlen = s.xmax + 1 - s.xmin;
    s.ylen = y1 - y0;
    if (s.ylen == 0)
    {
        s.increment = 0;
    }
    else if (s.ylen == s.xlen && s.xlen != 1)
    {
        s.increment = 0x40000;
    }
    else
    {
        int yrecip = (1 << 18) / s.ylen;
        s.increment = abs((x1 - x0) * yrecip);
    }

    s.xMajor = s.increment > 0x40000;
    if (isRight)
    {
        s.dx = s.xMajor ? (s.negative ? 0x20000 + 0x40000 : s.increment - 0x20000) : (s.increment != 0 && s.negative ? 0x40000 : 0);
    }
    else
    {
        s.dx = s.xMajor ? (s.negative ? (s.increment - 0x20000) + 0x40000 : 0x20000) : (s.increment != 0 && s.negative ? 0x40000 : 0);
    }

    s.dx += (y - y0) * s.increment;

    int offset = s.increment >= 0x40000 && (isRight != s.negative) ? 1 : 0;
    s.interp = MakeInterpolator(y0 - offset, y1 - offset, w0, w1, wbuffer, true);
    SetX(s.interp, y);
    s.xcovIncrement = s.xMajor ? (s.ylen << 10) / s.xlen : 0;
    return s;
}

int XValue(Slope s)
{
    int x = s.negative ? s.x0 - (s.dx >> 18) : s.x0 + (s.dx >> 18);
    return clamp(x, s.xmin, s.xmax);
}

void EdgeParameters(Slope s, bool isRight, bool swapped, out int length, out uint coverage)
{
    length = 1;
    if (s.xMajor)
    {
        if (!swapped || isRight)
        {
            length = isRight != s.negative ? (s.dx >> 18) - ((s.dx - s.increment) >> 18) : ((s.dx + s.increment) >> 18) - (s.dx >> 18);
        }

        int startX = s.dx >> 18;
        if (s.negative) startX = s.xlen - startX;
        if (isRight) startX = startX - length + 1;

        int startCoverage = (((startX << 10) + 0x1FF) * s.ylen) / s.xlen;
        coverage = XMajorCoverage | (uint(startCoverage & 0x3FF) << 12) | uint(s.xcovIncrement & 0x3FF);
        if (swapped) length = 1;
        return;
    }

    if (s.increment == 0)
    {
        coverage = swapped ? 0 : 31;
        return;
    }

    int value = ((s.dx >> 9) + (s.increment >> 10)) >> 4;
    if ((value >> 5) != (s.dx >> 18)) value = 31;
    value &= 0x1F;
    bool inverted = swapped ? isRight != s.negative : isRight == s.negative;
    coverage = uint(inverted ? 0x1F - value : value);
}

uint PolygonWord(uint polygon, uint word)
{
    uint start = frame[parameters.window == ScreenWindow ? PolygonsStartWord : MarginPolygonsStartWord];
    return frame[start + polygon * PolygonWords + word];
}

int VertexX(uint polygon, uint vertex)
{
    return SignExtend(PolygonWord(polygon, FirstVertexWord + vertex * VertexWords) & 0xFFFF, 16);
}

int VertexY(uint polygon, uint vertex)
{
    return int(PolygonWord(polygon, FirstVertexWord + vertex * VertexWords) >> 16);
}

int VertexDepth(uint polygon, uint vertex)
{
    return int(PolygonWord(polygon, FirstVertexWord + vertex * VertexWords + 1));
}

int VertexW(uint polygon, uint vertex)
{
    return int(PolygonWord(polygon, FirstVertexWord + vertex * VertexWords + 2));
}

int VertexColor(uint polygon, uint vertex, uint channel)
{
    return int((PolygonWord(polygon, FirstVertexWord + vertex * VertexWords + 3) >> (channel * 10)) & 0x3FF);
}

int VertexTexCoord(uint polygon, uint vertex, uint axis)
{
    return SignExtend(PolygonWord(polygon, FirstVertexWord + vertex * VertexWords + 4) >> (axis * 16), 16);
}

struct Span
{
    int xstart;
    int xend;
    int wl;
    int wr;
    int zl;
    int zr;
    int colorL[3];
    int colorR[3];
    int texCoordL[2];
    int texCoordR[2];
    bool fillLeft;
    bool fillRight;
    int leftLength;
    int rightLength;
    uint leftCoverage;
    uint rightCoverage;
    uint yEdge;
};

uint NextVertex(uint vertex, uint count, bool forward)
{
    return forward ? (vertex + 1 == count ? 0 : vertex + 1) : (vertex == 0 ? count - 1 : vertex - 1);
}

Span SetupSpan(uint polygon, int y, uint polygonAlpha)
{
    uint flags = PolygonWord(polygon, 3);
    uint count = flags & 0xF;
    bool facing = (flags & FacingFlag) != 0;
    uint vtop = (flags >> 8) & 0xF;
    uint vbottom = (flags >> 12) & 0xF;
    int ytop = int(PolygonWord(polygon, 4) & 0xFFFF);
    int ybottom = int(PolygonWord(polygon, 4) >> 16);
    bool wbuffer = UsesWBuffer();

    uint curL = vtop, curR = vtop;
    uint nextL = NextVertex(vtop, count, facing);
    uint nextR = NextVertex(vtop, count, !facing);
    Slope left;
    Slope right;
    if (ybottom == ytop)
    {
        uint leftmost = 0, rightmost = 0;
        uint candidates[2] = { 1, count - 1 };
        for (uint c = 0; c < 2; c++)
        {
            uint i = candidates[c];
            if (VertexX(polygon, i) < VertexX(polygon, leftmost)) leftmost = i;
            if (VertexX(polygon, i) > VertexX(polygon, rightmost)) rightmost = i;
        }

        curL = leftmost;
        nextL = leftmost;
        curR = rightmost;
        nextR = rightmost;
        left = DummySlope(VertexX(polygon, curL), wbuffer);
        right = DummySlope(VertexX(polygon, curR), wbuffer);
    }
    else
    {
        while (y >= VertexY(polygon, nextL) && curL != vbottom)
        {
            curL = nextL;
            nextL = NextVertex(curL, count, facing);
        }

        while (y >= VertexY(polygon, nextR) && curR != vbottom)
        {
            curR = nextR;
            nextR = NextVertex(curR, count, !facing);
        }

        left = MakeSlope(VertexX(polygon, curL), VertexX(polygon, nextL), VertexY(polygon, curL), VertexY(polygon, nextL), VertexW(polygon, curL), VertexW(polygon, nextL), y, wbuffer, false);
        right = MakeSlope(VertexX(polygon, curR), VertexX(polygon, nextR), VertexY(polygon, curR), VertexY(polygon, nextR), VertexW(polygon, curR), VertexW(polygon, nextR), y, wbuffer, true);
    }

    Span span;
    span.xstart = XValue(left);
    span.xend = XValue(right);
    span.wl = Interpolate(left.interp, VertexW(polygon, curL), VertexW(polygon, nextL));
    span.wr = Interpolate(right.interp, VertexW(polygon, curR), VertexW(polygon, nextR));
    span.zl = InterpolateZ(left.interp, VertexDepth(polygon, curL), VertexDepth(polygon, nextL));
    span.zr = InterpolateZ(right.interp, VertexDepth(polygon, curR), VertexDepth(polygon, nextR));

    if (right.increment == 0 && (left.increment != 0 || span.xstart != span.xend) && span.xend != 0)
    {
        span.xend--;
    }

    uint dispcnt = DISP3DCNT();
    bool wireframe = polygonAlpha == 0;
    bool fillsAllEdges = (dispcnt & (AntiAliasing | EdgeMarking)) != 0 || (polygonAlpha < 31 && (dispcnt & AlphaBlending) != 0) || wireframe;
    bool isBottomLine = y == ybottom - 1;

    uint startCur, startNext, endCur, endNext;
    Interpolator startInterp;
    Interpolator endInterp;
    if (span.xstart > span.xend)
    {
        startCur = curR; startNext = nextR; endCur = curL; endNext = nextL;
        startInterp = right.interp;
        endInterp = left.interp;
        EdgeParameters(right, true, true, span.leftLength, span.leftCoverage);
        EdgeParameters(left, false, true, span.rightLength, span.rightCoverage);

        int temporary = span.xstart; span.xstart = span.xend; span.xend = temporary;
        temporary = span.wl; span.wl = span.wr; span.wr = temporary;
        temporary = span.zl; span.zl = span.zr; span.zr = temporary;

        bool nextXsDiffer = VertexX(polygon, startNext) != VertexX(polygon, endNext);
        span.fillLeft = fillsAllEdges || (right.negative || !right.xMajor) || (isBottomLine && right.xMajor && nextXsDiffer);
        span.fillRight = fillsAllEdges || (!left.negative && left.xMajor) || (!(left.negative && left.xMajor) && right.increment == 0)
            || (isBottomLine && left.xMajor && nextXsDiffer);
    }
    else
    {
        startCur = curL; startNext = nextL; endCur = curR; endNext = nextR;
        startInterp = left.interp;
        endInterp = right.interp;
        EdgeParameters(left, false, false, span.leftLength, span.leftCoverage);
        EdgeParameters(right, true, false, span.rightLength, span.rightCoverage);

        bool nextXsDiffer = VertexX(polygon, startNext) != VertexX(polygon, endNext);
        span.fillLeft = fillsAllEdges || (left.negative || !left.xMajor) || (isBottomLine && left.xMajor && nextXsDiffer)
            || (left.increment == right.increment && span.xstart + span.leftLength == span.xend + 1);
        span.fillRight = fillsAllEdges || (!right.negative && right.xMajor) || right.increment == 0 || (isBottomLine && right.xMajor && nextXsDiffer);
    }

    for (uint channel = 0; channel < 3; channel++)
    {
        span.colorL[channel] = Interpolate(startInterp, VertexColor(polygon, startCur, channel), VertexColor(polygon, startNext, channel));
        span.colorR[channel] = Interpolate(endInterp, VertexColor(polygon, endCur, channel), VertexColor(polygon, endNext, channel));
    }

    for (uint axis = 0; axis < 2; axis++)
    {
        span.texCoordL[axis] = Interpolate(startInterp, VertexTexCoord(polygon, startCur, axis), VertexTexCoord(polygon, startNext, axis));
        span.texCoordR[axis] = Interpolate(endInterp, VertexTexCoord(polygon, endCur, axis), VertexTexCoord(polygon, endNext, axis));
    }

    span.yEdge = y == ytop ? 0x4u : isBottomLine ? 0x8u : 0u;
    return span;
}

uint TextureByte(uint address)
{
    return VRAM8(0, Texture, address);
}

uint TextureHalf(uint address)
{
    return VRAM16(0, Texture, address);
}

uint TexturePaletteColor(uint address)
{
    return VRAM16(0, TexturePalette, address);
}

uint Mix(uint color0, uint color1, uint weight0, uint weight1, uint shift)
{
    uint red = ((color0 & 0x001F) * weight0 + (color1 & 0x001F) * weight1) >> shift;
    uint green = (((color0 & 0x03E0) * weight0 + (color1 & 0x03E0) * weight1) >> shift) & 0x03E0;
    uint blue = (((color0 & 0x7C00) * weight0 + (color1 & 0x7C00) * weight1) >> shift) & 0x7C00;
    return red | green | blue;
}

void TextureLookup(uint parameters, uint paletteBase, int s, int t, out uint color, out uint alpha)
{
    uint address = (parameters & 0xFFFF) << 3;
    int width = 8 << ((parameters >> 20) & 7);
    int height = 8 << ((parameters >> 23) & 7);
    s >>= 4;
    t >>= 4;

    if ((parameters & (1u << 16)) != 0)
    {
        s = (parameters & (1u << 18)) != 0 && (s & width) != 0 ? (width - 1) - (s & (width - 1)) : s & (width - 1);
    }
    else
    {
        s = clamp(s, 0, width - 1);
    }

    if ((parameters & (1u << 17)) != 0)
    {
        t = (parameters & (1u << 19)) != 0 && (t & height) != 0 ? (height - 1) - (t & (height - 1)) : t & (height - 1);
    }
    else
    {
        t = clamp(t, 0, height - 1);
    }

    uint alpha0 = (parameters & (1u << 29)) != 0 ? 0 : 31;
    uint texel = uint(t * width + s);
    switch ((parameters >> 26) & 7)
    {
        case 1:
        {
            uint value = TextureByte(address + texel);
            color = TexturePaletteColor((paletteBase << 4) + ((value & 0x1F) << 1));
            alpha = ((value >> 3) & 0x1C) + (value >> 6);
            return;
        }
        case 2:
        {
            uint value = (TextureByte(address + (texel >> 2)) >> ((uint(s) & 3) << 1)) & 3;
            color = TexturePaletteColor((paletteBase << 3) + (value << 1));
            alpha = value == 0 ? alpha0 : 31;
            return;
        }
        case 3:
        {
            uint value = (TextureByte(address + (texel >> 1)) >> ((uint(s) & 1) * 4)) & 0xF;
            color = TexturePaletteColor((paletteBase << 4) + (value << 1));
            alpha = value == 0 ? alpha0 : 31;
            return;
        }
        case 4:
        {
            uint value = TextureByte(address + texel);
            color = TexturePaletteColor((paletteBase << 4) + (value << 1));
            alpha = value == 0 ? alpha0 : 31;
            return;
        }
        case 5:
        {
            address += (uint(t) & 0x3FC) * uint(width >> 2) + (uint(s) & 0x3FC) + (uint(t) & 3);
            address &= 0x7FFFF;
            uint slot1 = 0x20000 + ((address & 0x1FFFC) >> 1) + (address >= 0x40000 ? 0x10000 : 0);
            uint value = address >= 0x20000 && address < 0x40000 ? 0 : TextureByte(address) >> (2 * (uint(s) & 3));
            uint paletteInfo = TextureHalf(slot1);
            uint palette = (paletteBase << 4) + ((paletteInfo & 0x3FFF) << 2);
            uint mode = paletteInfo >> 14;
            alpha = 31;
            switch (value & 3)
            {
                case 0:
                    color = TexturePaletteColor(palette);
                    return;
                case 1:
                    color = TexturePaletteColor(palette + 2);
                    return;
                case 2:
                    color = mode == 1 ? Mix(TexturePaletteColor(palette), TexturePaletteColor(palette + 2), 1, 1, 1)
                        : mode == 3 ? Mix(TexturePaletteColor(palette), TexturePaletteColor(palette + 2), 5, 3, 3)
                        : TexturePaletteColor(palette + 4);
                    return;
                default:
                    if (mode == 2)
                    {
                        color = TexturePaletteColor(palette + 6);
                    }
                    else if (mode == 3)
                    {
                        color = Mix(TexturePaletteColor(palette), TexturePaletteColor(palette + 2), 3, 5, 3);
                    }
                    else
                    {
                        color = 0;
                        alpha = 0;
                    }

                    return;
            }
        }
        case 6:
        {
            uint value = TextureByte(address + texel);
            color = TexturePaletteColor((paletteBase << 4) + ((value & 7) << 1));
            alpha = value >> 3;
            return;
        }
        default:
        {
            color = TextureHalf(address + (texel << 1));
            alpha = (color & 0x8000) != 0 ? 31 : 0;
            return;
        }
    }
}

uint ToonColor(uint index)
{
    return ExpandColor(ControlHalf(TOON_TABLE + index * 2));
}

uint3 Unpack(uint color)
{
    return uint3(color & 0x3F, (color >> 8) & 0x3F, (color >> 16) & 0x3F);
}

uint Pack(uint3 color, uint alpha)
{
    return color.x | (color.y << 8) | (color.z << 16) | (alpha << 24);
}

uint PolygonPixel(uint polygon, uint3 vertexColor, int s, int t)
{
    uint attributes = PolygonWord(polygon, 0);
    uint parameters = PolygonWord(polygon, 1);
    uint dispcnt = DISP3DCNT();
    uint mode = (attributes >> 4) & 3;
    uint polygonAlpha = (attributes >> 16) & 0x1F;
    if (polygonAlpha == 0)
    {
        polygonAlpha = 31;
    }

    bool isShaded = mode == 2;
    bool isHighlight = isShaded && (dispcnt & HighlightShading) != 0;
    uint3 shade = Unpack(ToonColor(vertexColor.x >> 1));
    if (isShaded && !isHighlight)
    {
        vertexColor = shade;
    }

    if ((dispcnt & TextureMapping) == 0 || ((parameters >> 26) & 7) == 0)
    {
        if (isHighlight)
        {
            return Pack(min(uint3(vertexColor.x, vertexColor.x, vertexColor.x) + shade, uint3(63, 63, 63)), polygonAlpha);
        }

        return Pack(vertexColor, polygonAlpha);
    }

    uint textureColor;
    uint textureAlpha;
    TextureLookup(parameters, PolygonWord(polygon, 2), s, t, textureColor, textureAlpha);
    uint3 texture = Unpack(ExpandColor(textureColor));
    uint textureAlpha6 = Expand(textureAlpha);
    uint modulatedAlpha = (((textureAlpha6 + 1) * (Expand(polygonAlpha) + 1) - 1) / 64) >> 1;

    if (isHighlight)
    {
        return Pack(min((((texture + 1) * (shade + 1) - 1) >> 6) + shade, uint3(63, 63, 63)), modulatedAlpha);
    }

    if ((mode & 1) != 0)
    {
        uint3 color = textureAlpha == 0 ? vertexColor
            : textureAlpha == 31 ? texture
            : ((texture * textureAlpha6) + (vertexColor * (63 - textureAlpha6))) / 64;
        return Pack(color, polygonAlpha);
    }

    return Pack(((texture + 1) * (vertexColor + 1) - 1) >> 6, modulatedAlpha);
}

bool DepthTest(uint attributes, uint destinationDepth, int z)
{
    int destination = int(destinationDepth);
    return (attributes & DepthEqual) != 0 ? uint(destination - z + 0x200) <= 0x400 : z < destination;
}

struct Layers
{
    uint color[2];
    uint depth[2];
    uint attributes[2];
};

uint AlphaBlend(uint source, uint destination)
{
    uint sourceAlpha = source >> 24;
    uint destinationAlpha = destination >> 24;
    if ((DISP3DCNT() & AlphaBlending) == 0 || destinationAlpha == 0)
    {
        return source;
    }

    uint3 color = (Unpack(source) * (sourceAlpha + 1) + Unpack(destination) * (31 - sourceAlpha)) >> 5;
    return Pack(color, max(sourceAlpha, destinationAlpha));
}

void PlotTranslucent(inout Layers layers, uint layer, uint color, int z, bool updatesDepth, uint polygonAttributes, bool isShadow)
{
    uint destination = layers.attributes[layer];
    uint attributes = (polygonAttributes & 0xE0F0) | ((polygonAttributes >> 8) & 0xFF0000) | TranslucentFlag | (destination & 0xFF001F0F);
    if (isShadow && (destination & TranslucentFlag) == 0)
    {
        if ((destination & 0x3F000000) == (polygonAttributes & 0x3F000000))
        {
            return;
        }
    }
    else if ((destination & 0x007F0000) == (attributes & 0x007F0000))
    {
        return;
    }

    if ((destination & FogFlag) == 0)
    {
        attributes &= ~FogFlag;
    }

    layers.color[layer] = AlphaBlend(color, layers.color[layer]);
    if (updatesDepth)
    {
        layers.depth[layer] = uint(z);
    }

    layers.attributes[layer] = attributes;
}

void ClearLayers(inout Layers layers, uint x, uint y)
{
    uint dispcnt = DISP3DCNT();
    uint polygonID = ClearPolygonID();
    if ((dispcnt & RearPlaneBitmap) != 0)
    {
        uint offset = ControlHalf(CLRIMAGE_OFFSET);
        uint column = ((offset & 0xFF) + x) & 0xFF;
        uint row = (((offset >> 8) & 0xFF) + y) & 0xFF;
        uint colorValue = TextureHalf(RearColorSlot + (row << 9) + (column << 1));
        uint depthValue = TextureHalf(RearDepthSlot + (row << 9) + (column << 1));
        layers.color[0] = ExpandColor(colorValue) | ((colorValue & 0x8000) != 0 ? 31u << 24 : 0);
        layers.depth[0] = RearPlaneDepth(depthValue & 0x7FFF);
        layers.attributes[0] = polygonID | (depthValue & FogFlag);
    }
    else
    {
        uint clearColor = ControlWord(CLEAR_COLOR);
        layers.color[0] = ExpandColor(clearColor) | (((clearColor >> 16) & 0x1F) << 24);
        layers.depth[0] = ClearDepth();
        layers.attributes[0] = polygonID | (clearColor & FogFlag);
    }

    layers.color[1] = layers.color[0];
    layers.depth[1] = layers.depth[0];
    layers.attributes[1] = layers.attributes[0];
}

uint AlphaReference()
{
    return (DISP3DCNT() & AlphaTest) != 0 ? ControlByte(ALPHA_TEST_REF) & 0x1F : 0;
}

bool IsCovered(uint polygon, int y)
{
    int ytop = int(PolygonWord(polygon, 4) & 0xFFFF);
    int ybottom = int(PolygonWord(polygon, 4) >> 16);
    return y >= ytop && (y < ybottom || (y == ytop && ybottom == ytop));
}

[shader("compute")]
[numthreads(256, 1, 1)]
void Rasterize(uint3 id : SV_DispatchThreadID)
{
    uint x = id.x;
    int y = int(id.y);
    int origin = WindowOrigins[parameters.window];
    int windowEnd = origin + Width3DSigned;
    int px = origin + int(x);
    Layers layers;
    ClearLayers(layers, uint(px), uint(y));
    bool isStenciled = false;
    uint dispcnt = DISP3DCNT();
    bool isAntiAliased = (dispcnt & AntiAliasing) != 0;
    uint alphaReference = AlphaReference();
    uint polygonCount = frame[parameters.window == ScreenWindow ? PolygonCountWord : MarginPolygonCountWord];

    for (uint polygon = 0; polygon < polygonCount; polygon++)
    {
        if (!IsCovered(polygon, y))
        {
            continue;
        }

        uint attributes = PolygonWord(polygon, 0);
        uint flags = PolygonWord(polygon, 3);
        bool isDot = (flags & DotFlag) != 0;
        bool isShadowMask = (flags & ShadowMaskFlag) != 0;
        bool isShadow = (flags & ShadowFlag) != 0;
        uint polygonAlpha = (attributes >> 16) & 0x1F;
        bool wireframe = polygonAlpha == 0;
        uint polygonAttributes = attributes & 0x3F008000;
        Span span = SetupSpan(polygon, y, polygonAlpha);

        int start = span.xstart;
        int leftEnd = min(min(span.xstart + span.leftLength, span.xend + 1), windowEnd);
        int afterLeft = max(start, leftEnd);
        int innerEnd = min(min(span.xend - span.rightLength + 1, span.xend + 1), windowEnd);
        int afterInner = max(afterLeft, innerEnd);
        int rightEnd = min(span.xend + 1, windowEnd);

        uint edge = 0;
        bool isInSpan = false;
        if (px >= start && px < leftEnd)
        {
            isInSpan = span.fillLeft;
            edge = span.yEdge | 1;
        }
        else if (px >= afterLeft && px < innerEnd)
        {
            isInSpan = !(wireframe && span.yEdge == 0);
            edge = span.yEdge;
        }
        else if (px >= afterInner && px < rightEnd)
        {
            isInSpan = span.fillRight;
            edge = span.yEdge | 2;
        }

        Interpolator across = MakeInterpolator(span.xstart, span.xend + 1, span.wl, span.wr, UsesWBuffer(), false);
        SetX(across, px);
        int z = isDot ? VertexDepth(polygon, 0) : InterpolateZ(across, span.zl, span.zr);

        if (isShadowMask)
        {
            uint maskAlpha = wireframe ? 31 : polygonAlpha;
            if (isInSpan && maskAlpha > alphaReference)
            {
                bool passes = DepthTest(attributes, layers.depth[0], z)
                    || ((layers.attributes[0] & EdgeFlags) != 0 && DepthTest(attributes, layers.depth[1], z));
                if (passes)
                {
                    isStenciled = true;
                }
            }

            if (isAntiAliased)
            {
                GroupMemoryBarrierWithGroupSync();
                GroupMemoryBarrierWithGroupSync();
            }

            continue;
        }

        uint layer = 0;
        bool draws = isInSpan;
        if (draws && isShadow && isStenciled)
        {
            isStenciled = false;
            draws = false;
        }

        if (draws && !DepthTest(attributes, layers.depth[0], z))
        {
            draws = (layers.attributes[0] & EdgeFlags) != 0 && DepthTest(attributes, layers.depth[1], z);
            layer = 1;
        }

        uint color = 0;
        uint alpha = 0;
        if (draws)
        {
            uint3 vertexColor;
            int s;
            int t;
            if (isDot)
            {
                vertexColor = uint3(VertexColor(polygon, 0, 0), VertexColor(polygon, 0, 1), VertexColor(polygon, 0, 2)) >> 3;
                s = VertexTexCoord(polygon, 0, 0);
                t = VertexTexCoord(polygon, 0, 1);
            }
            else
            {
                vertexColor = uint3(
                    uint(Interpolate(across, span.colorL[0], span.colorR[0])) >> 3,
                    uint(Interpolate(across, span.colorL[1], span.colorR[1])) >> 3,
                    uint(Interpolate(across, span.colorL[2], span.colorR[2])) >> 3);
                s = SignExtend(uint(Interpolate(across, span.texCoordL[0], span.texCoordR[0])), 16);
                t = SignExtend(uint(Interpolate(across, span.texCoordL[1], span.texCoordR[1])), 16);
            }

            color = PolygonPixel(polygon, vertexColor, s, t);
            alpha = color >> 24;
            draws = alpha > alphaReference;
        }

        bool drawsOpaqueEdge = draws && alpha == 31 && (edge & 3) != 0;
        uint coverage = 0;
        if (isAntiAliased)
        {
            drawsOpaque[x] = drawsOpaqueEdge ? 1u : 0u;
            GroupMemoryBarrierWithGroupSync();
            uint edgeCoverage = (edge & 1) != 0 ? span.leftCoverage : span.rightCoverage;
            if (drawsOpaqueEdge && (edgeCoverage & XMajorCoverage) != 0)
            {
                uint first = uint(((edge & 1) != 0 ? max(start, origin) : afterInner) - origin);
                uint drawnBefore = 0;
                for (uint before = first; before < x; before++)
                {
                    drawnBefore += drawsOpaque[before];
                }

                uint startCoverage = (edgeCoverage >> 12) & 0x3FF;
                uint xcov = (startCoverage == 0x3FF ? 0 : startCoverage) + drawnBefore * (edgeCoverage & 0x3FF);
                coverage = (edge & 1) != 0 ? min(xcov >> 5, 31u) : uint(max(0x1F - int(xcov >> 5), 0));
            }
            else if (drawsOpaqueEdge)
            {
                coverage = edgeCoverage;
            }

            GroupMemoryBarrierWithGroupSync();
        }

        if (!draws)
        {
            continue;
        }

        if (alpha == 31)
        {
            uint written = polygonAttributes | edge;
            if (isAntiAliased && (edge & 3) != 0)
            {
                written |= coverage << CoverageShift;
            }
            else if (isAntiAliased && edge != 0)
            {
                written |= 0x1Fu << CoverageShift;
            }

            if (isAntiAliased && edge != 0 && layer == 0)
            {
                layers.color[1] = layers.color[0];
                layers.depth[1] = layers.depth[0];
                layers.attributes[1] = layers.attributes[0];
            }

            layers.depth[layer] = uint(z);
            layers.color[layer] = color;
            layers.attributes[layer] = written;
        }
        else
        {
            bool updatesDepth = (attributes & UpdateTranslucentDepth) != 0;
            uint destination = layers.attributes[layer];
            PlotTranslucent(layers, layer, color, z, updatesDepth, polygonAttributes, isShadow);
            if ((destination & EdgeFlags) != 0 && layer == 0)
            {
                PlotTranslucent(layers, 1, color, z, updatesDepth, polygonAttributes, isShadow);
            }
        }
    }

    uint pixel = parameters.window * WindowSize + uint(y) * Width3D + x;
    for (uint layer = 0; layer < 2; layer++)
    {
        rendering[ColorLayers + layer * BufferSize + pixel] = layers.color[layer];
        rendering[DepthLayers + layer * BufferSize + pixel] = layers.depth[layer];
        rendering[AttributeLayers + layer * BufferSize + pixel] = layers.attributes[layer];
    }
}

uint TopAttributes(int x, int y)
{
    if (x < 0 || x >= Width3DSigned || y < 0 || y >= int(Lines))
    {
        return ClearPolygonID();
    }

    return rendering[parameters.window * WindowSize + AttributeLayers + uint(y) * Width3D + uint(x)];
}

uint TopDepth(int x, int y)
{
    if (x < 0 || x >= Width3DSigned || y < 0 || y >= int(Lines))
    {
        return ClearDepth();
    }

    return rendering[parameters.window * WindowSize + DepthLayers + uint(y) * Width3D + uint(x)];
}

uint FogDensity(uint depth)
{
    uint offset = (ControlHalf(FOG_OFFSET) & 0x7FFF) * 0x200;
    uint shift = (DISP3DCNT() >> 8) & 0xF;
    uint index = 0;
    uint fraction = 0;
    if (depth >= offset)
    {
        uint scaled = ((depth - offset) >> 2) << shift;
        index = scaled >> 17;
        fraction = index >= 32 ? 0 : scaled & 0x1FFFF;
        index = min(index, 32u);
    }

    uint lower = ControlByte(FOG_TABLE + (index == 0 ? 0 : index - 1)) & 0x7F;
    uint upper = ControlByte(FOG_TABLE + min(index, 31u)) & 0x7F;
    uint density = (lower * (0x20000 - fraction) + upper * fraction) >> 17;
    return density >= 127 ? 128 : density;
}

uint Fog(uint color, uint depth)
{
    uint fogColor = ControlWord(FOG_COLOR);
    uint shift = (DISP3DCNT() >> 8) & 0xF;
    uint firstBoundary = (ControlHalf(FOG_OFFSET) & 0x7FFF) * 0x200 + (0x80000u >> shift);
    uint fogAlpha = depth < firstBoundary ? 31 : (fogColor >> 16) & 0x1F;
    uint density = FogDensity(depth);
    uint3 rgb = Unpack(color);
    if ((DISP3DCNT() & FogAlphaOnly) == 0)
    {
        rgb = (Unpack(ExpandColor(fogColor)) * density + rgb * (128 - density)) >> 7;
    }

    uint alpha = (fogAlpha * density + ((color >> 24) & 0x1F) * (128 - density)) >> 7;
    return Pack(rgb, alpha);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void FinishRendering(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width3D || id.y >= Lines)
    {
        return;
    }

    int x = int(id.x);
    int y = int(id.y);
    uint pixel = parameters.window * WindowSize + id.y * Width3D + id.x;
    uint dispcnt = DISP3DCNT();
    uint top = rendering[ColorLayers + pixel];
    uint bottom = rendering[ColorLayers + BufferSize + pixel];
    uint attributes = rendering[AttributeLayers + pixel];

    if ((dispcnt & EdgeMarking) != 0 && (attributes & EdgeFlags) != 0)
    {
        uint polygonID = attributes >> 24;
        uint depth = rendering[DepthLayers + pixel];
        int2 neighbors[4] = { int2(x - 1, y), int2(x + 1, y), int2(x, y - 1), int2(x, y + 1) };
        bool isEdge = false;
        for (uint n = 0; n < 4; n++)
        {
            isEdge = isEdge || (polygonID != (TopAttributes(neighbors[n].x, neighbors[n].y) >> 24) && depth < TopDepth(neighbors[n].x, neighbors[n].y));
        }

        if (isEdge)
        {
            top = (ExpandColor(ControlHalf(EDGE_COLOR + (polygonID >> 3) * 2)) & 0x3F3F3F) | (top & 0xFF000000);
            attributes = (attributes & 0xFFFFE0FF) | 0x00001000;
        }
    }

    if ((dispcnt & FogEnable) != 0)
    {
        if ((attributes & FogFlag) != 0)
        {
            top = Fog(top, rendering[DepthLayers + pixel]);
        }

        if ((attributes & EdgeFlags) != 0 && (rendering[AttributeLayers + BufferSize + pixel] & FogFlag) != 0)
        {
            bottom = Fog(bottom, rendering[DepthLayers + BufferSize + pixel]);
        }
    }

    if ((dispcnt & AntiAliasing) != 0 && (attributes & EdgeFlags) != 0)
    {
        uint coverage = (attributes >> CoverageShift) & 0x1F;
        if (coverage == 0)
        {
            top = bottom;
        }
        else if (coverage != 0x1F)
        {
            coverage++;
            uint bottomAlpha = (bottom >> 24) & 0x1F;
            uint3 rgb = Unpack(top);
            if (bottomAlpha > 0)
            {
                rgb = (rgb * coverage + Unpack(bottom) * (32 - coverage)) >> 5;
            }

            uint alpha = (((top >> 24) & 0x1F) * coverage + bottomAlpha * (32 - coverage)) >> 5;
            top = Pack(rgb, alpha);
        }
    }

    rendering[Output3D + pixel] = top;
}
