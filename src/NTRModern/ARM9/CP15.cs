namespace NTRModern.ARM9;

/// <summary>The ARM9's system control coprocessor. This is called by recompiled code, so you won't need it yourself.</summary>
public static class CP15
{
    internal const int ITCMSize = 0x8000;
    internal const int DTCMSize = 0x4000;

    private const int InstructionCacheSize = 0x2000;
    private const int DataCacheSize = 0x1000;

    private const uint MainID = 0x41059461;
    private const uint CacheType = 0x0F0D2112;
    private const uint TCMSizes = 0x00140180;
    private const uint ControlWritable = 0x000FF085;
    private const uint ControlFixed = 0x00000078;
    private const uint ProtectionUnitEnable = 1 << 0;
    private const uint DataCacheEnable = 1 << 2;
    private const uint InstructionCacheEnable = 1 << 12;
    private const uint RoundRobinReplacement = 1 << 14;
    private const uint DTCMEnable = 1 << 16;
    private const uint DTCMLoadMode = 1 << 17;
    private const uint ITCMEnable = 1 << 18;
    private const uint ITCMLoadMode = 1 << 19;
    private const uint RegionMask = 0xFFFFF03F;
    private const uint TCMRegionMask = 0xFFFFF03E;
    private const uint ITCMRegionMask = 0x0000003E;
    private const int PageShift = 12;
    private const byte CodeCachable = 1 << 0;
    private const byte DataCachable = 1 << 1;
    private const byte WriteBack = 1 << 2;

    internal static readonly byte[] ITCM = new byte[ITCMSize];
    internal static readonly byte[] DTCM = new byte[DTCMSize];

    private static readonly uint[] Regions = new uint[8];
    private static readonly byte[] PageCaching = new byte[1 << (32 - PageShift)];

    private static uint _control = ControlFixed;
    private static uint _dataCachable;
    private static uint _instructionCachable;
    private static uint _writeBufferable;
    private static uint _dataPermissions;
    private static uint _instructionPermissions;
    private static uint _extendedDataPermissions;
    private static uint _extendedInstructionPermissions;
    private static uint _dtcmRegion;
    private static uint _itcmRegion;
    private static uint _traceProcessID;

    internal static Cache InstructionCache { get; } = new(InstructionCacheSize);

    internal static Cache DataCache { get; } = new(DataCacheSize);

    internal static bool IsRoundRobin => (_control & RoundRobinReplacement) != 0;

    internal static ulong ITCMReadEnd { get; private set; }

    internal static ulong ITCMWriteEnd { get; private set; }

    internal static uint DTCMBase { get; private set; }

    internal static ulong DTCMReadSize { get; private set; }

    internal static ulong DTCMWriteSize { get; private set; }

    public static uint Read(int opcode, int cn, int cm, int info) => (opcode, cn, cm, info) switch
    {
        (0, 0, 0, 1) => CacheType,
        (0, 0, 0, 2) => TCMSizes,
        (0, 0, 0, 0 or >= 3) => MainID,
        (0, 1, 0, 0) => _control,
        (0, 2, 0, 0) => _dataCachable,
        (0, 2, 0, 1) => _instructionCachable,
        (0, 3, 0, 0) => _writeBufferable,
        (0, 5, 0, 0) => _dataPermissions,
        (0, 5, 0, 1) => _instructionPermissions,
        (0, 5, 0, 2) => _extendedDataPermissions,
        (0, 5, 0, 3) => _extendedInstructionPermissions,
        (0, 6, <= 7, 0 or 1) => Regions[cm],
        (0, 9, 0, 0 or 1) => 0,
        (0, 9, 1, 0) => _dtcmRegion,
        (0, 9, 1, 1) => _itcmRegion,
        (0, 13, 0, 0) => 0,
        (0, 13, 0, 1) or (0, 13, 1, 1) => _traceProcessID,
        _ => throw Unsupported("read", opcode, cn, cm, info),
    };

    public static void Write(int opcode, int cn, int cm, int info, uint value)
    {
        switch (opcode, cn, cm, info)
        {
            case (0, 1, 0, 0):
                _control = (value & ControlWritable) | ControlFixed;
                UpdateTCMs();
                UpdateCaching();
                break;
            case (0, 2, 0, 0):
                _dataCachable = value & 0xFF;
                UpdateCaching();
                break;
            case (0, 2, 0, 1):
                _instructionCachable = value & 0xFF;
                UpdateCaching();
                break;
            case (0, 3, 0, 0):
                _writeBufferable = value & 0xFF;
                UpdateCaching();
                break;
            case (0, 5, 0, 0):
                _dataPermissions = value & 0xFFFF;
                break;
            case (0, 5, 0, 1):
                _instructionPermissions = value & 0xFFFF;
                break;
            case (0, 5, 0, 2):
                _extendedDataPermissions = value;
                break;
            case (0, 5, 0, 3):
                _extendedInstructionPermissions = value;
                break;
            case (0, 6, <= 7, 0 or 1):
                Regions[cm] = value & RegionMask;
                UpdateCaching();
                break;
            case (0, 7, 0, 4) or (0, 7, 8, 2):
                SystemClock.Halt(Processor.ARM9, () => IO.Interrupts.IsPending);
                break;
            case (0, 7, 5, 0):
                InstructionCache.InvalidateAll();
                break;
            case (0, 7, 5, 1):
                InstructionCache.Invalidate(value);
                break;
            case (0, 7, 6, 0):
                DataCache.InvalidateAll();
                break;
            case (0, 7, 6, 1) or (0, 7, 14, 1):
                DataCache.Invalidate(value);
                break;
            case (0, 7, 14, 2):
                DataCache.InvalidateAt(value);
                break;
            case (0, 7, 13, 1):
                if (IsCodeCachable(value))
                {
                    InstructionCache.Load(value, IsRoundRobin);
                }

                break;
            case (0, 7, 10, 1 or 2 or 4):
                break;
            case (0, 9, 0, 0 or 1) when value != 0:
                throw new NotSupportedException($"Cache lockdown (CP15 0,C9,C0,{info}) isn't supported.");
            case (0, 9, 0, 0 or 1):
                break;
            case (0, 9, 1, 0):
                _dtcmRegion = value & TCMRegionMask;
                UpdateTCMs();
                break;
            case (0, 9, 1, 1):
                _itcmRegion = value & ITCMRegionMask;
                UpdateTCMs();
                break;
            case (0, 13, 0, 0):
                break;
            case (0, 13, 0, 1) or (0, 13, 1, 1):
                _traceProcessID = value;
                break;
            default:
                throw Unsupported("write", opcode, cn, cm, info);
        }
    }

    internal static void Reset()
    {
        _control = ControlFixed;
        (_dataCachable, _instructionCachable, _writeBufferable) = (0, 0, 0);
        (_dataPermissions, _instructionPermissions, _extendedDataPermissions, _extendedInstructionPermissions) = (0, 0, 0, 0);
        (_dtcmRegion, _itcmRegion, _traceProcessID) = (0, 0, 0);
        Array.Clear(Regions);
        Array.Clear(ITCM);
        Array.Clear(DTCM);
        UpdateTCMs();
        InstructionCache.Reset();
        DataCache.Reset();
        UpdateCaching();
    }

    internal static bool IsCodeCachable(uint address) => (PageCaching[address >> PageShift] & CodeCachable) != 0;

    internal static bool IsDataCachable(uint address) => (PageCaching[address >> PageShift] & DataCachable) != 0;

    internal static bool IsWriteBack(uint address) => (PageCaching[address >> PageShift] & WriteBack) != 0;

    private static void UpdateCaching()
    {
        Array.Clear(PageCaching);
        if ((_control & ProtectionUnitEnable) == 0)
        {
            return;
        }

        for (int n = 0; n < Regions.Length; n++)
        {
            uint region = Regions[n];
            if ((region & 1) == 0)
            {
                continue;
            }

            int sizeShift = (int)((region >> 1) & 0x1F) + 1;
            if (sizeShift < PageShift)
            {
                throw new NotSupportedException($"Protection region {n} is smaller than 4KB.");
            }

            ulong size = 1UL << sizeShift;
            ulong start = region & 0xFFFFF000;
            if (start % size != 0)
            {
                throw new NotSupportedException($"Protection region {n} at 0x{start:X8} isn't aligned to its size.");
            }

            bool isCode = (_control & InstructionCacheEnable) != 0 && ((_instructionCachable >> n) & 1) != 0;
            bool isData = (_control & DataCacheEnable) != 0 && ((_dataCachable >> n) & 1) != 0;
            bool isWriteBack = isData && ((_writeBufferable >> n) & 1) != 0;
            byte caching = (byte)((isCode ? CodeCachable : 0) | (isData ? DataCachable : 0) | (isWriteBack ? WriteBack : 0));
            PageCaching.AsSpan((int)(start >> PageShift), (int)(size >> PageShift)).Fill(caching);
        }
    }

    private static void UpdateTCMs()
    {
        ulong itcmSize = (_control & ITCMEnable) != 0 ? RegionSize(_itcmRegion) : 0;
        ITCMWriteEnd = itcmSize;
        ITCMReadEnd = (_control & ITCMLoadMode) != 0 ? 0 : itcmSize;

        ulong dtcmSize = (_control & DTCMEnable) != 0 ? RegionSize(_dtcmRegion) : 0;
        DTCMBase = _dtcmRegion & 0xFFFFF000;
        DTCMWriteSize = dtcmSize;
        DTCMReadSize = (_control & DTCMLoadMode) != 0 ? 0 : dtcmSize;
    }

    private static ulong RegionSize(uint region) => 512UL << (int)((region >> 1) & 0x1F);

    private static NotSupportedException Unsupported(string access, int opcode, int cn, int cm, int info)
    {
        return new NotSupportedException($"Can't {access} CP15 register {opcode},C{cn},C{cm},{info}.");
    }
}
