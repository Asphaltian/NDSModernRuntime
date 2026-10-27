using NTRModern.ARM9;

namespace NTRModern.Tests;

public class CP15Tests
{
    private const uint DTCMEnable = 1 << 16;
    private const uint DTCMLoadMode = 1 << 17;
    private const uint ITCMEnable = 1 << 18;
    private const uint ITCMLoadMode = 1 << 19;
    private const uint ProtectionUnitEnable = 1 << 0;
    private const uint DataCacheEnable = 1 << 2;
    private const uint InstructionCacheEnable = 1 << 12;
    private const uint RoundRobinReplacement = 1 << 14;
    private const uint DataSetStride = 32 * 32;

    public CP15Tests() => Hardware.Reset(Model.DS);

    private static void WriteControl(uint value) => CP15.Write(0, 1, 0, 0, value);

    private static void EnableCaches()
    {
        CP15.Write(0, 6, 1, 0, 0x0200002D);
        CP15.Write(0, 2, 0, 0, 0x02);
        CP15.Write(0, 2, 0, 1, 0x02);
        WriteControl(ProtectionUnitEnable | DataCacheEnable | InstructionCacheEnable | RoundRobinReplacement);
    }

    private static long ReadCycles(uint address)
    {
        long start = ARM9.Scheduler.Cycles;
        ARM9.Memory.Read32(address);
        return ARM9.Scheduler.Cycles - start;
    }

    private static long WriteCycles(uint address)
    {
        long start = ARM9.Scheduler.Cycles;
        ARM9.Memory.Write32(address, 0);
        return ARM9.Scheduler.Cycles - start;
    }

    private static void PlaceDTCM(uint region) => CP15.Write(0, 9, 1, 0, region);

    private static void PlaceITCM(uint region) => CP15.Write(0, 9, 1, 1, region);

    [Theory]
    [InlineData(0, 0x41059461u)]
    [InlineData(1, 0x0F0D2112u)]
    [InlineData(2, 0x00140180u)]
    [InlineData(3, 0x41059461u)]
    [InlineData(7, 0x41059461u)]
    public void IDCodesAreTheNDS9s(int info, uint value)
    {
        Assert.Equal(value, CP15.Read(0, 0, 0, info));
    }

    [Fact]
    public void ControlRegisterKeepsItsFixedBits()
    {
        WriteControl(0);
        Assert.Equal(0x00000078u, CP15.Read(0, 1, 0, 0));

        WriteControl(0xFFFFFFFF);
        Assert.Equal(0x000FF0FDu, CP15.Read(0, 1, 0, 0));
    }

    [Fact]
    public void ProtectionRegionsReadBackThroughBothOfTheirNames()
    {
        CP15.Write(0, 6, 3, 0, 0x0800003F);

        Assert.Equal(0x0800003Fu, CP15.Read(0, 6, 3, 1));
    }

    [Fact]
    public void DTCMCoversMainMemoryWhereItIsPlaced()
    {
        PlaceDTCM(0x027E000A);
        WriteControl(DTCMEnable);
        ARM9.Memory.Write32(0x027E0000, 0x12345678);

        Assert.Equal(0x12345678u, ARM9.Memory.Read32(0x027E0000));
        Assert.Equal(0u, ARM7.Memory.Read32(0x027E0000));
    }

    [Fact]
    public void AVirtualSizeBiggerThanTheTCMFillsItWithMirrors()
    {
        PlaceDTCM(0x027E000C);
        WriteControl(DTCMEnable);
        ARM9.Memory.Write32(0x027E0000, 0x12345678);

        Assert.Equal(0x12345678u, ARM9.Memory.Read32(0x027E4000));
    }

    [Fact]
    public void ITCMStartsAtZeroWhereverItsBaseIsSet()
    {
        PlaceITCM(0x01000020);
        WriteControl(ITCMEnable);
        ARM9.Memory.Write32(0x01FF8000, 0x12345678);

        Assert.Equal(0x12345678u, ARM9.Memory.Read32(0x00000000));
        Assert.Equal(0x00000020u, CP15.Read(0, 9, 1, 1));
    }

    [Fact]
    public void ITCMHasPriorityWhereTheTCMsOverlap()
    {
        PlaceITCM(0x00000020);
        PlaceDTCM(0x0000000A);
        WriteControl(ITCMEnable | DTCMEnable);
        ARM9.Memory.Write32(0x00000000, 0x12345678);

        Assert.Equal(0x12345678u, BitConverter.ToUInt32(CP15.ITCM, 0));
        Assert.Equal(0u, BitConverter.ToUInt32(CP15.DTCM, 0));
    }

    [Fact]
    public void LoadModeMakesTheTCMWriteOnly()
    {
        ARM9.Memory.Write32(0x027E0000, 0x11111111);
        PlaceDTCM(0x027E000A);
        WriteControl(DTCMEnable | DTCMLoadMode);
        ARM9.Memory.Write32(0x027E0000, 0x22222222);

        Assert.Equal(0x11111111u, ARM9.Memory.Read32(0x027E0000));

        WriteControl(DTCMEnable);
        Assert.Equal(0x22222222u, ARM9.Memory.Read32(0x027E0000));
    }

    [Fact]
    public void ITCMInLoadModeTakesWritesButNotReads()
    {
        PlaceITCM(0x00000020);
        WriteControl(ITCMEnable | ITCMLoadMode);
        ARM9.Memory.Write32(0x00000000, 0x12345678);

        Assert.Equal(0x12345678u, BitConverter.ToUInt32(CP15.ITCM, 0));
        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x00000000));
    }

    [Fact]
    public void TCMAccessesCostHalfABusCycle()
    {
        PlaceDTCM(0x027E000A);
        WriteControl(DTCMEnable);

        long start = ARM9.Scheduler.Cycles;
        ARM9.Memory.Read32(0x027E0000);
        Assert.Equal(1, ARM9.Scheduler.Cycles - start);
    }

    [Fact]
    public void HaltingWithAnInterruptPendingReturnsAtOnce()
    {
        ARM9.IO.Write32(0x04000208, 1);
        ARM9.IO.Write32(0x04000210, (uint)Interrupt.Timer0);
        ARM9.IO.Interrupts.Raise(Interrupt.Timer0);

        CP15.Write(0, 7, 0, 4, 0);

        Assert.Equal(0, ARM9.Scheduler.Cycles);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(10, 2)]
    [InlineData(10, 4)]
    [InlineData(13, 1)]
    [InlineData(14, 1)]
    [InlineData(14, 2)]
    public void CacheCommandsLeaveMemoryAsItIs(int cm, int info)
    {
        ARM9.Memory.Write32(0x02000000, 0x12345678);

        CP15.Write(0, 7, cm, info, 0x02000000);

        Assert.Equal(0x12345678u, ARM9.Memory.Read32(0x02000000));
    }

    [Fact]
    public void CachedReadsFillAWholeLineFromMainMemory()
    {
        EnableCaches();

        Assert.Equal(46, ReadCycles(0x02000000));
        Assert.Equal(1, ReadCycles(0x0200001C));
        Assert.Equal(46, ReadCycles(0x02000020));
    }

    [Fact]
    public void RunningUntimedLeavesTheCachesAndTimeAsTheyWere()
    {
        EnableCaches();
        long before = ARM9.Scheduler.Cycles;

        ARM9.Scheduler.RunUntimed(() => ARM9.Memory.Read32(0x02000000));

        Assert.Equal(before, ARM9.Scheduler.Cycles);
        Assert.Equal(46, ReadCycles(0x02000000));
    }

    [Fact]
    public void NothingIsCachedWithoutTheProtectionUnit()
    {
        EnableCaches();
        WriteControl(DataCacheEnable | InstructionCacheEnable | RoundRobinReplacement);

        Assert.Equal(20, ReadCycles(0x02000000));
        Assert.Equal(20, ReadCycles(0x02000000));
    }

    [Fact]
    public void NothingIsCachedOutsideCachableRegions()
    {
        EnableCaches();

        Assert.Equal(20, ReadCycles(0x02800000));
        Assert.Equal(20, ReadCycles(0x02800000));
    }

    [Fact]
    public void AHigherRegionDecidesWhereRegionsOverlap()
    {
        EnableCaches();
        CP15.Write(0, 6, 2, 0, 0x027E0021);

        Assert.Equal(20, ReadCycles(0x027E0000));
        Assert.Equal(20, ReadCycles(0x027E0000));
        Assert.Equal(46, ReadCycles(0x027DFFE0));
    }

    [Fact]
    public void TheFifthLineInASetReplacesTheFirst()
    {
        EnableCaches();
        for (uint line = 0; line < 5; line++)
        {
            ReadCycles(0x02000000 + (line * DataSetStride));
        }

        Assert.Equal(1, ReadCycles(0x02000000 + DataSetStride));
        Assert.Equal(46, ReadCycles(0x02000000));
    }

    [Fact]
    public void WritesDontFillLines()
    {
        EnableCaches();
        ARM9.Memory.Write32(0x02000000, 0x12345678);

        Assert.Equal(46, ReadCycles(0x02000000));
    }

    [Fact]
    public void CachedReadsSeeTheLatestWrites()
    {
        EnableCaches();
        ARM9.Memory.Read32(0x02000000);
        ARM9.Memory.Write32(0x02000000, 0x12345678);

        Assert.Equal(0x12345678u, ARM9.Memory.Read32(0x02000000));
    }

    [Fact]
    public void DMADoesntGoThroughTheCache()
    {
        EnableCaches();
        ARM9.Memory.ReadBus32(0x02000000);

        Assert.Equal(46, ReadCycles(0x02000000));
    }

    [Fact]
    public void CachedReadsAreNeverSequential()
    {
        EnableCaches();

        Assert.Equal(0, ARM9.Memory.SequentialSaving(0x02000000, isWrite: false));
        Assert.Equal(16, ARM9.Memory.SequentialSaving(0x02000000, isWrite: true));
    }

    [Fact]
    public void WriteBackWritesToACachedLineOnlyCostTheCache()
    {
        EnableCaches();
        CP15.Write(0, 3, 0, 0, 0x02);
        ReadCycles(0x02000000);

        Assert.Equal(1, WriteCycles(0x0200001C));
        Assert.Equal(0, ARM9.Memory.SequentialSaving(0x02000000, isWrite: true));
    }

    [Fact]
    public void WriteBackWritesToAnUncachedLineGoToMemory()
    {
        EnableCaches();
        CP15.Write(0, 3, 0, 0, 0x02);

        Assert.Equal(20, WriteCycles(0x02000000));
        Assert.Equal(46, ReadCycles(0x02000000));
    }

    [Fact]
    public void WriteThroughWritesGoToMemory()
    {
        EnableCaches();
        ReadCycles(0x02000000);

        Assert.Equal(20, WriteCycles(0x02000000));
    }

    [Fact]
    public void CachedBIOSReadsFillTheirLineFromTheBIOS()
    {
        EnableCaches();
        CP15.Write(0, 6, 6, 0, 0xFFFF001D);
        CP15.Write(0, 2, 0, 0, 0x42);

        Assert.Equal(22, ReadCycles(0xFFFF0020));
        Assert.Equal(1, ReadCycles(0xFFFF0024));
    }

    [Theory]
    [InlineData(6, 1)]
    [InlineData(14, 1)]
    public void InvalidatingADataLineMakesItMissAgain(int cm, int info)
    {
        EnableCaches();
        ReadCycles(0x02000000);

        CP15.Write(0, 7, cm, info, 0x02000010);

        Assert.Equal(46, ReadCycles(0x02000000));
    }

    [Fact]
    public void InvalidatingTheDataCacheEmptiesIt()
    {
        EnableCaches();
        ReadCycles(0x02000000);
        ReadCycles(0x02000020);

        CP15.Write(0, 7, 6, 0, 0);

        Assert.Equal(46, ReadCycles(0x02000000));
        Assert.Equal(46, ReadCycles(0x02000020));
    }

    [Fact]
    public void CleaningAndInvalidatingBySetAndIndexEmptiesThatWay()
    {
        EnableCaches();
        ReadCycles(0x02000000);
        ReadCycles(0x02000000 + DataSetStride);

        CP15.Write(0, 7, 14, 2, 1u << 30);

        Assert.Equal(1, ReadCycles(0x02000000));
        Assert.Equal(46, ReadCycles(0x02000000 + DataSetStride));
    }

    [Fact]
    public void CleaningLeavesLinesCached()
    {
        EnableCaches();
        ReadCycles(0x02000000);

        CP15.Write(0, 7, 10, 1, 0x02000000);

        Assert.Equal(1, ReadCycles(0x02000000));
    }

    [Fact]
    public void CodeFetchesGoThroughTheInstructionCache()
    {
        EnableCaches();

        Assert.Equal(46 + 7, ARM9.Memory.FetchCycles(0x02000000, 8, isThumb: false));
        Assert.Equal(8, ARM9.Memory.FetchCycles(0x02000000, 8, isThumb: false));
        Assert.Equal(46, ReadCycles(0x02000000));
    }

    [Fact]
    public void UncachedCodeFetchesCostTheBus()
    {
        Assert.Equal(2 * 18, ARM9.Memory.FetchCycles(0x02000000, 2, isThumb: false));
        Assert.Equal(2 * 9, ARM9.Memory.FetchCycles(0x02000000, 2, isThumb: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InvalidatingInstructionLinesMakesThemMissAgain(int info)
    {
        EnableCaches();
        ARM9.Memory.FetchCycles(0x02000000, 1, isThumb: false);

        CP15.Write(0, 7, 5, info, 0x02000000);

        Assert.Equal(46, ARM9.Memory.FetchCycles(0x02000000, 1, isThumb: false));
    }

    [Fact]
    public void PrefetchingAnInstructionLineFillsIt()
    {
        EnableCaches();

        CP15.Write(0, 7, 13, 1, 0x02000000);

        Assert.Equal(1, ARM9.Memory.FetchCycles(0x02000000, 1, isThumb: false));
    }

    [Fact]
    public void PseudoRandomReplacementIsntSupported()
    {
        EnableCaches();
        WriteControl(ProtectionUnitEnable | DataCacheEnable);

        Assert.Throws<NotSupportedException>(() => ARM9.Memory.Read32(0x02000000));
    }

    [Fact]
    public void LockingDownACacheIsntSupported()
    {
        CP15.Write(0, 9, 0, 0, 0);

        Assert.Equal(0u, CP15.Read(0, 9, 0, 1));
        Assert.Throws<NotSupportedException>(() => CP15.Write(0, 9, 0, 1, 1));
    }

    [Theory]
    [InlineData(0x02000015u)]
    [InlineData(0x02001019u)]
    public void RegionsGBATEKDoesntAllowArentSupported(uint region)
    {
        WriteControl(ProtectionUnitEnable);

        Assert.Throws<NotSupportedException>(() => CP15.Write(0, 6, 1, 0, region));
    }

    [Fact]
    public void UnsupportedRegistersThrow()
    {
        Assert.Throws<NotSupportedException>(() => CP15.Read(0, 15, 0, 0));
        Assert.Throws<NotSupportedException>(() => CP15.Write(0, 15, 0, 0, 0));
    }
}
