namespace NTRModern;

internal static class IORegisters
{
    private const uint DISPSTAT = 0x004;
    private const uint Timers0 = 0x100;
    private const uint TimersEnd = Timers0 + 0x10;
    private const uint KEYINPUT = 0x130;
    private const uint IPCSYNC = 0x180;
    private const uint IPCFIFOCNT = 0x184;
    private const uint IPCFIFOSEND = 0x188;
    private const uint IME = 0x208;
    private const uint IE = 0x210;
    private const uint IF = 0x214;
    private const uint IPCFIFORECV = 0x100000;

    public static (uint Word, int Shift, uint Mask) Locate(uint address, int size)
    {
        uint offset = address & 0x00FFFFFF;
        int shift = (int)(offset & 3) * 8;
        uint mask = (size == 4 ? uint.MaxValue : (1u << (size * 8)) - 1) << shift;
        return (offset & ~3u, shift, mask);
    }

    public static uint? Read(Processor processor, uint word, int size, uint mask)
    {
        var interrupts = Hardware.InterruptsOf(processor);
        var timers = Hardware.TimersOf(processor);
        return word switch
        {
            DISPSTAT => Video.ReadDISPSTAT(processor) | ((uint)Video.VCOUNT << 16),
            >= DMA.FirstRegister and <= DMA.LastRegister => Hardware.DMAOf(processor).Read(word),
            >= Timers0 and < TimersEnd => timers.ReadCounter(TimerOf(word)) | ((uint)timers.ReadControl(TimerOf(word)) << 16),
            KEYINPUT => Keypad.KEYINPUT | ((uint)Keypad.ReadKEYCNT(processor) << 16),
            IPCSYNC => IPC.ReadIPCSYNC(processor),
            IPCFIFOCNT => IPC.ReadIPCFIFOCNT(processor),
            IME => interrupts.IME,
            IE => interrupts.IE,
            IF => interrupts.IF,
            IPCFIFORECV when size == 4 => IPC.Receive(processor),
            _ => GameCard.Read(processor, word, mask),
        };
    }

    public static bool Write(Processor processor, uint word, int size, uint value, uint mask)
    {
        var interrupts = Hardware.InterruptsOf(processor);
        var timers = Hardware.TimersOf(processor);
        switch (word)
        {
            case DISPSTAT when (mask & 0xFFFF0000) == 0:
                Video.WriteDISPSTAT(processor, (ushort)Merge(Video.ReadDISPSTAT(processor), value, mask));
                return true;
            case >= DMA.FirstRegister and <= DMA.LastRegister:
                var dma = Hardware.DMAOf(processor);
                dma.Write(word, Merge(dma.Read(word), value, mask));
                return true;
            case >= Timers0 and < TimersEnd:
                int timer = TimerOf(word);
                if ((mask & 0xFFFF) != 0)
                {
                    timers.WriteReload(timer, (ushort)Merge(timers.ReadReload(timer), value, mask));
                }

                if ((mask & 0xFFFF0000) != 0)
                {
                    timers.WriteControl(timer, (ushort)(Merge((uint)timers.ReadControl(timer) << 16, value, mask) >> 16));
                }

                return true;
            case KEYINPUT when (mask & 0xFFFF) == 0:
                Keypad.WriteKEYCNT(processor, (ushort)(Merge((uint)Keypad.ReadKEYCNT(processor) << 16, value, mask) >> 16));
                return true;
            case IPCSYNC:
                IPC.WriteIPCSYNC(processor, Merge(IPC.ReadIPCSYNC(processor), value, mask));
                return true;
            case IPCFIFOCNT:
                IPC.WriteIPCFIFOCNT(processor, Merge(IPC.ReadIPCFIFOCNT(processor) & IPC.WritableControl, value, mask));
                return true;
            case IPCFIFOSEND when size == 4:
                IPC.Send(processor, value);
                return true;
            case IME:
                interrupts.WriteIME(Merge(interrupts.IME, value, mask));
                return true;
            case IE:
                interrupts.WriteIE(Merge(interrupts.IE, value, mask));
                return true;
            case IF:
                interrupts.Acknowledge(value & mask);
                if (processor == Processor.ARM9)
                {
                    GeometryEngine.UpdateInterrupt();
                }

                return true;
            default:
                return GameCard.Write(processor, word, value, mask);
        }
    }

    public static uint Merge(uint current, uint value, uint mask) => (current & ~mask) | (value & mask);

    public static NotSupportedException Unsupported(Processor processor, string access, uint address, int size)
    {
        return new NotSupportedException($"The {processor} tried to {access} {size * 8} bits of the I/O register at 0x{address:X8}, which isn't supported.");
    }

    private static int TimerOf(uint word) => (int)(word - Timers0) / 4;
}
