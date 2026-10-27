using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NTRModern.ARM9;

/// <summary>
/// <c>Memory</c> is how you read and write memory the way the ARM9 sees it. Keep in mind that Read and Write cost
/// cycles just like the game's own reads and writes do, so in your own code you'll usually want
/// <see cref="Peek{T}"/> and <see cref="Poke{T}"/> instead, they don't cost anything.
/// </summary>
public static class Memory
{
    private const int PaletteSize = 0x800;
    private const int OAMSize = 0x800;
    private const uint EngineBOffset = 0x400;
    private const long TCMCycles = 1;
    private const long CacheHitCycles = 1;
    private const long MainMemoryLineFillCycles = 23 * SystemClock.BusCycle;
    private const long BIOSLineFillCycles = 11 * SystemClock.BusCycle;
    private const uint BIOSLogoStart = 0xFFFF0020;
    private const int BIOSLogoSize = 0x9C;

    internal static readonly byte[] Palettes = new byte[PaletteSize];
    internal static readonly byte[] OAM = new byte[OAMSize];
    internal static readonly byte[] BIOSLogo = new byte[BIOSLogoSize];

    private static readonly byte[] PoweredOffPalette = new byte[4];

    private static readonly long[] HalfwordCycles = CreateCycles(mainMemory: 18, fast: 8, video: 8);
    private static readonly long[] WordCycles = CreateCycles(mainMemory: 20, fast: 8, video: 10);
    private static readonly long[] SequentialSavings = CreateCycles(mainMemory: 16, fast: 6, video: 6);
    private static readonly long[] ARMFetchCycles = CreateCycles(mainMemory: 18, fast: 8, video: 10);
    private static readonly long[] ThumbFetchCycles = CreateCycles(mainMemory: 9, fast: 4, video: 5);

    /// <summary>Reads memory the same way the game does, so it costs cycles just like the game's own reads.</summary>
    public static uint Read32(uint address) => Read32(address, isCPU: true);

    /// <inheritdoc cref="Read32(uint)"/>
    public static ushort Read16(uint address) => Read16(address, isCPU: true);

    /// <inheritdoc cref="Read32(uint)"/>
    public static byte Read8(uint address)
    {
        ref byte memory = ref Locate(address, isWrite: false, isCPU: true, size: 1, out long cycles);
        Scheduler.Cycles += ReadCycles(address, isCPU: true, cycles);
        if (Unsafe.IsNullRef(ref memory))
        {
            return GBASlot.IsROM(address) ? GBASlot.Read8(Processor.ARM9, address) : IO.Read8(address);
        }

        return memory;
    }

    /// <summary>Writes memory the same way the game does, so it costs cycles just like the game's own writes.</summary>
    public static void Write32(uint address, uint value) => Write32(address, value, isCPU: true);

    /// <inheritdoc cref="Write32(uint, uint)"/>
    public static void Write16(uint address, ushort value) => Write16(address, value, isCPU: true);

    /// <inheritdoc cref="Write32(uint, uint)"/>
    public static void Write8(uint address, byte value)
    {
        ref byte memory = ref Locate(address, isWrite: true, isCPU: true, size: 1, out long cycles);
        Scheduler.Cycles += WriteCycles(address, isCPU: true, cycles);
        if (Unsafe.IsNullRef(ref memory))
        {
            IO.Write8(address, value);
        }
        else if (address >> 24 is not (0x05 or 0x06 or 0x07))
        {
            memory = value;
        }
    }

    /// <summary>
    /// Returns a struct straight from main RAM or the TCMs, without costing any cycles. Please note that it throws if
    /// the struct doesn't fit inside one of them.
    /// </summary>
    /// <example>
    /// <code>
    /// // The game keeps a pointer here, so follow it to the struct behind it
    /// uint dungeon = Memory.Peek&lt;uint&gt;(0x02353538);
    /// var camera = Memory.Peek&lt;Camera&gt;(dungeon + 0x1A21C);
    /// </code>
    /// </example>
    public static ref readonly T Peek<T>(uint address)
        where T : unmanaged
    {
        return ref MemoryMarshal.AsRef<T>(RAM(address, Unsafe.SizeOf<T>(), isWrite: false));
    }

    /// <summary>
    /// Same as <see cref="Peek{T}"/>, but you get the struct itself, so you can change it in place. This doesn't cost
    /// any cycles either, and it throws the same way.
    /// </summary>
    /// <example>
    /// <code>
    /// // Change a number that the game keeps in main RAM
    /// Memory.Poke&lt;int&gt;(0x02100000) = 99999;
    /// </code>
    /// </example>
    public static ref T Poke<T>(uint address)
        where T : unmanaged
    {
        return ref MemoryMarshal.AsRef<T>(RAM(address, Unsafe.SizeOf<T>(), isWrite: true));
    }

    internal static uint Peek32(uint address)
    {
        ref byte memory = ref Locate(address & ~3u, isWrite: false, isCPU: true, size: 4, out _);
        return Unsafe.IsNullRef(ref memory) ? throw new InvalidOperationException($"0x{address:X8} is I/O or the GBA slot, not memory.") : Unsafe.ReadUnaligned<uint>(ref memory);
    }

    internal static uint ReadBus32(uint address) => Read32(address, isCPU: false);

    internal static ushort ReadBus16(uint address) => Read16(address, isCPU: false);

    internal static void WriteBus32(uint address, uint value) => Write32(address, value, isCPU: false);

    internal static void WriteBus16(uint address, ushort value) => Write16(address, value, isCPU: false);

    internal static void Reset()
    {
        Array.Clear(Palettes);
        Array.Clear(OAM);
    }

    internal static long JumpCycles(uint target, bool isThumb)
    {
        return FetchCycles(target, 1, isThumb && (target & 2) == 0) + FetchCycles(target + (isThumb ? 2u : 4u), 1, isThumb);
    }

    internal static long FetchCycles(uint address, int opcodes, bool isThumb)
    {
        if (address >= CP15.ITCMWriteEnd && CP15.IsCodeCachable(address))
        {
            long first = CP15.InstructionCache.Load(address, CP15.IsRoundRobin) ? CacheHitCycles : LineFillCycles(address);
            return first + ((opcodes - 1) * CacheHitCycles);
        }

        return opcodes * BusFetchCycles(address, isThumb ? ThumbFetchCycles : ARMFetchCycles);
    }

    internal static long ReadWordCycles(uint address)
    {
        Locate(address & ~3u, isWrite: false, isCPU: true, size: 4, out long cycles);
        return ReadCycles(address, isCPU: true, cycles);
    }

    internal static long SequentialSaving(uint address, bool isWrite)
    {
        bool isCached = isWrite ? CP15.IsWriteBack(address) : CP15.IsDataCachable(address);
        return IsInITCM(address, isWrite) || IsInDTCM(address, isWrite) || isCached ? 0 : SequentialBusSaving(address);
    }

    internal static long SequentialBusSaving(uint address) => SequentialSavings[address >> 24];

    private static uint Read32(uint address, bool isCPU)
    {
        address &= ~3u;
        ref byte memory = ref Locate(address, isWrite: false, isCPU, size: 4, out long cycles);
        Scheduler.Cycles += ReadCycles(address, isCPU, cycles);
        if (Unsafe.IsNullRef(ref memory))
        {
            return GBASlot.IsROM(address) ? GBASlot.Read32(Processor.ARM9, address) : IO.Read32(address);
        }

        return Unsafe.ReadUnaligned<uint>(ref memory);
    }

    private static ushort Read16(uint address, bool isCPU)
    {
        address &= ~1u;
        ref byte memory = ref Locate(address, isWrite: false, isCPU, size: 2, out long cycles);
        Scheduler.Cycles += ReadCycles(address, isCPU, cycles);
        if (Unsafe.IsNullRef(ref memory))
        {
            return GBASlot.IsROM(address) ? GBASlot.Read16(Processor.ARM9, address) : IO.Read16(address);
        }

        return Unsafe.ReadUnaligned<ushort>(ref memory);
    }

    private static void Write32(uint address, uint value, bool isCPU)
    {
        address &= ~3u;
        ref byte memory = ref Locate(address, isWrite: true, isCPU, size: 4, out long cycles);
        Scheduler.Cycles += WriteCycles(address, isCPU, cycles);
        if (Unsafe.IsNullRef(ref memory))
        {
            IO.Write32(address, value);
        }
        else
        {
            Unsafe.WriteUnaligned(ref memory, value);
        }
    }

    private static void Write16(uint address, ushort value, bool isCPU)
    {
        address &= ~1u;
        ref byte memory = ref Locate(address, isWrite: true, isCPU, size: 2, out long cycles);
        Scheduler.Cycles += WriteCycles(address, isCPU, cycles);
        if (Unsafe.IsNullRef(ref memory))
        {
            IO.Write16(address, value);
        }
        else
        {
            Unsafe.WriteUnaligned(ref memory, value);
        }
    }

    private static long BusFetchCycles(uint address, long[] regionCycles)
    {
        if (address < CP15.ITCMWriteEnd)
        {
            return TCMCycles;
        }

        long cycles = regionCycles[address >> 24];
        return cycles != 0 ? cycles : throw new NotSupportedException($"The ARM9 jumped to 0x{address:X8}, which has no code.");
    }

    private static long ReadCycles(uint address, bool isCPU, long busCycles)
    {
        if (!isCPU || IsInITCM(address, isWrite: false) || IsInDTCM(address, isWrite: false) || !CP15.IsDataCachable(address))
        {
            return busCycles;
        }

        return CP15.DataCache.Load(address, CP15.IsRoundRobin) ? CacheHitCycles : LineFillCycles(address);
    }

    private static long WriteCycles(uint address, bool isCPU, long busCycles)
    {
        if (!isCPU || IsInITCM(address, isWrite: true) || IsInDTCM(address, isWrite: true) || !CP15.IsWriteBack(address))
        {
            return busCycles;
        }

        return CP15.DataCache.Contains(address) ? CacheHitCycles : busCycles;
    }

    private static long LineFillCycles(uint address) => (address >> 24) switch
    {
        0x02 => MainMemoryLineFillCycles,
        0xFF => BIOSLineFillCycles,
        _ => throw new NotSupportedException($"The ARM9 cached 0x{address:X8}, which isn't supported."),
    };

    private static Span<byte> RAM(uint address, int size, bool isWrite)
    {
        var span = address switch
        {
            _ when IsInITCM(address, isWrite) => CP15.ITCM.AsSpan((int)(address & (CP15.ITCMSize - 1))),
            _ when IsInDTCM(address, isWrite) => CP15.DTCM.AsSpan((int)((address - CP15.DTCMBase) & (CP15.DTCMSize - 1))),
            _ when address >> 24 == 0x02 => MainMemory.Data.AsSpan((int)(address & MainMemory.Mask)),
            _ => [],
        };

        return span.Length >= size ? span[..size] : throw new ArgumentOutOfRangeException(nameof(address), $"0x{address:X8} is not in RAM.");
    }

    private static bool IsInITCM(uint address, bool isWrite) => address < (isWrite ? CP15.ITCMWriteEnd : CP15.ITCMReadEnd);

    private static bool IsInDTCM(uint address, bool isWrite) => address - CP15.DTCMBase < (isWrite ? CP15.DTCMWriteSize : CP15.DTCMReadSize);

    private static ref byte Locate(uint address, bool isWrite, bool isCPU, int size, out long cycles)
    {
        if (isCPU && IsInITCM(address, isWrite))
        {
            cycles = TCMCycles;
            return ref CP15.ITCM[address & (CP15.ITCMSize - 1)];
        }

        if (isCPU && IsInDTCM(address, isWrite))
        {
            cycles = TCMCycles;
            return ref CP15.DTCM[(address - CP15.DTCMBase) & (CP15.DTCMSize - 1)];
        }

        cycles = GBASlot.IsROM(address) ? GBASlot.Cycles(Processor.ARM9, size) : (size == 4 ? WordCycles : HalfwordCycles)[address >> 24];
        switch (address >> 24)
        {
            case 0x02:
                return ref MainMemory.Data[address & MainMemory.Mask];
            case 0x03:
                if (SharedWRAM.TryMapARM9(address, out int offset))
                {
                    return ref SharedWRAM.Data[offset];
                }

                throw new NotSupportedException($"The ARM9 accessed shared WRAM at 0x{address:X8} while none of it was given to the ARM9.");
            case 0x04:
                return ref Unsafe.NullRef<byte>();
            case 0x05 when !IO.IsEnginePowered((address & EngineBOffset) != 0):
                Array.Clear(PoweredOffPalette);
                return ref PoweredOffPalette[0];
            case 0x05:
                return ref VideoMemory(Palettes, VideoFrame.PalettesKilobyte, (int)(address & (PaletteSize - 1)), isWrite);
            case 0x06:
                return ref VideoMemory(VRAM.Data, 0, VRAM.LocateARM9(address), isWrite);
            case 0x07 when !IO.IsEnginePowered((address & EngineBOffset) != 0):
                throw new NotSupportedException($"The ARM9 accessed OAM at 0x{address:X8} with its 2D engine off.");
            case 0x07:
                return ref VideoMemory(OAM, VideoFrame.OAMKilobyte, (int)(address & (OAMSize - 1)), isWrite);
            case 0x08 or 0x09 when !isWrite:
                return ref Unsafe.NullRef<byte>();
            case 0xFF when !isWrite && address - BIOSLogoStart < BIOSLogoSize:
                return ref BIOSLogo[address - BIOSLogoStart];
            default:
                throw new NotSupportedException($"The ARM9 accessed 0x{address:X8}, which isn't supported.");
        }
    }

    private static ref byte VideoMemory(byte[] memory, int firstKilobyte, int offset, bool isWrite)
    {
        if (isWrite)
        {
            Video.BeforeVideoMemoryWrite(firstKilobyte + (offset / VideoFrame.KilobyteSize));
        }

        return ref memory[offset];
    }

    private static long[] CreateCycles(long mainMemory, long fast, long video)
    {
        long[] cycles = new long[256];
        cycles[0x02] = mainMemory;
        cycles[0x03] = cycles[0x04] = cycles[0x07] = cycles[0xFF] = fast;
        cycles[0x05] = cycles[0x06] = video;
        return cycles;
    }
}
