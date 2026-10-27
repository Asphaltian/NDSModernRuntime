[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NTRModern.Tests;

public class MemoryTests
{
    private const uint WRAMCNT = 0x04000247;

    public MemoryTests() => Hardware.Reset(Model.DS);

    [Fact]
    public void PeekAndPokeReachRAMWithoutCostingCycles()
    {
        ARM9.Memory.Write32(0x02000040, 0x12345678);
        long before = ARM9.Scheduler.Cycles;

        Assert.Equal(0x12345678u, ARM9.Memory.Peek<uint>(0x02000040));
        ARM9.Memory.Poke<ushort>(0x02000042) = 0xABCD;

        Assert.Equal(before, ARM9.Scheduler.Cycles);
        Assert.Equal(0xABCD5678u, ARM9.Memory.Read32(0x02000040));
        Assert.Throws<ArgumentOutOfRangeException>(() => ARM9.Memory.Peek<int>(0x04000000));
    }

    [Fact]
    public void MainMemoryIsMirroredThroughItsRegionAndSharedByBothCPUs()
    {
        ARM9.Memory.Write32(0x02000010, 0x11223344);

        Assert.Equal(0x11223344u, ARM9.Memory.Read32(0x02400010));
        Assert.Equal(0x11223344u, ARM9.Memory.Read32(0x02C00010));
        Assert.Equal(0x11223344u, ARM7.Memory.Read32(0x02000010));
    }

    [Fact]
    public void ValuesAreLittleEndianAndAccessesAreAligned()
    {
        ARM9.Memory.Write32(0x02000020, 0xAABBCCDD);

        Assert.Equal(0xDD, ARM9.Memory.Read8(0x02000020));
        Assert.Equal(0xAA, ARM9.Memory.Read8(0x02000023));
        Assert.Equal(0xAABB, ARM9.Memory.Read16(0x02000023));
        Assert.Equal(0xAABBCCDDu, ARM9.Memory.Read32(0x02000022));
    }

    [Fact]
    public void AllSharedWRAMGoesToTheARM7AtBoot()
    {
        ARM7.Memory.Write32(0x03000000, 0x12345678);

        Assert.Equal(0x12345678u, ARM7.Memory.Read32(0x03008000));
        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x03000000));
    }

    [Fact]
    public void WithAllSharedWRAMOnTheARM9TheARM7SeesMirrorsOfItsOwnWRAM()
    {
        ARM9.IO.Write8(WRAMCNT, 0);
        ARM9.Memory.Write32(0x03000000, 0x12345678);
        ARM7.Memory.Write32(0x03800000, 0x9ABCDEF0);

        Assert.Equal(0x12345678u, ARM9.Memory.Read32(0x03008000));
        Assert.Equal(0x9ABCDEF0u, ARM7.Memory.Read32(0x03000000));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SplittingSharedWRAMGivesEachCPUItsOwnMirroredHalf(byte wramcnt)
    {
        ARM9.IO.Write8(WRAMCNT, wramcnt);
        ARM9.Memory.Write32(0x03000000, 0x11111111);
        ARM7.Memory.Write32(0x03000000, 0x22222222);

        Assert.Equal(0x11111111u, ARM9.Memory.Read32(0x03004000));
        Assert.Equal(0x22222222u, ARM7.Memory.Read32(0x03004000));
        Assert.Equal(0x11, SharedWRAM.Data[wramcnt == 1 ? 0x4000 : 0]);
    }

    [Fact]
    public void ARM7WRAMIsMirroredThroughItsRegion()
    {
        ARM7.Memory.Write32(0x0380FFFC, 0x55667788);

        Assert.Equal(0x55667788u, ARM7.Memory.Read32(0x0381FFFC));
        Assert.Equal(0x55667788u, ARM7.Memory.Read32(0x03FFFFFC));
    }

    [Fact]
    public void TheARM7BIOSReadsAsFFAndIgnoresWrites()
    {
        ARM7.Memory.Write32(0x00000000, 0x12345678);

        Assert.Equal(0xFFFFFFFFu, ARM7.Memory.Read32(0x00000000));
        Assert.Equal(0xFF, ARM7.Memory.Read8(0x00003FFF));
    }

    [Fact]
    public void ARM7DMACanCopyFromTheBIOS()
    {
        ARM7.IO.Write32(0x040000B0, 0);
        ARM7.IO.Write32(0x040000B4, 0x02000000);
        ARM7.IO.Write32(0x040000B8, 0x80000001);

        Assert.Equal(0xFFFF, ARM7.Memory.Read16(0x02000000));
    }

    [Theory]
    [InlineData(0x05000000u)]
    [InlineData(0x07000000u)]
    public void TheARM9IgnoresByteWritesToPalettesAndOAM(uint address)
    {
        ARM9.Memory.Write16(address, 0x1234);
        ARM9.Memory.Write8(address, 0xFF);

        Assert.Equal(0x1234, ARM9.Memory.Read16(address));
    }

    [Theory]
    [InlineData(0x02000000u, 18, 20)]
    [InlineData(0x03800000u, 2, 2)]
    [InlineData(0x04000208u, 2, 2)]
    [InlineData(0x00000000u, 2, 2)]
    public void ARM7DataAccessesCostTheirNonsequentialTime(uint address, long halfword, long word)
    {
        long start = ARM7.Scheduler.Cycles;
        ARM7.Memory.Read16(address);
        Assert.Equal(halfword, ARM7.Scheduler.Cycles - start);

        start = ARM7.Scheduler.Cycles;
        ARM7.Memory.Read32(address);
        Assert.Equal(word, ARM7.Scheduler.Cycles - start);
    }

    [Theory]
    [InlineData(0x02000000u, 18, 20)]
    [InlineData(0x04000208u, 8, 8)]
    [InlineData(0x05000000u, 8, 10)]
    [InlineData(0x07000000u, 8, 8)]
    public void ARM9DataAccessesCostTheirNonsequentialTime(uint address, long halfword, long word)
    {
        long start = ARM9.Scheduler.Cycles;
        ARM9.Memory.Read16(address);
        Assert.Equal(halfword, ARM9.Scheduler.Cycles - start);

        start = ARM9.Scheduler.Cycles;
        ARM9.Memory.Read32(address);
        Assert.Equal(word, ARM9.Scheduler.Cycles - start);
    }

    [Theory]
    [InlineData(0x02000000u, false, 18 + 18)]
    [InlineData(0x02000000u, true, 9 + 9)]
    [InlineData(0x02000002u, true, 18 + 9)]
    [InlineData(0xFFFF0018u, false, 8 + 8)]
    public void ARM9JumpsRefetchTwoOpcodesAndAWholeWordForAMisalignedThumbTarget(uint target, bool isThumb, long cycles)
    {
        Assert.Equal(cycles, ARM9.Memory.JumpCycles(target, isThumb));
    }

    [Theory]
    [InlineData(0x02380000u, false, 18 + 4)]
    [InlineData(0x02380000u, true, 16 + 2)]
    [InlineData(0x00000018u, false, 2 + 2)]
    public void ARM7JumpsRefetchANonsequentialAndASequentialOpcode(uint target, bool isThumb, long cycles)
    {
        Assert.Equal(cycles, ARM7.Memory.JumpCycles(target, isThumb));
    }

    [Fact]
    public void TCMHasNoSequentialSaving()
    {
        ARM9.CP15.Write(0, 9, 1, 0, 0x027E000A);
        ARM9.CP15.Write(0, 1, 0, 0, 1 << 16);

        Assert.Equal(0, ARM9.Memory.SequentialSaving(0x027E0000, isWrite: false));
        Assert.Equal(16, ARM9.Memory.SequentialSaving(0x027E4000, isWrite: false));
    }

    [Theory]
    [InlineData(0, 0xFE48)]
    [InlineData(1, 0x0040)]
    [InlineData(2, 0x0040)]
    [InlineData(3, 0xFFFF)]
    public void AnEmptyGBASlotReadsFromItsFirstAccessTime(int accessTime, int value)
    {
        ARM9.IO.Write16(0x04000204, (ushort)(accessTime << 2));

        Assert.Equal(value, ARM9.Memory.Read16(0x08000080));
        Assert.Equal(0, ARM7.Memory.Read16(0x08000080));
    }

    [Fact]
    public void TheDSiReadsFFFFFromTheGBASlotItDoesntHave()
    {
        Hardware.Reset(Model.DSi);

        Assert.Equal(0xFFFF, ARM9.Memory.Read16(0x08000080));
    }

    [Fact]
    public void TheCPUThatOwnsTheGBASlotReadsItAndTheOtherReadsZero()
    {
        ARM9.IO.Write16(0x04000204, 1 << 7);
        ARM7.IO.Write16(0x04000204, 2 << 2);

        Assert.Equal(0x00410040u, ARM7.Memory.Read32(0x08000080));
        Assert.Equal(0u, ARM9.Memory.Read32(0x08000080));
    }

    [Theory]
    [InlineData(Processor.ARM9, 2, (18 + 3) * 2)]
    [InlineData(Processor.ARM9, 4, (18 + 3 + 6) * 2)]
    [InlineData(Processor.ARM7, 2, (18 - 1) * 2)]
    public void GBASlotAccessesTakeTheirAccessTimes(Processor processor, int size, long cycles)
    {
        ARM9.IO.Write16(0x04000204, 3 << 2);
        ARM7.IO.Write16(0x04000204, 3 << 2);
        ref long now = ref SystemClock.CyclesOf(processor);

        long start = now;
        _ = (processor, size) switch
        {
            (Processor.ARM9, 2) => ARM9.Memory.Read16(0x08000000),
            (Processor.ARM9, _) => ARM9.Memory.Read32(0x08000000),
            _ => ARM7.Memory.Read16(0x08000000),
        };

        Assert.Equal(cycles, now - start);
    }

    [Fact]
    public void OnlyTheLogoInTheARM9BIOSCanBeRead()
    {
        ARM9.Memory.BIOSLogo[0] = 0x24;
        ARM9.Memory.BIOSLogo[^1] = 0x42;

        Assert.Equal((0x24, 0x42), (ARM9.Memory.Read8(0xFFFF0020), ARM9.Memory.Read8(0xFFFF00BB)));
        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read8(0xFFFF00BC));
        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read8(0xFFFF001F));
    }

    [Fact]
    public void TheARM7ReadsPOSTFLGAndHALTCNTTogether()
    {
        ARM7.IO.Write8(0x04000301, 0x3F);

        Assert.Equal(1, ARM7.IO.Read16(0x04000300));
    }

    [Fact]
    public void TheARM9SeesVRAMBanksMappedToTheLCDC()
    {
        ARM9.IO.Write8(0x04000240, 0x80);
        ARM9.Memory.Write32(0x06800000, 0xCAFEF00D);

        Assert.Equal(0xCAFEF00Du, ARM9.Memory.Read32(0x06800000));
        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x06820000));
    }

    [Fact]
    public void VRAMSTATShowsBanksCAndDGivenToTheARM7()
    {
        ARM9.IO.Write16(0x04000242, 0x8282);

        Assert.Equal(3, ARM7.IO.Read8(0x04000240));
    }
}
