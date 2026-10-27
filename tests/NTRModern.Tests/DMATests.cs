namespace NTRModern.Tests;

public class DMATests
{
    private const uint DMA0SAD = 0x040000B0;
    private const uint DMA3SAD = 0x040000D4;
    private const uint DMA0FILL = 0x040000E0;
    private const uint IF = 0x04000214;
    private const uint Source = 0x02100000;
    private const uint Destination = 0x02200000;
    private const uint WRAM = 0x03800000;
    private const long CyclesPerLine = 355 * 6 * SystemClock.BusCycle;

    private const uint DestinationDecrement = 1u << 21;
    private const uint DestinationReload = 3u << 21;
    private const uint SourceFixed = 2u << 23;
    private const uint Repeat = 1u << 25;
    private const uint Words = 1u << 26;
    private const uint ARM9HBlank = 2u << 27;
    private const uint ARM7VBlank = 1u << 28;
    private const uint IRQEnable = 1u << 30;
    private const uint Enable = 1u << 31;

    public DMATests() => Hardware.Reset(Model.DS);

    private static void Start(Processor processor, uint channel, uint source, uint destination, uint control)
    {
        uint registers = DMA0SAD + (channel * 12);
        if (processor == Processor.ARM9)
        {
            ARM9.IO.Write32(registers, source);
            ARM9.IO.Write32(registers + 4, destination);
            ARM9.IO.Write32(registers + 8, control);
        }
        else
        {
            ARM7.IO.Write32(registers, source);
            ARM7.IO.Write32(registers + 4, destination);
            ARM7.IO.Write32(registers + 8, control);
        }
    }

    [Fact]
    public void AnImmediateTransferCopiesItsCountAndStops()
    {
        for (uint i = 0; i < 4; i++)
        {
            ARM9.Memory.Write32(Source + (i * 4), 0x11111111 * (i + 1));
        }

        Start(Processor.ARM9, 1, Source, Destination, Enable | IRQEnable | Words | 3);

        Assert.Equal([0x11111111u, 0x22222222u, 0x33333333u, 0u], Enumerable.Range(0, 4).Select(i => ARM9.Memory.Read32(Destination + ((uint)i * 4))));
        Assert.Equal(0u, ARM9.IO.Read32(DMA0SAD + 12 + 8) & Enable);
        Assert.Equal((uint)Interrupt.DMA1, ARM9.IO.Read32(IF));
    }

    [Fact]
    public void TheRegistersReadBackWithTheirAddressRanges()
    {
        ARM9.IO.Write32(DMA0SAD, 0xFFFFFFFF);
        ARM7.IO.Write32(DMA0SAD, 0xFFFFFFFF);
        ARM7.IO.Write32(DMA3SAD, 0xFFFFFFFF);

        Assert.Equal(0x0FFFFFFEu, ARM9.IO.Read32(DMA0SAD));
        Assert.Equal(0x07FFFFFFu, ARM7.IO.Read32(DMA0SAD));
        Assert.Equal(0x0FFFFFFFu, ARM7.IO.Read32(DMA3SAD));
    }

    [Fact]
    public void AFixedSourceFillsFromTheFillRegister()
    {
        ARM9.IO.Write32(DMA0FILL + 8, 0xCAFEF00D);

        Start(Processor.ARM9, 2, DMA0FILL + 8, Destination, Enable | Words | SourceFixed | 3);

        Assert.Equal([0xCAFEF00Du, 0xCAFEF00Du, 0xCAFEF00Du], Enumerable.Range(0, 3).Select(i => ARM9.Memory.Read32(Destination + ((uint)i * 4))));
    }

    [Fact]
    public void ARM9DMAGoesPastTheDTCM()
    {
        ARM9.CP15.Write(0, 9, 1, 0, 0x027E000A);
        ARM9.CP15.Write(0, 1, 0, 0, 1 << 16);
        ARM9.Memory.Write32(Source, 0x12345678);

        Start(Processor.ARM9, 0, Source, 0x027E0000, Enable | Words | 1);

        Assert.Equal(0u, ARM9.Memory.Read32(0x027E0000));
        Assert.Equal(0x12345678u, ARM7.Memory.Read32(0x027E0000));
    }

    [Fact]
    public void ADecrementingDestinationWritesDownwards()
    {
        ARM7.Memory.Write16(Source, 0x1111);
        ARM7.Memory.Write16(Source + 2, 0x2222);

        Start(Processor.ARM7, 0, Source, Destination + 2, Enable | DestinationDecrement | 2);

        Assert.Equal((0x2222, 0x1111), (ARM7.Memory.Read16(Destination), ARM7.Memory.Read16(Destination + 2)));
    }

    [Fact]
    public void VBlankTransfersWaitForLine192()
    {
        Video.Start(paced: false);
        ARM7.Memory.Write32(Source, 0x12345678);
        Start(Processor.ARM7, 0, Source, Destination, Enable | ARM7VBlank | Words | 1);

        uint before = 0;
        uint after = 0;

        SystemClock.Run(
            () => { },
            () =>
            {
                Time.Spend(Processor.ARM7, (Video.VisibleLines * CyclesPerLine) - 1 - ARM7.Scheduler.Cycles);
                before = ARM7.Memory.Read32(Destination);
                Time.Spend(Processor.ARM7, CyclesPerLine);
                after = ARM7.Memory.Read32(Destination);
            });

        Assert.Equal((0u, 0x12345678u), (before, after));
    }

    [Fact]
    public void HBlankTransfersRepeatOnEveryVisibleLine()
    {
        Video.Start(paced: false);
        ARM9.Memory.Write32(Source, 1);
        Start(Processor.ARM9, 0, Source, Destination, Enable | ARM9HBlank | Repeat | SourceFixed | DestinationReload | Words | 1);
        ARM9.Memory.Write32(Source, 7);

        uint visible = 0;
        uint blanked = 0;
        uint control = 0;

        SystemClock.Run(
            () =>
            {
                Time.Spend(Processor.ARM9, 3 * CyclesPerLine);
                visible = ARM9.Memory.Read32(Destination);
                ARM9.Memory.Write32(Source, 9);
                Time.Spend(Processor.ARM9, ((Video.VisibleLines + 1) * CyclesPerLine) - ARM9.Scheduler.Cycles);
                ARM9.Memory.Write32(Source, 5);
                Time.Spend(Processor.ARM9, CyclesPerLine);
                blanked = ARM9.Memory.Read32(Destination);
                control = ARM9.IO.Read32(DMA0SAD + 8);
            },
            () => { });

        Assert.Equal((7u, 9u, Enable), (visible, blanked, control & Enable));
    }

    [Theory]
    [InlineData(Destination, 4 * (20 + 20))]
    [InlineData(WRAM, 20 + 2 + (3 * (4 + 2)))]
    public void EveryUnitAfterTheFirstIsSequentialUnlessBothSidesAreInMainMemory(uint destination, long cycles)
    {
        long start = ARM7.Scheduler.Cycles;

        Start(Processor.ARM7, 3, Source, destination, Enable | Words | 4);

        Assert.Equal(cycles, ARM7.Scheduler.Cycles - start);
    }

    [Fact]
    public void ARM9TransfersWithinTheBlocksOnlyTCMUsesDoNothing()
    {
        ARM9.CP15.Write(0, 9, 1, 1, 0x20);
        ARM9.CP15.Write(0, 1, 0, 0, 1 << 18);
        ARM9.Memory.Write16(0, 0x1234);

        Start(Processor.ARM9, 0, 0, 2, Enable | SourceFixed | (2u << 21) | 1);

        Assert.Equal((0x1234, 0), (ARM9.Memory.Read16(0), ARM9.Memory.Read16(2)));
        Assert.Throws<NotSupportedException>(() => Start(Processor.ARM9, 1, 0, Destination, Enable | 1));
    }

    [Fact]
    public void AStartModeThatIsntSupportedThrows()
    {
        Assert.Throws<NotSupportedException>(() => Start(Processor.ARM9, 0, Source, Destination, Enable | (3u << 27) | 1));
    }
}
