namespace NTRModern;

internal static class GBASlot
{
    private const uint ARM7Access = 1 << 7;

    public static bool IsROM(uint address) => address >> 24 is 0x08 or 0x09;

    public static uint Read32(Processor processor, uint address) => Read16(processor, address) | ((uint)Read16(processor, address + 2) << 16);

    public static ushort Read16(Processor processor, uint address)
    {
        if (((ARM9.IO.ExternalMemoryControl & ARM7Access) != 0) != (processor == Processor.ARM7))
        {
            return 0;
        }

        if (Hardware.Model == Model.DSi)
        {
            return 0xFFFF;
        }

        return FirstAccessTime(processor) switch
        {
            6 or 8 => (ushort)(address >> 1),
            10 => (ushort)((address >> 1) | 0xFE08),
            _ => 0xFFFF,
        };
    }

    public static byte Read8(Processor processor, uint address) => (byte)(Read16(processor, address & ~1u) >> (int)((address & 1) * 8));

    public static long Cycles(Processor processor, int size)
    {
        long nonsequential = processor == Processor.ARM9 ? FirstAccessTime(processor) + 3 : FirstAccessTime(processor) - 1;
        return (size == 4 ? nonsequential + SecondAccessTime(processor) : nonsequential) * SystemClock.BusCycle;
    }

    private static uint Control(Processor processor) => processor == Processor.ARM9 ? ARM9.IO.ExternalMemoryControl : ARM7.IO.ExternalMemoryControl;

    private static int FirstAccessTime(Processor processor) => ((Control(processor) >> 2) & 3) switch
    {
        0 => 10,
        1 => 8,
        2 => 6,
        _ => 18,
    };

    private static int SecondAccessTime(Processor processor) => (Control(processor) & (1 << 4)) == 0 ? 6 : 4;
}
