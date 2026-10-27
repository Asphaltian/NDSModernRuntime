using NTRModern;

namespace LibRecomp;

internal sealed class CPU
{
    public static readonly CPU ARM9 = new(Processor.ARM9);
    public static readonly CPU ARM7 = new(Processor.ARM7);

    private CPU(Processor processor)
    {
        Processor = processor;
        Threads = new Threads(this);
    }

    public Processor Processor { get; }

    public Functions Functions { get; } = new();

    public Threads Threads { get; }

    public uint Read32(uint address) => Processor == Processor.ARM9 ? NTRModern.ARM9.Memory.Read32(address) : NTRModern.ARM7.Memory.Read32(address);

    public ushort Read16(uint address) => Processor == Processor.ARM9 ? NTRModern.ARM9.Memory.Read16(address) : NTRModern.ARM7.Memory.Read16(address);

    public byte Read8(uint address) => Processor == Processor.ARM9 ? NTRModern.ARM9.Memory.Read8(address) : NTRModern.ARM7.Memory.Read8(address);

    public uint Peek32(uint address) => Processor == Processor.ARM9 ? NTRModern.ARM9.Memory.Peek32(address) : NTRModern.ARM7.Memory.Peek32(address);

    public void Write32(uint address, uint value)
    {
        if (Processor == Processor.ARM9)
        {
            NTRModern.ARM9.Memory.Write32(address, value);
        }
        else
        {
            NTRModern.ARM7.Memory.Write32(address, value);
        }
    }

    public void Write16(uint address, ushort value)
    {
        if (Processor == Processor.ARM9)
        {
            NTRModern.ARM9.Memory.Write16(address, value);
        }
        else
        {
            NTRModern.ARM7.Memory.Write16(address, value);
        }
    }

    public void Write8(uint address, byte value)
    {
        if (Processor == Processor.ARM9)
        {
            NTRModern.ARM9.Memory.Write8(address, value);
        }
        else
        {
            NTRModern.ARM7.Memory.Write8(address, value);
        }
    }

    public void HandleEvents(RecompContext ctx, uint? address)
    {
        SystemClock.Synchronize(Processor);
        while (ctx.AreIRQsEnabled && Hardware.InterruptsOf(Processor).IsPending)
        {
            BIOS.EnterIRQ(this, ctx, address);
        }
    }
}
