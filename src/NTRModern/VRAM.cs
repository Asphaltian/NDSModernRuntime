namespace NTRModern;

internal static class VRAM
{
    public const int Size = 0xA4000;

    public static readonly byte[] Data = new byte[Size];

    private const byte Enable = 1 << 7;
    private const int OffsetShift = 3;
    private const int PageSize = 0x4000;
    private const int AreaSize = 0x200000;
    private const int PagesPerArea = AreaSize / PageSize;
    private const int EngineABG = 0;
    private const int EngineBBG = 1;
    private const int EngineAOBJ = 2;
    private const int EngineBOBJ = 3;
    private const int LCDC = 4;
    private const int Areas = 5;
    private const int A = 0;
    private const int B = 1;
    private const int C = 2;
    private const int D = 3;
    private const int E = 4;
    private const int F = 5;
    private const int G = 6;
    private const int H = 7;
    private const int I = 8;

    private static readonly Bank[] Banks =
    [
        new(0x20000, 0x000000, MSTMask: 3, ValidMSTs: [0, 1, 2, 3]),
        new(0x20000, 0x020000, MSTMask: 3, ValidMSTs: [0, 1, 2, 3]),
        new(0x20000, 0x040000, MSTMask: 7, ValidMSTs: [0, 1, 2, 3, 4]),
        new(0x20000, 0x060000, MSTMask: 7, ValidMSTs: [0, 1, 2, 3, 4]),
        new(0x10000, 0x080000, MSTMask: 7, ValidMSTs: [0, 1, 2, 3, 4]),
        new(0x4000, 0x090000, MSTMask: 7, ValidMSTs: [0, 1, 2, 3, 4, 5]),
        new(0x4000, 0x094000, MSTMask: 7, ValidMSTs: [0, 1, 2, 3, 4, 5]),
        new(0x8000, 0x098000, MSTMask: 3, ValidMSTs: [0, 1, 2]),
        new(0x4000, 0x0A0000, MSTMask: 3, ValidMSTs: [0, 1, 2, 3]),
    ];

    private static readonly byte[] Control = new byte[Banks.Length];
    private static readonly Page[] Pages = new Page[Areas * PagesPerArea];

    public static byte VRAMSTAT => (byte)((IsAllocatedToARM7(C) ? 1 : 0) | (IsAllocatedToARM7(D) ? 2 : 0));

    public static ReadOnlySpan<byte> VRAMCNT => Control;

    public static void WriteVRAMCNT(int bank, byte value)
    {
        int mst = value & Banks[bank].MSTMask;
        int offset = (value >> OffsetShift) & 3;
        if ((value & Enable) != 0 && !Banks[bank].ValidMSTs.Contains(mst))
        {
            throw new NotSupportedException($"The ARM9 set VRAM bank {(char)('A' + bank)} to MST {mst}, which that bank doesn't have.");
        }

        if ((value & Enable) != 0 && mst == 2 && bank is A or B or C or D && (offset & 2) != 0)
        {
            throw new NotSupportedException($"The ARM9 set VRAM bank {(char)('A' + bank)} to MST 2 with offset {offset}, which that bank doesn't have.");
        }

        if ((value & Enable) != 0 && mst == 4 && bank is F or G && offset > 1)
        {
            throw new NotSupportedException($"The ARM9 set VRAM bank {(char)('A' + bank)} to MST 4 with offset {offset}, which that bank doesn't have.");
        }

        Control[bank] = value;
        MapPages();
    }

    public static int LocateARM9(uint address)
    {
        int area = (int)((address >> 21) & 7);
        if (area >= Areas)
        {
            throw new NotSupportedException($"The ARM9 accessed 0x{address:X8}, which is past the end of VRAM.");
        }

        var page = Pages[(area * PagesPerArea) + (int)((address & (AreaSize - 1)) / PageSize)];
        if (page.IsOverlapped)
        {
            throw new NotSupportedException($"The ARM9 accessed 0x{address:X8}, where more than one VRAM bank is mapped.");
        }

        if (!page.IsMapped)
        {
            throw new NotSupportedException($"The ARM9 accessed 0x{address:X8}, where no VRAM bank is mapped.");
        }

        return page.Offset + (int)(address & (PageSize - 1));
    }

    public static void Reset()
    {
        Array.Clear(Control);
        Array.Clear(Data);
        MapPages();
    }

    private static void MapPages()
    {
        Array.Clear(Pages);
        for (int bank = 0; bank < Banks.Length; bank++)
        {
            if (CPUMapping(bank) is not { } mapping)
            {
                continue;
            }

            var (area, offset) = mapping;
            for (int bankOffset = 0; bankOffset < Banks[bank].Size; bankOffset += PageSize)
            {
                ref var page = ref Pages[(area * PagesPerArea) + ((offset + bankOffset) / PageSize)];
                page = page.IsMapped || page.IsOverlapped ? new Page(false, 0, true) : new Page(true, Banks[bank].Offset + bankOffset, false);
            }
        }
    }

    private static (int Area, int Offset)? CPUMapping(int bank)
    {
        byte control = Control[bank];
        if ((control & Enable) == 0)
        {
            return null;
        }

        int mst = control & Banks[bank].MSTMask;
        int offset = (control >> OffsetShift) & 3;
        int smallBankOffset = (0x4000 * (offset & 1)) + (0x10000 * (offset >> 1));
        return (bank, mst) switch
        {
            (_, 0) => (LCDC, Banks[bank].Offset),
            ((A or B or C or D), 1) => (EngineABG, 0x20000 * offset),
            (E, 1) => (EngineABG, 0),
            ((F or G), 1) => (EngineABG, smallBankOffset),
            (H, 1) => (EngineBBG, 0),
            (I, 1) => (EngineBBG, 0x8000),
            ((A or B), 2) => (EngineAOBJ, 0x20000 * offset),
            (E, 2) => (EngineAOBJ, 0),
            ((F or G), 2) => (EngineAOBJ, smallBankOffset),
            (I, 2) => (EngineBOBJ, 0),
            (C, 4) => (EngineBBG, 0),
            (D, 4) => (EngineBOBJ, 0),
            _ => null,
        };
    }

    private static bool IsAllocatedToARM7(int bank) => (Control[bank] & Enable) != 0 && (Control[bank] & Banks[bank].MSTMask) == 2;

    private readonly record struct Page(bool IsMapped, int Offset, bool IsOverlapped);

    private sealed record Bank(int Size, int Offset, byte MSTMask, int[] ValidMSTs);
}
