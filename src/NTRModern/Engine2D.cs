namespace NTRModern;

internal sealed class Engine2D
{
    public const uint RegistersEnd = 0x70;
    public const int WordsPerLine = (int)(RegistersEnd / 4) + ReferencePointCount + 1;
    public const uint DISP3DCNT = 0x60;

    public static readonly Engine2D A = new(isEngineB: false);
    public static readonly Engine2D B = new(isEngineB: true);

    private const uint EngineAAddress = 0x04000000;
    private const uint EngineBAddress = 0x04001000;
    private const uint DISPCNT = 0x00;
    private const uint BG0CNT = 0x08;
    private const uint BG0HOFS = 0x10;
    private const uint BG2PA = 0x20;
    private const uint BG2PB = 0x22;
    private const uint BG2PD = 0x26;
    private const uint BG2X = 0x28;
    private const uint BG2Y = 0x2C;
    private const uint BG3PB = 0x32;
    private const uint BG3PD = 0x36;
    private const uint BG3X = 0x38;
    private const uint BG3Y = 0x3C;
    private const uint WIN0H = 0x40;
    private const uint WIN0V = 0x44;
    private const uint WININ = 0x48;
    private const uint WINOUT = 0x4A;
    private const uint MOSAIC = 0x4C;
    private const uint BLDCNT = 0x50;
    private const uint BLDALPHA = 0x52;
    private const uint BLDY = 0x54;
    private const uint DISPCAPCNT = 0x64;
    private const uint DISP_MMEM_FIFO = 0x68;
    private const uint MASTER_BRIGHT = 0x6C;
    private const uint EnginePortsStart = 0x08;
    private const uint EnginePortsEnd = 0x60;
    private const int ReferencePointCount = 4;
    private const int WindowCount = 2;
    private const ushort EngineBDISPCNTLow = 0xFFF7;
    private const ushort EngineBDISPCNTHigh = 0xC0B1;
    private const ushort BGMode = 7;
    private const ushort BitmapOBJReserved = 3 << 5;
    private const ushort DisplayMode = 3;
    private const ushort MainMemoryDisplay = 3;
    private const ushort ColorBufferUnderflow = 1 << 12;
    private const ushort PolygonVertexRAMOverflow = 1 << 13;
    private const ushort DISP3DCNTAcknowledged = ColorBufferUnderflow | PolygonVertexRAMOverflow;
    private const ushort CaptureEnable = 1 << 15;
    private const ushort MasterBrightReserved = 3 << 14;

    private static readonly uint[] ReferencePointRegisters = [BG2X, BG2Y, BG3X, BG3Y];
    private static readonly uint[] ReferencePointSteps = [BG2PB, BG2PD, BG3PB, BG3PD];

    private readonly ushort[] _registers = new ushort[RegistersEnd / 2];
    private readonly ushort[] _written = new ushort[RegistersEnd / 2];
    private readonly int[] _referencePoints = new int[ReferencePointCount];
    private int _activeWindows;

    private Engine2D(bool isEngineB) => IsEngineB = isEngineB;

    public bool IsEngineB { get; }

    public static void Reset()
    {
        A.Clear();
        B.Clear();
    }

    public ushort RegisterAt(uint offset) => _registers[offset / 2];

    public uint Read(uint word, uint mask) => ReadHalf(word, (ushort)mask) | ((uint)ReadHalf(word + 2, (ushort)(mask >> 16)) << 16);

    public void SetPolygonVertexRAMOverflow() => _registers[DISP3DCNT / 2] |= PolygonVertexRAMOverflow;

    public void Write(uint word, uint value, uint mask)
    {
        WriteHalf(word, (ushort)value, (ushort)mask);
        WriteHalf(word + 2, (ushort)(value >> 16), (ushort)(mask >> 16));
    }

    public void UpdateWindows(int line)
    {
        if (!ARM9.IO.IsEnginePowered(IsEngineB))
        {
            return;
        }

        for (int window = 0; window < WindowCount; window++)
        {
            ushort vertical = _registers[(WIN0V / 2) + window];
            if ((line & 0xFF) == (vertical & 0xFF))
            {
                _activeWindows &= ~(1 << window);
            }
            else if ((line & 0xFF) == vertical >> 8)
            {
                _activeWindows |= 1 << window;
            }
        }
    }

    public void ReloadReferencePoints()
    {
        foreach (uint register in ReferencePointRegisters)
        {
            ReloadReferencePoint(register);
        }
    }

    public void AdvanceReferencePoints()
    {
        for (int i = 0; i < ReferencePointCount; i++)
        {
            _referencePoints[i] += (short)_registers[ReferencePointSteps[i] / 2];
        }
    }

    public void CaptureLine(Span<uint> words)
    {
        for (int i = 0; i < _registers.Length / 2; i++)
        {
            words[i] = _registers[2 * i] | ((uint)_registers[(2 * i) + 1] << 16);
        }

        for (int i = 0; i < ReferencePointCount; i++)
        {
            words[(_registers.Length / 2) + i] = (uint)_referencePoints[i];
        }

        words[WordsPerLine - 1] = (uint)_activeWindows;
    }

    private void Clear()
    {
        Array.Clear(_registers);
        Array.Clear(_written);
        Array.Clear(_referencePoints);
        _activeWindows = 0;
    }

    private void ReloadReferencePoint(uint register)
    {
        uint value = _registers[register / 2] | ((uint)_registers[(register / 2) + 1] << 16);
        _referencePoints[Array.IndexOf(ReferencePointRegisters, register)] = (int)(value << 4) >> 4;
    }

    private ushort ReadHalf(uint offset, ushort mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        ushort used = UsedBits(offset);
        if (used == 0)
        {
            return 0;
        }

        if (!IsReadable(offset))
        {
            throw new NotSupportedException($"The ARM9 read the write-only display register at 0x{Address(offset):X8}.");
        }

        if ((mask & used & ~_written[offset / 2]) != 0)
        {
            throw new NotSupportedException($"The ARM9 read the display register at 0x{Address(offset):X8} before writing it.");
        }

        return _registers[offset / 2];
    }

    private void WriteHalf(uint offset, ushort value, ushort mask)
    {
        ushort used = UsedBits(offset);
        if (mask == 0 || used == 0 || (offset is >= EnginePortsStart and < EnginePortsEnd && !ARM9.IO.IsEnginePowered(IsEngineB)))
        {
            return;
        }

        ushort merged = (ushort)((_registers[offset / 2] & ~mask) | (value & mask));
        switch (offset)
        {
            case DISPCNT or DISPCNT + 2 when IsEngineB && (value & mask & ~used) != 0:
                throw new NotSupportedException($"The ARM9 wrote 0x{value & mask:X4} to 0x{Address(offset):X8}, which sets bits only engine A's DISPCNT has.");
            case DISPCNT when (merged & BGMode) >= (IsEngineB ? 6 : 7):
                throw new NotSupportedException($"The ARM9 set reserved BG mode {merged & BGMode} in DISPCNT at 0x{Address(offset):X8}.");
            case DISPCNT when (merged & BitmapOBJReserved) == BitmapOBJReserved:
                throw new NotSupportedException($"The ARM9 set a reserved bitmap OBJ mapping in DISPCNT at 0x{Address(offset):X8}.");
            case DISPCNT + 2 when (merged & DisplayMode) == MainMemoryDisplay:
                throw new NotSupportedException("The ARM9 selected main memory display mode in DISPCNT, which isn't supported.");
            case DISP3DCNT:
                value = (ushort)((value & ~DISP3DCNTAcknowledged) | (_registers[offset / 2] & DISP3DCNTAcknowledged & ~value));
                break;
            case DISPCAPCNT + 2 when (value & mask & CaptureEnable) != 0:
                throw new NotSupportedException("The ARM9 started a display capture, which isn't supported.");
            case DISP_MMEM_FIFO or DISP_MMEM_FIFO + 2:
                throw new NotSupportedException("The ARM9 wrote to the main memory display FIFO, which isn't supported.");
            case MASTER_BRIGHT when (value & mask & MasterBrightReserved) == MasterBrightReserved:
                throw new NotSupportedException($"The ARM9 set reserved brightness mode 3 in MASTER_BRIGHT at 0x{Address(offset):X8}.");
        }

        _registers[offset / 2] = (ushort)((_registers[offset / 2] & ~mask) | (value & mask & used));
        _written[offset / 2] |= mask;
        if (offset is >= BG2X and < BG2X + 8 or >= BG3X and < BG3X + 8)
        {
            ReloadReferencePoint(offset & ~3u);
        }
    }

    private bool IsReadable(uint offset) => offset switch
    {
        DISPCNT or DISPCNT + 2 => true,
        >= BG0CNT and < BG0HOFS => true,
        WININ or WINOUT or BLDCNT or BLDALPHA => true,
        DISP3DCNT or DISPCAPCNT or DISPCAPCNT + 2 => true,
        _ => false,
    };

    private ushort UsedBits(uint offset) => offset switch
    {
        DISPCNT => IsEngineB ? EngineBDISPCNTLow : (ushort)0xFFFF,
        DISPCNT + 2 => IsEngineB ? EngineBDISPCNTHigh : (ushort)0xFFFF,
        >= BG0CNT and < BG0HOFS => 0xFFFF,
        >= BG0HOFS and < BG2PA => 0x01FF,
        BG2X + 2 or BG2Y + 2 or BG3X + 2 or BG3Y + 2 => 0x0FFF,
        >= BG2PA and < WIN0H => 0xFFFF,
        >= WIN0H and < WININ => 0xFFFF,
        WININ or WINOUT => 0x3F3F,
        MOSAIC => 0xFFFF,
        BLDCNT => 0x3FFF,
        BLDALPHA => 0x1F1F,
        BLDY => 0x001F,
        DISP3DCNT when !IsEngineB => 0x7FFF,
        DISPCAPCNT when !IsEngineB => 0x1F1F,
        DISPCAPCNT + 2 when !IsEngineB => 0xEF3F,
        DISP_MMEM_FIFO or DISP_MMEM_FIFO + 2 when !IsEngineB => 0xFFFF,
        MASTER_BRIGHT => 0xC01F,
        _ => 0,
    };

    private uint Address(uint offset) => (IsEngineB ? EngineBAddress : EngineAAddress) + offset;
}
