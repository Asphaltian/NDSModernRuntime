namespace NTRModern;

internal static class Hardware
{
    public static Model Model { get; private set; }

    public static Interrupts InterruptsOf(Processor processor) => processor == Processor.ARM9 ? ARM9.IO.Interrupts : ARM7.IO.Interrupts;

    public static Timers TimersOf(Processor processor) => processor == Processor.ARM9 ? ARM9.IO.Timers : ARM7.IO.Timers;

    public static DMA DMAOf(Processor processor) => processor == Processor.ARM9 ? ARM9.IO.DMA : ARM7.IO.DMA;

    public static void Reset(Model model)
    {
        Model = model;
        SystemClock.Reset();
        IPC.Reset();
        ARM9.IO.Reset();
        ARM7.IO.Reset();
        ARM9.CP15.Reset();
        ARM9.Maths.Reset();
        ARM9.Memory.Reset();
        ARM7.Memory.Reset();
        VRAM.Reset();
        Video.Reset();
        Engine2D.Reset();
        RenderingEngine.Reset();
        GeometryEngine.Reset();
        Sound.Reset();
        SPI.Reset();
        GameCard.Reset();
        RTC.Reset();
        Wifi.Reset();
        Keypad.Reset();
        SharedWRAM.Reset();
        Array.Clear(MainMemory.Data);
    }
}
