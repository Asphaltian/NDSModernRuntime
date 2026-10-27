static const uint Lines = 192;
static const uint WordsPerLine = 70;
static const uint EngineWords = 33;
static const uint ReferencePointsWord = 28;
static const uint ActiveWindowsWord = 32;
static const uint VRAMCNTWord = 66;
static const uint POWCNT1Word = 69;

static const uint LayoutOfLineStart = Lines * WordsPerLine;
static const uint ObjectLinesStart = LayoutOfLineStart + Lines;
static const uint ObjectGroups = 4;
static const uint SuppliedColumnsStart = ObjectLinesStart + Lines * 2 * ObjectGroups;
static const uint MarginColumnsPerSide = 12;
static const uint MarginRows = 32;
static const uint MarginTilesStart = SuppliedColumnsStart + 2 * 4;
static const uint MarginWindowsStart = MarginTilesStart + 2 * 4 * 2 * MarginColumnsPerSide * MarginRows / 2;
static const uint NoMarginWindow = 0x80008000;
static const uint InsetsStart = MarginWindowsStart + 2 * 2 * Lines;
static const uint MaxInsets = 4;
static const uint InsetWords = 8;
static const uint InsetCountWord = InsetsStart;
static const uint HiddenLayersStart = InsetsStart + 1;
static const uint CoveredLayersStart = InsetsStart + 3;
static const uint InsetListStart = InsetsStart + 5;
static const uint RenderingStart = InsetListStart + MaxInsets * InsetWords;
static const uint DISP3DCNTWord = RenderingStart;
static const uint PolygonCountWord = RenderingStart + 1;
static const uint PolygonsStartWord = RenderingStart + 2;
static const uint MarginPolygonCountWord = RenderingStart + 3;
static const uint MarginPolygonsStartWord = RenderingStart + 4;
static const uint ControlRegistersStart = RenderingStart + 5;
static const uint ControlRegistersWords = 0x90 / 4;
static const uint CopiesStartWord = ControlRegistersStart + ControlRegistersWords;
static const uint LayoutsStart = CopiesStartWord + 1;
static const uint VideoMemoryKilobytes = 660;
static const uint PalettesKilobyte = 656;
static const uint OAMKilobyte = 658;
static const uint WordsPerKilobyte = 0x400 / 4;

static const uint Width3D = 256;
static const uint BufferSize = Width3D * Lines;
static const uint ColorLayers = 0;
static const uint DepthLayers = 2 * BufferSize;
static const uint AttributeLayers = 4 * BufferSize;
static const uint Output3D = 6 * BufferSize;
static const uint WindowSize = 7 * BufferSize;
static const uint ScreenWindow = 0;
static const uint LeftWindow = 1;
static const uint RightWindow = 2;
static const int WindowOrigins[3] = { 0, -256, 256 };

struct DrawParameters
{
    uint scale;
    uint margin;
    uint sizing;
    uint window;
    uint scroll;
};

[[vk::push_constant]]
ConstantBuffer<DrawParameters> parameters;

[[vk::binding(0, 0)]]
StructuredBuffer<uint> frame;

[[vk::binding(1, 0)]]
[[vk::image_format("rgba8")]]
RWTexture2D<float4> output;

[[vk::binding(2, 0)]]
RWStructuredBuffer<uint> rendering;

static const uint NotMapped = 0xFFFFFFFF;

static const uint EngineABG = 0;
static const uint EngineAOBJ = 2;
static const uint EngineABGExtendedPalette = 4;
static const uint EngineAOBJExtendedPalette = 6;
static const uint Texture = 8;
static const uint TexturePalette = 9;
static const uint AreaMasks[10] = { 0x7FFFF, 0x1FFFF, 0x3FFFF, 0x1FFFF, 0x7FFF, 0x7FFF, 0x1FFF, 0x1FFF, 0x7FFFF, 0x1FFFF };

static const uint BankA = 0;
static const uint BankB = 1;
static const uint BankC = 2;
static const uint BankD = 3;
static const uint BankE = 4;
static const uint BankF = 5;
static const uint BankG = 6;
static const uint BankH = 7;
static const uint BankI = 8;
static const uint Banks = 9;
static const uint BankOffsets[9] = { 0x00000, 0x20000, 0x40000, 0x60000, 0x80000, 0x90000, 0x94000, 0x98000, 0xA0000 };
static const uint BankSizes[9] = { 0x20000, 0x20000, 0x20000, 0x20000, 0x10000, 0x4000, 0x4000, 0x8000, 0x4000 };
static const uint MSTMasks[9] = { 3, 3, 7, 7, 7, 7, 7, 3, 3 };

uint LineWord(uint line, uint word)
{
    return frame[line * WordsPerLine + word];
}

uint Register(uint line, uint engine, uint offset)
{
    return (LineWord(line, engine * EngineWords + (offset >> 2)) >> ((offset & 2) * 8)) & 0xFFFF;
}

uint Register32(uint line, uint engine, uint offset)
{
    return LineWord(line, engine * EngineWords + (offset >> 2));
}

int ReferencePoint(uint line, uint engine, uint index)
{
    return int(LineWord(line, engine * EngineWords + ReferencePointsWord + index));
}

uint VRAMCNT(uint line, uint bank)
{
    return (LineWord(line, VRAMCNTWord + bank / 4) >> ((bank & 3) * 8)) & 0xFF;
}

uint VideoMemoryWord(uint line, uint kilobyte, uint word)
{
    uint layout = frame[LayoutOfLineStart + line];
    uint entry = LayoutsStart + layout * (VideoMemoryKilobytes / 2) + kilobyte / 2;
    uint copy = (frame[entry] >> ((kilobyte & 1) * 16)) & 0xFFFF;
    return frame[frame[CopiesStartWord] + copy * WordsPerKilobyte + word];
}

uint BankWord(uint line, uint bank, uint offset)
{
    uint address = BankOffsets[bank] + offset;
    return VideoMemoryWord(line, address >> 10, (address & 0x3FF) >> 2);
}

uint BankStart(uint line, uint bank, uint area)
{
    uint control = VRAMCNT(line, bank);
    if ((control & 0x80) == 0)
    {
        return NotMapped;
    }

    uint mst = control & MSTMasks[bank];
    uint offset = (control >> 3) & 3;
    uint smallBankOffset = 0x4000 * (offset & 1) + 0x10000 * (offset >> 1);
    bool isSmallBank = bank == BankF || bank == BankG;
    switch (area)
    {
        case EngineABG:
            if (mst == 1 && bank <= BankD) return 0x20000 * offset;
            if (mst == 1 && bank == BankE) return 0;
            if (mst == 1 && isSmallBank) return smallBankOffset;
            return NotMapped;
        case EngineABG + 1:
            if (mst == 4 && bank == BankC) return 0;
            if (mst == 1 && bank == BankH) return 0;
            if (mst == 1 && bank == BankI) return 0x8000;
            return NotMapped;
        case EngineAOBJ:
            if (mst == 2 && (bank == BankA || bank == BankB)) return 0x20000 * (offset & 1);
            if (mst == 2 && bank == BankE) return 0;
            if (mst == 2 && isSmallBank) return smallBankOffset;
            return NotMapped;
        case EngineAOBJ + 1:
            if (mst == 4 && bank == BankD) return 0;
            if (mst == 2 && bank == BankI) return 0;
            return NotMapped;
        case EngineABGExtendedPalette:
            if (mst == 4 && bank == BankE) return 0;
            if (mst == 4 && isSmallBank) return 0x4000 * (offset & 1);
            return NotMapped;
        case EngineABGExtendedPalette + 1:
            return mst == 2 && bank == BankH ? 0 : NotMapped;
        case EngineAOBJExtendedPalette:
            return mst == 5 && isSmallBank ? 0 : NotMapped;
        case EngineAOBJExtendedPalette + 1:
            return mst == 3 && bank == BankI ? 0 : NotMapped;
        case Texture:
            return mst == 3 && bank <= BankD ? 0x20000 * offset : NotMapped;
        case TexturePalette:
            if (mst == 3 && bank == BankE) return 0;
            if (mst == 3 && isSmallBank) return 0x4000 * ((offset & 1) + (offset >> 1) * 4);
            return NotMapped;
        default:
            return NotMapped;
    }
}

uint VRAMWord(uint line, uint area, uint address)
{
    address &= AreaMasks[area];
    uint value = 0;
    for (uint bank = 0; bank < Banks; bank++)
    {
        uint start = BankStart(line, bank, area);
        if (start != NotMapped && address - start < BankSizes[bank])
        {
            value |= BankWord(line, bank, (address - start) & ~3u);
        }
    }

    return value;
}

uint VRAM8(uint line, uint area, uint address)
{
    return (VRAMWord(line, area, address) >> ((address & 3) * 8)) & 0xFF;
}

uint VRAM16(uint line, uint area, uint address)
{
    return (VRAMWord(line, area, address) >> ((address & 2) * 8)) & 0xFFFF;
}

int SignExtend(uint value, uint bits)
{
    return int(value << (32 - bits)) >> (32 - bits);
}

int Modulo(int value, uint divisor)
{
    return int(uint(value % int(divisor) + int(divisor)) % divisor);
}
