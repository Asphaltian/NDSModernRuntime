#include "Common.slang"

static const uint DISPCNT = 0x00;
static const uint BG0CNT = 0x08;
static const uint BG0HOFS = 0x10;
static const uint BG0VOFS = 0x12;
static const uint BG2PA = 0x20;
static const uint WIN0H = 0x40;
static const uint WININ = 0x48;
static const uint WINOUT = 0x4A;
static const uint MOSAIC = 0x4C;
static const uint BLDCNT = 0x50;
static const uint BLDALPHA = 0x52;
static const uint BLDY = 0x54;
static const uint MASTER_BRIGHT = 0x6C;

static const uint BothLCDsEnable = 1u << 0;
static const uint DisplaySwap = 1u << 15;
static const uint EnginePower[2] = { 1u << 1, 1u << 9 };

static const uint EngineA = 0;
static const uint EngineB = 1;

static const uint SizingEven = 0;
static const uint SizingTopOnly = 1;
static const uint SizingScrolled = 4;
static const uint SizingCropped = 5;

static const uint LayerOBJ = 4;
static const uint LayerBackdrop = 5;
static const uint WindowEffects = 1u << 5;

static const uint Transparent = 0xFFFFFFFF;

uint PaletteColor(uint line, uint engine, uint index)
{
    return (VideoMemoryWord(line, PalettesKilobyte + engine, index >> 1) >> ((index & 1) * 16)) & 0x7FFF;
}

uint ExtendedPaletteColor(uint line, uint area, uint index)
{
    return VRAM16(line, area, index * 2) & 0x7FFF;
}

uint OAM16(uint line, uint engine, uint address)
{
    return (VideoMemoryWord(line, OAMKilobyte + engine, (address & 0x3FF) >> 2) >> ((address & 2) * 8)) & 0xFFFF;
}

uint ObjectsOnLine(uint line, uint engine, uint group)
{
    return frame[ObjectLinesStart + (line * 2 + engine) * ObjectGroups + group];
}

bool IsPastScreen(int x)
{
    return x < 0 || x >= 256;
}

uint SuppliedColumns(uint engine, uint bg)
{
    return frame[SuppliedColumnsStart + engine * 4 + bg];
}

uint MarginWindow(uint engine, uint window, uint line)
{
    return frame[MarginWindowsStart + (engine * 2 + window) * Lines + line];
}

uint MarginEntry(uint engine, uint bg, int column, int row)
{
    uint index = column <= 0 ? uint(-column) : uint(column - 32) + MarginColumnsPerSide;
    if (index >= 2 * MarginColumnsPerSide || (SuppliedColumns(engine, bg) & (1u << index)) == 0 || row < 0 || row >= int(MarginRows))
    {
        return Transparent;
    }

    uint entry = ((engine * 4 + bg) * 2 * MarginColumnsPerSide + index) * MarginRows + uint(row);
    return (frame[MarginTilesStart + (entry >> 1)] >> ((entry & 1) * 16)) & 0xFFFF;
}

uint CharacterBase(uint line, uint engine, uint control)
{
    uint dispcnt = Register32(line, engine, DISPCNT);
    return ((control >> 2) & 0xF) * 0x4000 + (engine == EngineA ? ((dispcnt >> 24) & 7) * 0x10000 : 0);
}

uint ScreenBase(uint line, uint engine, uint control)
{
    uint dispcnt = Register32(line, engine, DISPCNT);
    return ((control >> 8) & 0x1F) * 0x800 + (engine == EngineA ? ((dispcnt >> 27) & 7) * 0x10000 : 0);
}

uint TileColor256(uint line, uint engine, uint bg, uint index, uint palette)
{
    if (index == 0)
    {
        return Transparent;
    }

    if ((Register32(line, engine, DISPCNT) & (1u << 30)) == 0)
    {
        return PaletteColor(line, engine, index);
    }

    uint control = Register(line, engine, BG0CNT + bg * 2);
    uint slot = bg < 2 && (control & 0x2000) != 0 ? bg + 2 : bg;
    return ExtendedPaletteColor(line, EngineABGExtendedPalette + engine, slot * 0x1000 + palette * 256 + index);
}

uint TextBackgroundPixel(uint line, uint engine, uint bg, int2 pixel)
{
    uint control = Register(line, engine, BG0CNT + bg * 2);
    uint area = EngineABG + engine;
    uint characterBase = CharacterBase(line, engine, control);
    uint screenBase = ScreenBase(line, engine, control);
    uint size = control >> 14;

    int scrollX = int(Register(line, engine, BG0HOFS + bg * 4) & 0x1FF);
    int scrollY = int(Register(line, engine, BG0VOFS + bg * 4) & 0x1FF);
    int mapX = pixel.x + scrollX;
    int mapY = pixel.y + scrollY;
    uint x = uint(mapX) & 0x1FF;
    uint y = uint(mapY) & 0x1FF;

    uint entry;
    if (IsPastScreen(pixel.x))
    {
        entry = MarginEntry(engine, bg, (mapX >> 3) - (scrollX >> 3), (mapY >> 3) - (scrollY >> 3));
        if (entry == Transparent)
        {
            return Transparent;
        }
    }
    else
    {
        uint blockX = (size & 1) != 0 ? (x >> 8) & 1 : 0;
        uint blockY = (size & 2) != 0 ? (y >> 8) & 1 : 0;
        uint block = size == 3 ? blockY * 2 + blockX : blockX + blockY;
        entry = VRAM16(line, area, screenBase + block * 0x800 + (((y >> 3) & 31) * 32 + ((x >> 3) & 31)) * 2);
    }

    uint tile = entry & 0x3FF;
    uint tileX = (entry & 0x400) != 0 ? 7 - (x & 7) : x & 7;
    uint tileY = (entry & 0x800) != 0 ? 7 - (y & 7) : y & 7;

    if ((control & 0x80) != 0)
    {
        return TileColor256(line, engine, bg, VRAM8(line, area, characterBase + tile * 64 + tileY * 8 + tileX), entry >> 12);
    }

    uint index = (VRAM8(line, area, characterBase + tile * 32 + tileY * 4 + (tileX >> 1)) >> ((tileX & 1) * 4)) & 0xF;
    return index == 0 ? Transparent : PaletteColor(line, engine, (entry >> 12) * 16 + index);
}

bool Wrap(inout int2 texel, int2 size, uint control)
{
    if ((control & 0x2000) != 0)
    {
        texel = int2(Modulo(texel.x, uint(size.x)), Modulo(texel.y, uint(size.y)));
        return true;
    }

    return texel.x >= 0 && texel.x < size.x && texel.y >= 0 && texel.y < size.y;
}

static const uint KindNone = 0;
static const uint KindText = 1;
static const uint KindAffine = 2;
static const uint KindExtended = 3;
static const uint KindLarge = 4;
static const uint Kind3D = 5;

static const uint BitmapWidths[4] = { 128, 256, 512, 512 };
static const uint BitmapHeights[4] = { 128, 256, 256, 512 };

uint AffineBackgroundPixel(uint line, uint engine, uint bg, uint kind, int2 pixel)
{
    uint control = Register(line, engine, BG0CNT + bg * 2);
    uint area = EngineABG + engine;
    uint matrix = BG2PA + (bg - 2) * 0x10;
    int pa = SignExtend(Register(line, engine, matrix), 16);
    int pb = SignExtend(Register(line, engine, matrix + 2), 16);
    int pc = SignExtend(Register(line, engine, matrix + 4), 16);
    int pd = SignExtend(Register(line, engine, matrix + 6), 16);

    int down = pixel.y - int(line);
    int2 texel = int2(
        (ReferencePoint(line, engine, (bg - 2) * 2) + pa * pixel.x + pb * down) >> 8,
        (ReferencePoint(line, engine, (bg - 2) * 2 + 1) + pc * pixel.x + pd * down) >> 8);

    uint size = control >> 14;
    if (kind == KindLarge)
    {
        int2 largeSize = size == 0 ? int2(512, 1024) : int2(1024, 512);
        if (!Wrap(texel, largeSize, control))
        {
            return Transparent;
        }

        uint index = VRAM8(line, area, uint(texel.y * largeSize.x + texel.x));
        return index == 0 ? Transparent : PaletteColor(line, engine, index);
    }

    if (kind == KindExtended && (control & 0x80) != 0)
    {
        int2 bitmapSize = int2(int(BitmapWidths[size]), int(BitmapHeights[size]));
        if (!Wrap(texel, bitmapSize, control))
        {
            return Transparent;
        }

        uint bitmapBase = ((control >> 8) & 0x1F) * 0x4000;
        uint texelIndex = uint(texel.y * bitmapSize.x + texel.x);
        if ((control & 0x04) != 0)
        {
            uint color = VRAM16(line, area, bitmapBase + texelIndex * 2);
            return (color & 0x8000) != 0 ? color & 0x7FFF : Transparent;
        }

        uint index = VRAM8(line, area, bitmapBase + texelIndex);
        return index == 0 ? Transparent : PaletteColor(line, engine, index);
    }

    int mapSize = 128 << size;
    if (!Wrap(texel, int2(mapSize, mapSize), control))
    {
        return Transparent;
    }

    uint characterBase = CharacterBase(line, engine, control);
    uint screenBase = ScreenBase(line, engine, control);
    uint mapIndex = uint(texel.y >> 3) * uint(mapSize >> 3) + uint(texel.x >> 3);
    if (kind == KindExtended)
    {
        uint entry = VRAM16(line, area, screenBase + mapIndex * 2);
        uint tileX = (entry & 0x400) != 0 ? 7 - uint(texel.x & 7) : uint(texel.x & 7);
        uint tileY = (entry & 0x800) != 0 ? 7 - uint(texel.y & 7) : uint(texel.y & 7);
        return TileColor256(line, engine, bg, VRAM8(line, area, characterBase + (entry & 0x3FF) * 64 + tileY * 8 + tileX), entry >> 12);
    }

    uint tile = VRAM8(line, area, screenBase + mapIndex);
    uint index = VRAM8(line, area, characterBase + tile * 64 + uint(texel.y & 7) * 8 + uint(texel.x & 7));
    return index == 0 ? Transparent : PaletteColor(line, engine, index);
}

uint BackgroundKind(uint line, uint engine, uint bg)
{
    uint dispcnt = Register32(line, engine, DISPCNT);
    uint mode = dispcnt & 7;
    if (bg == 0 && engine == EngineA && ((dispcnt & 0x08) != 0 || mode == 6))
    {
        return Kind3D;
    }

    switch (mode)
    {
        case 0: return KindText;
        case 1: return bg == 3 ? KindAffine : KindText;
        case 2: return bg >= 2 ? KindAffine : KindText;
        case 3: return bg == 3 ? KindExtended : KindText;
        case 4: return bg == 3 ? KindExtended : bg == 2 ? KindAffine : KindText;
        case 5: return bg >= 2 ? KindExtended : KindText;
        case 6: return bg == 2 ? KindLarge : KindNone;
        default: return KindNone;
    }
}

uint BackgroundPixel(uint line, uint engine, uint bg, uint kind, int x)
{
    int2 pixel = int2(x, int(line));
    if ((Register(line, engine, BG0CNT + bg * 2) & 0x40) != 0)
    {
        uint mosaic = Register(line, engine, MOSAIC);
        pixel.x -= Modulo(pixel.x, (mosaic & 0xF) + 1);
        pixel.y -= Modulo(pixel.y, ((mosaic >> 4) & 0xF) + 1);
    }

    if (kind == KindText)
    {
        return TextBackgroundPixel(line, engine, bg, pixel);
    }

    return IsPastScreen(pixel.x) ? Transparent : AffineBackgroundPixel(line, engine, bg, kind, pixel);
}

struct ObjectPixel
{
    uint color;
    uint priority;
    bool isSemiTransparent;
    uint bitmapAlpha;
    bool isInObjectWindow;
};

ObjectPixel NoObject()
{
    ObjectPixel none;
    none.color = Transparent;
    none.priority = 4;
    none.isSemiTransparent = false;
    none.bitmapAlpha = 0;
    none.isInObjectWindow = false;
    return none;
}

static const uint ObjectSizes[12] =
{
    0x0808, 0x1010, 0x2020, 0x4040,
    0x0810, 0x0820, 0x1020, 0x2040,
    0x1008, 0x2008, 0x2010, 0x4020,
};

static const uint ModeSemiTransparent = 1;
static const uint ModeWindow = 2;
static const uint ModeBitmap = 3;

uint ObjectTexel(uint line, uint engine, uint attribute0, uint attribute2, uint width, uint2 texel)
{
    uint dispcnt = Register32(line, engine, DISPCNT);
    uint area = EngineAOBJ + engine;
    uint tile = attribute2 & 0x3FF;

    if (((attribute0 >> 10) & 3) == ModeBitmap)
    {
        uint address;
        if ((dispcnt & 0x40) != 0)
        {
            address = tile * ((dispcnt & (1u << 22)) != 0 ? 256 : 128) + (texel.y * width + texel.x) * 2;
        }
        else
        {
            bool isWide = (dispcnt & 0x20) != 0;
            uint maskX = isWide ? 0x1F : 0x0F;
            address = (tile & maskX) * 0x10 + (tile & ~maskX) * 0x80 + (texel.y * (isWide ? 256 : 128) + texel.x) * 2;
        }

        uint color = VRAM16(line, area, address);
        return (color & 0x8000) != 0 ? color & 0x7FFF : Transparent;
    }

    bool is8Bit = (attribute0 & 0x2000) != 0;
    uint2 tileOffset = texel >> 3;
    uint2 pixel = texel & 7;
    uint inTile = is8Bit ? pixel.y * 8 + pixel.x : pixel.y * 4 + (pixel.x >> 1);

    uint address;
    if ((dispcnt & 0x10) != 0)
    {
        uint boundary = 32u << ((dispcnt >> 20) & 3);
        address = tile * boundary + (tileOffset.y * (width >> 3) + tileOffset.x) * (is8Bit ? 64 : 32) + inTile;
    }
    else
    {
        uint firstTile = is8Bit ? tile & ~1u : tile;
        address = ((firstTile + tileOffset.y * 32 + tileOffset.x * (is8Bit ? 2 : 1)) * 32 + inTile) & 0x7FFF;
    }

    if (is8Bit)
    {
        uint index = VRAM8(line, area, address);
        if (index == 0)
        {
            return Transparent;
        }

        return (dispcnt & (1u << 31)) != 0
            ? ExtendedPaletteColor(line, EngineAOBJExtendedPalette + engine, (attribute2 >> 12) * 256 + index)
            : PaletteColor(line, engine, 256 + index);
    }

    uint index = (VRAM8(line, area, address) >> ((pixel.x & 1) * 4)) & 0xF;
    return index == 0 ? Transparent : PaletteColor(line, engine, 256 + (attribute2 >> 12) * 16 + index);
}

ObjectPixel ObjectPixelAt(uint line, uint engine, int x)
{
    return ObjectPixelAt(line, engine, x, int(line), false, false);
}

ObjectPixel ObjectPixelAt(uint line, uint engine, int x, int objectLine, bool startsOnScreen, bool continues)
{
    ObjectPixel result = NoObject();
    uint bestRank = 0xFFFFFFFF;

    uint dispcnt = Register32(line, engine, DISPCNT);
    bool findsObjectWindow = (dispcnt & 0x8000) != 0;
    uint mosaic = Register(line, engine, MOSAIC);

    for (uint group = 0; group < ObjectGroups; group++)
    {
        uint objects = ObjectsOnLine(line, engine, group);
        while (objects != 0)
        {
            uint n = group * 32 + firstbitlow(objects);
            objects &= objects - 1;

            uint attribute0 = OAM16(line, engine, n * 8);
            uint attribute1 = OAM16(line, engine, n * 8 + 2);
            uint attribute2 = OAM16(line, engine, n * 8 + 4);
            uint mode = (attribute0 >> 10) & 3;
            uint rank = ((attribute2 >> 10) & 3) * 128 + n;
            if (mode == ModeWindow ? !findsObjectWindow || result.isInObjectWindow : rank >= bestRank)
            {
                continue;
            }

            if (mode == ModeBitmap && (attribute2 >> 12) == 0)
            {
                continue;
            }

            if ((startsOnScreen && (attribute0 & 0xFF) >= Lines) || (continues && mode == ModeWindow))
            {
                continue;
            }

            bool isAffine = (attribute0 & 0x100) != 0;
            bool isDoubleSize = isAffine && (attribute0 & 0x200) != 0;
            uint size = ObjectSizes[(attribute0 >> 14) * 4 + (attribute1 >> 14)];
            int width = int(size & 0xFF);
            int height = int(size >> 8);
            int boundsWidth = isDoubleSize ? width * 2 : width;
            int boundsHeight = isDoubleSize ? height * 2 : height;

            int left = int(attribute1 & 0x1FF);
            if (left >= 384)
            {
                left -= 512;
            }

            int2 texel = int2(x - left, continues ? objectLine - int(attribute0 & 0xFF) : int((line - (attribute0 & 0xFF)) & 0xFF));
            if (texel.x < 0 || texel.x >= boundsWidth || texel.y < 0 || texel.y >= boundsHeight)
            {
                continue;
            }

            if ((attribute0 & 0x1000) != 0)
            {
                texel.x = max(texel.x - Modulo(x, ((mosaic >> 8) & 0xF) + 1), 0);
                texel.y = max(texel.y - Modulo(objectLine, (mosaic >> 12) + 1), 0);
            }

            if (isAffine)
            {
                uint parameterGroup = ((attribute1 >> 9) & 0x1F) * 32;
                int pa = SignExtend(OAM16(line, engine, parameterGroup + 6), 16);
                int pb = SignExtend(OAM16(line, engine, parameterGroup + 14), 16);
                int pc = SignExtend(OAM16(line, engine, parameterGroup + 22), 16);
                int pd = SignExtend(OAM16(line, engine, parameterGroup + 30), 16);

                int2 fromCenter = texel - int2(boundsWidth, boundsHeight) / 2;
                texel = int2((pa * fromCenter.x + pb * fromCenter.y) >> 8, (pc * fromCenter.x + pd * fromCenter.y) >> 8) + int2(width, height) / 2;
                if (texel.x < 0 || texel.x >= width || texel.y < 0 || texel.y >= height)
                {
                    continue;
                }
            }
            else
            {
                if ((attribute1 & 0x1000) != 0) texel.x = width - 1 - texel.x;
                if ((attribute1 & 0x2000) != 0) texel.y = height - 1 - texel.y;
            }

            uint color = ObjectTexel(line, engine, attribute0, attribute2, uint(width), uint2(texel));
            if (color == Transparent)
            {
                continue;
            }

            if (mode == ModeWindow)
            {
                result.isInObjectWindow = true;
            }
            else
            {
                result.color = color;
                result.priority = (attribute2 >> 10) & 3;
                result.isSemiTransparent = mode == ModeSemiTransparent || mode == ModeBitmap;
                result.bitmapAlpha = mode == ModeBitmap ? (attribute2 >> 12) : 0;
                bestRank = rank;
            }
        }
    }

    return result;
}

bool IsInsideSpan(int x, int left, int right)
{
    return left <= right ? x >= left && x < right : x >= left || x < right;
}

bool IsInsideWindow(uint line, uint engine, uint window, int x)
{
    if ((LineWord(line, engine * EngineWords + ActiveWindowsWord) & (1u << window)) == 0)
    {
        return false;
    }

    if (IsPastScreen(x))
    {
        uint edges = MarginWindow(engine, window, line);
        if (edges != NoMarginWindow)
        {
            return IsInsideSpan(x, SignExtend(edges & 0xFFFF, 16), SignExtend(edges >> 16, 16));
        }

        x = clamp(x, 0, 255);
    }

    uint horizontal = Register(line, engine, WIN0H + window * 2);
    int x1 = int(horizontal >> 8);
    int x2 = int(horizontal & 0xFF);
    if (x1 == 0 && x2 == 0)
    {
        return false;
    }

    if (x2 == 0 || x1 > x2)
    {
        x2 = 256;
    }

    return x >= x1 && x < x2;
}

uint WindowControl(uint line, uint engine, int x, bool isInObjectWindow)
{
    uint dispcnt = Register32(line, engine, DISPCNT);
    if ((dispcnt & 0xE000) == 0)
    {
        return 0x3F;
    }

    if ((dispcnt & 0x2000) != 0 && IsInsideWindow(line, engine, 0, x))
    {
        return Register(line, engine, WININ) & 0x3F;
    }

    if ((dispcnt & 0x4000) != 0 && IsInsideWindow(line, engine, 1, x))
    {
        return (Register(line, engine, WININ) >> 8) & 0x3F;
    }

    if ((dispcnt & 0x8000) != 0 && isInObjectWindow)
    {
        return (Register(line, engine, WINOUT) >> 8) & 0x3F;
    }

    return Register(line, engine, WINOUT) & 0x3F;
}

uint3 Channels(uint color)
{
    return uint3(color & 31, (color >> 5) & 31, (color >> 10) & 31);
}

struct LayerPixel
{
    uint3 color;
    uint layer;
    bool is3D;
    uint alpha;
};

LayerPixel TwoDPixel(uint color, uint layer)
{
    LayerPixel pixel;
    pixel.color = Channels(color) * 2;
    pixel.layer = layer;
    pixel.is3D = false;
    pixel.alpha = 31;
    return pixel;
}

uint RenderedPixel(uint line, int x)
{
    if (IsPastScreen(x))
    {
        int rendered = x + SignExtend(Register(line, EngineA, BG0HOFS) & 0x1FF, 9);
        uint window = rendered < 0 ? LeftWindow : rendered >= int(Width3D) ? RightWindow : ScreenWindow;
        int column = rendered - WindowOrigins[window];
        return column >= 0 && column < int(Width3D) ? rendering[window * WindowSize + Output3D + line * Width3D + uint(column)] : 0;
    }

    uint scrolled = uint(x + int(Register(line, EngineA, BG0HOFS))) & 0x1FF;
    return scrolled < Width3D ? rendering[Output3D + line * Width3D + scrolled] : 0;
}

uint3 Blend(LayerPixel top, LayerPixel under, uint eva, uint evb)
{
    if (!top.is3D && !under.is3D)
    {
        return min(((top.color >> 1) * eva + (under.color >> 1) * evb) >> 4, uint3(31, 31, 31)) * 2;
    }

    return min((top.color * eva + under.color * evb) >> 4, uint3(63, 63, 63));
}

uint3 Brighten(LayerPixel top, uint evy)
{
    uint brightest = top.is3D ? 63 : 31;
    uint3 color = top.is3D ? top.color : top.color >> 1;
    color += ((brightest - color) * evy) >> 4;
    return top.is3D ? color : color * 2;
}

uint3 Darken(LayerPixel top, uint evy)
{
    uint3 color = top.is3D ? top.color : top.color >> 1;
    color -= (color * evy) >> 4;
    return top.is3D ? color : color * 2;
}

bool IsSupplied(uint line, uint engine, uint bg, int x)
{
    int scrollX = int(Register(line, engine, BG0HOFS + bg * 4) & 0x1FF);
    int scrollY = int(Register(line, engine, BG0VOFS + bg * 4) & 0x1FF);
    int column = ((x + scrollX) >> 3) - (scrollX >> 3);
    int row = ((int(line) + scrollY) >> 3) - (scrollY >> 3);
    return MarginEntry(engine, bg, column, row) != Transparent;
}

bool IsShownBackground(uint line, uint engine, uint bg, uint dispcnt, uint window)
{
    return (dispcnt & (0x100u << bg)) != 0 && (window & (1u << bg)) != 0 && BackgroundKind(line, engine, bg) != KindNone;
}

bool IsAnySupplied(uint line, uint engine, int x, uint dispcnt, uint window)
{
    for (uint bg = 0; bg < 4; bg++)
    {
        if (IsShownBackground(line, engine, bg, dispcnt, window) && BackgroundKind(line, engine, bg) == KindText && IsSupplied(line, engine, bg, x))
        {
            return true;
        }
    }

    return false;
}

uint3 UnsuppliedColor(uint line, uint engine, int x, uint dispcnt, uint window)
{
    if (!IsShownBackground(line, engine, 0, dispcnt, window) || BackgroundKind(line, engine, 0) != Kind3D)
    {
        return uint3(0, 0, 0);
    }

    uint rendered = RenderedPixel(line, x);
    uint3 color = uint3(rendered & 0x3F, (rendered >> 8) & 0x3F, (rendered >> 16) & 0x3F);
    uint eva = ((rendered >> 24) & 0x1F) + 1;
    return (rendered >> 24) == 0 ? uint3(0, 0, 0) : eva == 32 ? color : ((color * eva) + 16) >> 5;
}

LayerPixel CoverPixel(uint engine, uint layers, float2 position)
{
    float scale = (256.0 + 2.0 * float(parameters.margin)) / 256.0;
    float2 center = float2(128.0, float(Lines) / 2.0);
    int2 pixel = clamp(int2(floor((position - center) / scale + center)), int2(0, 0), int2(255, int(Lines) - 1));
    uint line = uint(pixel.y);
    uint dispcnt = Register32(line, engine, DISPCNT);
    for (uint priority = 0; priority < 4; priority++)
    {
        for (uint bg = 0; bg < 4; bg++)
        {
            uint kind = BackgroundKind(line, engine, bg);
            if ((layers & (1u << bg)) == 0 || (dispcnt & (0x100u << bg)) == 0 || kind == KindNone || kind == Kind3D || (Register(line, engine, BG0CNT + bg * 2) & 3) != priority)
            {
                continue;
            }

            uint color = BackgroundPixel(line, engine, bg, kind, pixel.x);
            if (color != Transparent)
            {
                return TwoDPixel(color, bg);
            }
        }
    }

    return TwoDPixel(PaletteColor(line, engine, 0), LayerBackdrop);
}

uint3 GraphicsColor(uint line, uint engine, int x, uint hiddenLayers, bool isBelowJoin, float2 position)
{
    uint covered = frame[CoveredLayersStart + engine];
    uint dispcnt = Register32(line, engine, DISPCNT) & ~((hiddenLayers | covered) << 8);

    ObjectPixel object = NoObject();
    if ((dispcnt & 0x1000) != 0)
    {
        object = ObjectPixelAt(line, engine, x, int(line), isBelowJoin, false);
    }

    if (isBelowJoin && (Register32(Lines - 1, engine ^ 1, DISPCNT) & 0x1000) != 0)
    {
        ObjectPixel above = ObjectPixelAt(Lines - 1, engine ^ 1, x, int(Lines + line), true, true);
        if (above.color != Transparent && (object.color == Transparent || above.priority <= object.priority))
        {
            above.isInObjectWindow = object.isInObjectWindow;
            object = above;
        }
    }

    uint window = WindowControl(line, engine, x, object.isInObjectWindow);
    if ((window & (1u << LayerOBJ)) == 0)
    {
        object.color = Transparent;
    }

    if (IsPastScreen(x) && !IsAnySupplied(line, engine, x, dispcnt, window))
    {
        return covered != 0 ? Under3D(line, engine, x, CoverPixel(engine, covered, position).color) : UnsuppliedColor(line, engine, x, dispcnt, window);
    }

    LayerPixel top = covered != 0 ? CoverPixel(engine, covered, position) : TwoDPixel(PaletteColor(line, engine, 0), LayerBackdrop);
    LayerPixel under = top;
    uint found = 0;

    for (uint priority = 0; priority < 4 && found < 2; priority++)
    {
        if (object.color != Transparent && object.priority == priority)
        {
            if (found == 0) top = TwoDPixel(object.color, LayerOBJ);
            else under = TwoDPixel(object.color, LayerOBJ);
            found++;
        }

        for (uint bg = 0; bg < 4 && found < 2; bg++)
        {
            uint kind = BackgroundKind(line, engine, bg);
            bool isShown = (dispcnt & (0x100u << bg)) != 0 && (window & (1u << bg)) != 0 && kind != KindNone;
            if (!isShown || (Register(line, engine, BG0CNT + bg * 2) & 3) != priority)
            {
                continue;
            }

            LayerPixel pixel;
            if (kind == Kind3D)
            {
                uint rendered = RenderedPixel(line, x);
                if ((rendered >> 24) == 0)
                {
                    continue;
                }

                pixel.color = uint3(rendered & 0x3F, (rendered >> 8) & 0x3F, (rendered >> 16) & 0x3F);
                pixel.layer = bg;
                pixel.is3D = true;
                pixel.alpha = (rendered >> 24) & 0x1F;
            }
            else
            {
                uint color = BackgroundPixel(line, engine, bg, kind, x);
                if (color == Transparent)
                {
                    continue;
                }

                pixel = TwoDPixel(color, bg);
            }

            if (found == 0) top = pixel;
            else under = pixel;
            found++;
        }
    }

    uint blend = Register(line, engine, BLDCNT);
    bool underIsSecondTarget = (blend & (0x100u << under.layer)) != 0;
    if (top.is3D && underIsSecondTarget)
    {
        uint eva = top.alpha + 1;
        return eva == 32 ? top.color : ((top.color * eva) + (under.color * (32 - eva)) + 16) >> 5;
    }

    if ((window & WindowEffects) == 0)
    {
        return top.color;
    }

    uint effect = (blend >> 6) & 3;
    bool isSemiTransparent = top.layer == LayerOBJ && object.isSemiTransparent;
    bool topIsFirstTarget = isSemiTransparent || (blend & (1u << top.layer)) != 0;

    if (underIsSecondTarget && (isSemiTransparent || (effect == 1 && topIsFirstTarget)))
    {
        uint alpha = Register(line, engine, BLDALPHA);
        uint eva = min(alpha & 0x1F, 16);
        uint evb = min((alpha >> 8) & 0x1F, 16);
        if (top.layer == LayerOBJ && object.bitmapAlpha != 0)
        {
            eva = object.bitmapAlpha + 1;
            evb = 16 - eva;
        }

        return Blend(top, under, eva, evb);
    }

    if (effect == 2 && topIsFirstTarget)
    {
        return Brighten(top, min(Register(line, engine, BLDY) & 0x1F, 16));
    }

    if (effect == 3 && topIsFirstTarget)
    {
        return Darken(top, min(Register(line, engine, BLDY) & 0x1F, 16));
    }

    return top.color;
}

static const uint3 Black = uint3(0, 0, 0);
static const uint3 White = uint3(63, 63, 63);

uint3 VRAMDisplayColor(uint line, uint dispcnt, int x)
{
    uint bank = (dispcnt >> 18) & 3;
    uint control = VRAMCNT(line, bank);
    if (IsPastScreen(x) || (control & 0x80) == 0 || (control & MSTMasks[bank]) != 0)
    {
        return Black;
    }

    uint offset = (line * 256 + uint(x)) * 2;
    return Channels(BankWord(line, bank, offset & ~3u) >> ((offset & 2) * 8)) * 2;
}

uint3 ApplyMasterBrightness(uint3 color, uint masterBright)
{
    uint factor = min(masterBright & 0x1F, 16);
    switch (masterBright >> 14)
    {
        case 1: return color + (63 - color) * factor / 16;
        case 2: return color - color * factor / 16;
        default: return color;
    }
}

uint3 ScreenColor(uint line, uint engine, int x, uint hiddenLayers, bool isBelowJoin, float2 position)
{
    uint powcnt1 = LineWord(line, POWCNT1Word);
    if ((powcnt1 & BothLCDsEnable) == 0)
    {
        return Black;
    }

    uint dispcnt = Register32(line, engine, DISPCNT);
    uint displayMode = (dispcnt >> 16) & 3;
    if (displayMode == 0)
    {
        return White;
    }

    uint3 color;
    if (displayMode == 2)
    {
        color = VRAMDisplayColor(line, dispcnt, x);
    }
    else if ((powcnt1 & EnginePower[engine]) == 0)
    {
        color = engine == EngineA ? Black : White;
    }
    else if ((dispcnt & 0x80) != 0)
    {
        color = White;
    }
    else
    {
        color = GraphicsColor(line, engine, x, hiddenLayers, isBelowJoin, position);
    }

    return ApplyMasterBrightness(color, Register(line, engine, MASTER_BRIGHT));
}

static const uint InsetObjects = 1u << 4;

uint3 Under3D(uint line, uint engine, int x, uint3 color)
{
    if ((Register32(line, engine, DISPCNT) & 0x100) == 0 || BackgroundKind(line, engine, 0) != Kind3D)
    {
        return color;
    }

    uint rendered = RenderedPixel(line, x);
    uint eva = ((rendered >> 24) & 0x1F) + 1;
    uint3 over = uint3(rendered & 0x3F, (rendered >> 8) & 0x3F, (rendered >> 16) & 0x3F);
    return (rendered >> 24) == 0 ? color : eva == 32 ? over : ((over * eva) + (color * (32 - eva)) + 16) >> 5;
}

bool TryInsetColor(uint line, uint engine, int x, uint layers, out uint3 color)
{
    color = Black;
    uint dispcnt = Register32(line, engine, DISPCNT);
    ObjectPixel object = NoObject();
    if ((layers & InsetObjects) != 0 && (dispcnt & 0x1000) != 0)
    {
        object = ObjectPixelAt(line, engine, x);
    }

    for (uint priority = 0; priority < 4; priority++)
    {
        bool isFound = false;
        if (object.color != Transparent && object.priority == priority)
        {
            color = Channels(object.color) * 2;
            isFound = true;
        }

        for (uint bg = 0; bg < 4 && !isFound; bg++)
        {
            uint kind = BackgroundKind(line, engine, bg);
            if ((layers & (1u << bg)) == 0 || (dispcnt & (0x100u << bg)) == 0 || kind == KindNone || kind == Kind3D || (Register(line, engine, BG0CNT + bg * 2) & 3) != priority)
            {
                continue;
            }

            uint pixel = BackgroundPixel(line, engine, bg, kind, x);
            if (pixel != Transparent)
            {
                color = Channels(pixel) * 2;
                isFound = true;
            }
        }

        if (!isFound)
        {
            continue;
        }

        if ((Register(line, engine, BG0CNT) & 3) <= priority)
        {
            color = Under3D(line, engine, x, color);
        }

        return true;
    }

    return false;
}

uint3 InsetsOver(uint3 color, float2 view, uint line, uint baseEngine, int x)
{
    uint count = frame[InsetCountWord];
    for (uint index = 0; index < count; index++)
    {
        uint start = InsetListStart + index * InsetWords;
        uint engine = frame[start] & 0xFF;
        uint layers = frame[start] >> 8;
        int2 source = int2(int(frame[start + 1]), int(frame[start + 2]));
        int2 size = int2(int(frame[start + 3]), int(frame[start + 4]));
        float2 corner = float2(asfloat(frame[start + 5]), asfloat(frame[start + 6]));
        float scale = asfloat(frame[start + 7]);
        int2 texel = int2(floor((view - corner) / scale));
        if (any(texel < int2(0, 0)) || any(texel >= size))
        {
            continue;
        }

        int2 pixel = source + texel;
        uint3 insetColor;
        if (pixel.y >= 0 && pixel.y < int(Lines) && TryInsetColor(uint(pixel.y), engine, pixel.x, layers, insetColor))
        {
            color = engine == baseEngine
                ? ApplyMasterBrightness(insetColor, Register(uint(pixel.y), engine, MASTER_BRIGHT))
                : ApplyMasterBrightness(Under3D(line, baseEngine, x, insetColor), Register(line, baseEngine, MASTER_BRIGHT));
        }
    }

    return color;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void DrawFrame(uint3 id : SV_DispatchThreadID)
{
    int x = int(id.x / parameters.scale) - int(parameters.margin);
    uint y = id.y / parameters.scale;
    if (x >= 256 + int(parameters.margin) || y >= (parameters.sizing == SizingEven ? 2 * Lines : Lines))
    {
        return;
    }

    bool isScrolled = parameters.sizing == SizingScrolled || parameters.sizing == SizingCropped;
    bool isStacked = parameters.sizing == SizingEven || isScrolled;
    y = isScrolled ? (id.y + parameters.scroll) / parameters.scale : y;
    uint line = y % Lines;
    bool isTop = isStacked ? y < Lines : parameters.sizing == SizingTopOnly;
    bool isEngineAOnTop = (LineWord(line, POWCNT1Word) & DisplaySwap) != 0;
    uint engine = isEngineAOnTop == isTop ? EngineA : EngineB;
    bool showsBothScreens = isScrolled && parameters.scroll < Lines * parameters.scale;
    float2 view = (float2(id.xy) + 0.5) / float(parameters.scale);
    float lineY = isScrolled ? (float(id.y + parameters.scroll) + 0.5) / float(parameters.scale) : view.y;
    float2 position = float2(view.x - float(parameters.margin), fmod(lineY, float(Lines)));
    uint3 color = ScreenColor(line, engine, x, frame[HiddenLayersStart + engine], showsBothScreens && !isTop, position);
    output[id.xy] = float4(float3(InsetsOver(color, view, line, engine, x)) / 63.0, 1);
}
