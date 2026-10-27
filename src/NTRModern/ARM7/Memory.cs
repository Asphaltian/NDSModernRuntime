using System.Runtime.CompilerServices;

namespace NTRModern.ARM7;

/// <summary>
/// <c>Memory</c> is how you read and write memory the way the ARM7 sees it. Keep in mind that Read and Write cost
/// cycles just like the game's own reads and writes do.
/// </summary>
public static class Memory
{
    private const int WRAMSize = 0x10000;
    private const uint WRAMStart = 0x03800000;
    private const uint BIOSSize = 0x4000;

    internal static readonly byte[] WRAM = new byte[WRAMSize];

    private static readonly byte[] ProtectedBIOS = new byte[4];

    private static readonly long[] HalfwordCycles = CreateCycles(mainMemory: 18, fast: 2);
    private static readonly long[] WordCycles = CreateCycles(mainMemory: 20, fast: 2);
    private static readonly long[] SequentialSavings = CreateCycles(mainMemory: 16, fast: 0);
    private static readonly long[] ARMJumpCycles = CreateCycles(mainMemory: 18 + 4, fast: 2 + 2);
    private static readonly long[] ThumbJumpCycles = CreateCycles(mainMemory: 16 + 2, fast: 2 + 2);

    /// <summary>Reads memory the same way the game does, so it costs cycles just like the game's own reads.</summary>
    public static uint Read32(uint address)
    {
        address &= ~3u;
        ref byte memory = ref Locate(address, isWrite: false, size: 4, out long cycles);
        Scheduler.Cycles += cycles;
        if (Unsafe.IsNullRef(ref memory))
        {
            return GBASlot.IsROM(address) ? GBASlot.Read32(Processor.ARM7, address) : Wifi.Contains(address) ? Wifi.Read32(address) : IO.Read32(address);
        }

        return Unsafe.ReadUnaligned<uint>(ref memory);
    }

    /// <inheritdoc cref="Read32"/>
    public static ushort Read16(uint address)
    {
        address &= ~1u;
        ref byte memory = ref Locate(address, isWrite: false, size: 2, out long cycles);
        Scheduler.Cycles += cycles;
        if (Unsafe.IsNullRef(ref memory))
        {
            return GBASlot.IsROM(address) ? GBASlot.Read16(Processor.ARM7, address) : Wifi.Contains(address) ? Wifi.Read16(address) : IO.Read16(address);
        }

        return Unsafe.ReadUnaligned<ushort>(ref memory);
    }

    /// <inheritdoc cref="Read32"/>
    public static byte Read8(uint address)
    {
        ref byte memory = ref Locate(address, isWrite: false, size: 1, out long cycles);
        Scheduler.Cycles += cycles;
        if (Unsafe.IsNullRef(ref memory))
        {
            return GBASlot.IsROM(address) ? GBASlot.Read8(Processor.ARM7, address) : Wifi.Contains(address) ? Wifi.Read8(address) : IO.Read8(address);
        }

        return memory;
    }

    /// <summary>Writes memory the same way the game does, so it costs cycles just like the game's own writes.</summary>
    public static void Write32(uint address, uint value)
    {
        address &= ~3u;
        ref byte memory = ref Locate(address, isWrite: true, size: 4, out long cycles);
        Scheduler.Cycles += cycles;
        if (Unsafe.IsNullRef(ref memory) && Wifi.Contains(address))
        {
            Wifi.Write32(address, value);
        }
        else if (Unsafe.IsNullRef(ref memory))
        {
            IO.Write32(address, value);
        }
        else
        {
            Unsafe.WriteUnaligned(ref memory, value);
        }
    }

    /// <inheritdoc cref="Write32"/>
    public static void Write16(uint address, ushort value)
    {
        address &= ~1u;
        ref byte memory = ref Locate(address, isWrite: true, size: 2, out long cycles);
        Scheduler.Cycles += cycles;
        if (Unsafe.IsNullRef(ref memory) && Wifi.Contains(address))
        {
            Wifi.Write16(address, value);
        }
        else if (Unsafe.IsNullRef(ref memory))
        {
            IO.Write16(address, value);
        }
        else
        {
            Unsafe.WriteUnaligned(ref memory, value);
        }
    }

    /// <inheritdoc cref="Write32"/>
    public static void Write8(uint address, byte value)
    {
        ref byte memory = ref Locate(address, isWrite: true, size: 1, out long cycles);
        Scheduler.Cycles += cycles;
        if (Unsafe.IsNullRef(ref memory) && !Wifi.Contains(address))
        {
            IO.Write8(address, value);
        }
        else if (!Unsafe.IsNullRef(ref memory))
        {
            memory = value;
        }
    }

    internal static uint Peek32(uint address)
    {
        ref byte memory = ref Locate(address & ~3u, isWrite: false, size: 4, out _);
        return Unsafe.IsNullRef(ref memory) ? throw new InvalidOperationException($"0x{address:X8} is I/O or the GBA slot, not memory.") : Unsafe.ReadUnaligned<uint>(ref memory);
    }

    internal static void Reset() => Array.Clear(WRAM);

    internal static long JumpCycles(uint target, bool isThumb)
    {
        long cycles = (isThumb ? ThumbJumpCycles : ARMJumpCycles)[target >> 24];
        return cycles != 0 ? cycles : throw new NotSupportedException($"The ARM7 jumped to 0x{target:X8}, which has no code.");
    }

    internal static long SequentialSaving(uint address) => SequentialSavings[address >> 24];

    private static ref byte Locate(uint address, bool isWrite, int size, out long cycles)
    {
        cycles = GBASlot.IsROM(address) ? GBASlot.Cycles(Processor.ARM7, size)
            : Wifi.Contains(address) ? Wifi.Cycles(address, size)
            : (size == 4 ? WordCycles : HalfwordCycles)[address >> 24];
        switch (address >> 24)
        {
            case 0x00 when address < BIOSSize:
                Array.Fill(ProtectedBIOS, byte.MaxValue);
                return ref ProtectedBIOS[0];
            case 0x02:
                return ref MainMemory.Data[address & MainMemory.Mask];
            case 0x03 when address < WRAMStart && SharedWRAM.TryMapARM7(address, out int offset):
                return ref SharedWRAM.Data[offset];
            case 0x03:
                return ref WRAM[address & (WRAMSize - 1)];
            case 0x04 when Wifi.Contains(address) && !IO.IsWifiPowered:
                throw new NotSupportedException($"The ARM7 accessed wifi at 0x{address:X8} with wifi power off.");
            case 0x04 when address < Wifi.RegionEnd:
            case 0x08 or 0x09 when !isWrite:
                return ref Unsafe.NullRef<byte>();
            default:
                throw new NotSupportedException($"The ARM7 accessed 0x{address:X8}, which isn't supported.");
        }
    }

    private static long[] CreateCycles(long mainMemory, long fast)
    {
        long[] cycles = new long[256];
        cycles[0x02] = mainMemory;
        cycles[0x00] = cycles[0x03] = cycles[0x04] = fast;
        return cycles;
    }
}
