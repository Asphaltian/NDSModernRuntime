namespace NTRModern.ARM9;

internal static class IO
{
    private const uint DISPSTAT = 0x004;
    private const uint DMAFill0 = 0x0E0;
    private const uint DMAFill3 = 0x0EC;
    private const uint EXMEMCNT = 0x204;
    private const uint VRAMCNT_A = 0x240;
    private const uint VRAMCNT_E = 0x244;
    private const uint VRAMCNT_H = 0x248;
    private const uint DIVCNT = 0x280;
    private const uint DIV_NUMER = 0x290;
    private const uint DIV_DENOM = 0x298;
    private const uint DIV_RESULT = 0x2A0;
    private const uint DIVREM_RESULT = 0x2A8;
    private const uint SQRTCNT = 0x2B0;
    private const uint SQRT_RESULT = 0x2B4;
    private const uint SQRT_PARAM = 0x2B8;
    private const uint POSTFLG = 0x300;
    private const uint POWCNT1 = 0x304;
    private const uint EngineBRegisters = 0x1000;
    private const uint EXMEMCNTWritable = 0xE8FF;
    private const byte PostBootFlag = 1;
    private const ushort BothLCDsEnable = 1 << 0;
    private const ushort EngineAEnable = 1 << 1;
    private const ushort RenderingEngineEnable = 1 << 2;
    private const ushort GeometryEngineEnable = 1 << 3;
    private const ushort EngineBEnable = 1 << 9;
    private const ushort DisplaySwap = 1 << 15;
    private const ushort POWCNT1Writable = BothLCDsEnable | EngineAEnable | RenderingEngineEnable | GeometryEngineEnable | EngineBEnable | DisplaySwap;
    private const ushort POWCNT1AtBoot = BothLCDsEnable | EngineAEnable | RenderingEngineEnable | GeometryEngineEnable | EngineBEnable | DisplaySwap;

    public static readonly Interrupts Interrupts = new(Processor.ARM9);
    public static readonly Timers Timers = new(Processor.ARM9, Interrupts);
    public static readonly DMA DMA = new(Processor.ARM9, Interrupts);

    private static readonly uint[] DMAFill = new uint[4];

    private static byte _postflg = PostBootFlag;

    public static ushort ExternalMemoryControl { get; private set; }

    public static ushort GraphicsPowerControl { get; private set; } = POWCNT1AtBoot;

    public static bool IsRenderingEnginePowered => (GraphicsPowerControl & RenderingEngineEnable) != 0;

    public static bool IsGeometryEnginePowered => (GraphicsPowerControl & GeometryEngineEnable) != 0;

    public static bool IsEnginePowered(bool isEngineB) => (GraphicsPowerControl & (isEngineB ? EngineBEnable : EngineAEnable)) != 0;

    public static uint Read32(uint address) => Read(address, 4);

    public static ushort Read16(uint address) => (ushort)Read(address, 2);

    public static byte Read8(uint address) => (byte)Read(address, 1);

    public static void Write32(uint address, uint value) => Write(address, 4, value);

    public static void Write16(uint address, ushort value) => Write(address, 2, value);

    public static void Write8(uint address, byte value) => Write(address, 1, value);

    public static void Reset()
    {
        Interrupts.Reset();
        Timers.Reset();
        DMA.Reset();
        ExternalMemoryControl = 0;
        GraphicsPowerControl = POWCNT1AtBoot;
        Array.Clear(DMAFill);
        _postflg = PostBootFlag;
    }

    private static uint Read(uint address, int size)
    {
        var (word, shift, mask) = IORegisters.Locate(address, size);
        uint value = IORegisters.Read(Processor.ARM9, word, size, mask) ?? word switch
        {
            < Engine2D.RegistersEnd and not DISPSTAT => Engine2D.A.Read(word, mask),
            >= EngineBRegisters and < EngineBRegisters + Engine2D.RegistersEnd => Engine2D.B.Read(word - EngineBRegisters, mask),
            >= RenderingEngine.RegistersStart and < RenderingEngine.RegistersEnd => RenderingEngine.Read(word, mask),
            >= GeometryEngine.RegistersStart and < GeometryEngine.RegistersEnd => GeometryEngine.Read(word),
            >= DMAFill0 and <= DMAFill3 => DMAFill[(word - DMAFill0) / 4],
            EXMEMCNT when (mask & 0xFFFF0000) == 0 => ExternalMemoryControl,
            VRAMCNT_E when (mask & 0x00FFFFFF) == 0 => (uint)SharedWRAM.WRAMCNT << 24,
            DIVCNT => Maths.DIVCNT,
            DIV_NUMER => (uint)Maths.DIV_NUMER,
            DIV_NUMER + 4 => (uint)(Maths.DIV_NUMER >> 32),
            DIV_DENOM => (uint)Maths.DIV_DENOM,
            DIV_DENOM + 4 => (uint)(Maths.DIV_DENOM >> 32),
            DIV_RESULT => (uint)Maths.DIV_RESULT,
            DIV_RESULT + 4 => (uint)(Maths.DIV_RESULT >> 32),
            DIVREM_RESULT => (uint)Maths.DIVREM_RESULT,
            DIVREM_RESULT + 4 => (uint)(Maths.DIVREM_RESULT >> 32),
            SQRTCNT => Maths.SQRTCNT,
            SQRT_RESULT => Maths.SQRT_RESULT,
            SQRT_PARAM => (uint)Maths.SQRT_PARAM,
            SQRT_PARAM + 4 => (uint)(Maths.SQRT_PARAM >> 32),
            POSTFLG => _postflg,
            POWCNT1 when (mask & 0xFFFF0000) == 0 => GraphicsPowerControl,
            _ => throw IORegisters.Unsupported(Processor.ARM9, "read", address, size),
        };

        return value >> shift;
    }

    private static void Write(uint address, int size, uint value)
    {
        var (word, shift, mask) = IORegisters.Locate(address, size);
        value <<= shift;
        if (IORegisters.Write(Processor.ARM9, word, size, value, mask))
        {
            return;
        }

        switch (word)
        {
            case < Engine2D.RegistersEnd and not DISPSTAT:
                Engine2D.A.Write(word, value, mask);
                break;
            case >= EngineBRegisters and < EngineBRegisters + Engine2D.RegistersEnd:
                Engine2D.B.Write(word - EngineBRegisters, value, mask);
                break;
            case >= RenderingEngine.RegistersStart and < RenderingEngine.RegistersEnd:
                RenderingEngine.Write(word, value, mask);
                break;
            case >= GeometryEngine.RegistersStart and < GeometryEngine.RegistersEnd:
                GeometryEngine.Write(word, value, mask);
                break;
            case >= DMAFill0 and <= DMAFill3:
                DMAFill[(word - DMAFill0) / 4] = IORegisters.Merge(DMAFill[(word - DMAFill0) / 4], value, mask);
                break;
            case EXMEMCNT when (mask & 0xFFFF0000) == 0:
                ExternalMemoryControl = (ushort)(IORegisters.Merge(ExternalMemoryControl, value, mask) & EXMEMCNTWritable);
                break;
            case VRAMCNT_A:
                WriteVRAMCNT(0, value, mask);
                break;
            case VRAMCNT_E:
                WriteVRAMCNT(4, value, mask & 0x00FFFFFF);
                if ((mask & 0xFF000000) != 0)
                {
                    SharedWRAM.WriteWRAMCNT((byte)(value >> 24));
                }

                break;
            case VRAMCNT_H when (mask & 0xFFFF0000) == 0:
                WriteVRAMCNT(7, value, mask);
                break;
            case DIVCNT:
                Maths.WriteDIVCNT(IORegisters.Merge(Maths.DIVCNT, value, mask));
                break;
            case DIV_NUMER or DIV_NUMER + 4:
                Maths.WriteDIV_NUMER(MergeHalf(Maths.DIV_NUMER, word == DIV_NUMER + 4, value, mask));
                break;
            case DIV_DENOM or DIV_DENOM + 4:
                Maths.WriteDIV_DENOM(MergeHalf(Maths.DIV_DENOM, word == DIV_DENOM + 4, value, mask));
                break;
            case SQRTCNT:
                Maths.WriteSQRTCNT(IORegisters.Merge(Maths.SQRTCNT, value, mask));
                break;
            case SQRT_PARAM or SQRT_PARAM + 4:
                Maths.WriteSQRT_PARAM((ulong)MergeHalf((long)Maths.SQRT_PARAM, word == SQRT_PARAM + 4, value, mask));
                break;
            case POSTFLG when (mask & 0xFF) != 0:
                _postflg = (byte)((_postflg & PostBootFlag) | (int)(value & 3));
                break;
            case POWCNT1 when (mask & 0xFFFF0000) == 0:
                GraphicsPowerControl = (ushort)(IORegisters.Merge(GraphicsPowerControl, value, mask) & POWCNT1Writable);
                break;
            default:
                throw IORegisters.Unsupported(Processor.ARM9, "write", address, size);
        }
    }

    private static void WriteVRAMCNT(int firstBank, uint value, uint mask)
    {
        for (int index = 0; index < 4; index++)
        {
            if ((mask & (0xFFu << (index * 8))) != 0)
            {
                VRAM.WriteVRAMCNT(firstBank + index, (byte)(value >> (index * 8)));
            }
        }
    }

    private static long MergeHalf(long current, bool isHigh, uint value, uint mask)
    {
        int shift = isHigh ? 32 : 0;
        uint half = IORegisters.Merge((uint)(current >> shift), value, mask);
        return (long)(((ulong)current & ~(0xFFFFFFFFUL << shift)) | ((ulong)half << shift));
    }
}
