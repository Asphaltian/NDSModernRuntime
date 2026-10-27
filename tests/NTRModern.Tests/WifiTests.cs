namespace NTRModern.Tests;

public class WifiTests
{
    private const uint POWCNT2 = 0x04000304;
    private const uint IF = 0x04000214;
    private const uint W_ID = 0x04808000;
    private const uint W_MODE_RST = 0x04808004;
    private const uint W_TXSTATCNT = 0x04808008;
    private const uint W_IF = 0x04808010;
    private const uint W_IE = 0x04808012;
    private const uint W_RXCNT = 0x04808030;
    private const uint W_POWER_US = 0x04808036;
    private const uint W_POWER_TX = 0x04808038;
    private const uint W_POWERSTATE = 0x0480803C;
    private const uint W_POWERFORCE = 0x04808040;
    private const uint W_RANDOM = 0x04808044;
    private const uint W_POWER_048 = 0x04808048;
    private const uint W_RXBUF_WRCSR = 0x04808054;
    private const uint W_RXBUF_WR_ADDR = 0x04808056;
    private const uint W_TXBUF_BEACON = 0x04808080;
    private const uint W_BEACONINT = 0x0480808C;
    private const uint W_TXBUF_LOC1 = 0x048080A0;
    private const uint W_TXBUF_LOC3 = 0x048080A8;
    private const uint W_TXREQ_RESET = 0x048080AC;
    private const uint W_TXREQ_SET = 0x048080AE;
    private const uint W_TXREQ_READ = 0x048080B0;
    private const uint W_TXBUSY = 0x048080B6;
    private const uint W_TXSTAT = 0x048080B8;
    private const uint W_US_COUNTCNT = 0x048080E8;
    private const uint W_US_COMPARECNT = 0x048080EA;
    private const uint W_US_COMPARE0 = 0x048080F0;
    private const uint W_US_COUNT0 = 0x048080F8;
    private const uint W_PRE_BEACON = 0x04808110;
    private const uint W_CMD_COUNT = 0x04808118;
    private const uint W_BEACON_COUNT = 0x0480811C;
    private const uint W_POST_BEACON = 0x04808134;
    private const uint W_BB_CNT = 0x04808158;
    private const uint W_BB_WRITE = 0x0480815A;
    private const uint W_BB_READ = 0x0480815C;
    private const uint W_BB_POWER = 0x04808168;
    private const uint W_RF_DATA2 = 0x0480817C;
    private const uint W_RF_DATA1 = 0x0480817E;
    private const uint W_RF_PINS = 0x0480819C;
    private const uint W_RXSTAT_OVF_IF = 0x048081AC;
    private const uint W_RXSTAT = 0x048081B0;
    private const uint W_TX_SEQNO = 0x04808210;
    private const uint W_RF_STATUS = 0x04808214;
    private const uint W_IF_SET = 0x0480821C;
    private const uint W_CONFIG_254 = 0x04808254;
    private const uint Frame = 0x04804100;
    private const ushort FrameLocation = 0x0080;
    private const ushort WifiOn = 1 << 1;

    private static void PowerOn(Model model)
    {
        Hardware.Reset(model);
        ARM7.IO.Write16(POWCNT2, 1 | WifiOn);
    }

    private static void WakeUp()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_POWER_US, 0);
        ARM7.Memory.Write16(W_MODE_RST, 1);
        ARM7.Memory.Write16(W_POWERSTATE, 2);
        Advance(Microseconds(2048));
        ARM7.Memory.Write16(W_IF, 0xFFFF);
    }

    private static long Microseconds(long microseconds) => ((microseconds * SystemClock.CyclesPerSecond) + 999_999) / 1_000_000;

    private static void Advance(long cycles)
    {
        long target = Math.Max(ARM9.Scheduler.Cycles, ARM7.Scheduler.Cycles) + cycles;
        (ARM9.Scheduler.Cycles, ARM7.Scheduler.Cycles) = (target, target);
        SystemClock.Synchronize(Processor.ARM9);
    }

    private static void StartCounting()
    {
        ARM7.Memory.Write16(W_POWER_US, 0);
        ARM7.Memory.Write16(W_US_COUNTCNT, 1);
    }

    private static void WriteFrame(uint address, ushort destination, int length)
    {
        ushort[] frame = [0, 0, 0, 0, 0x0014, (ushort)length, 0x0040, 0, destination, 0xFFFF, 0xFFFF, 0x1600, 0x1156, 0x3322, 0xFFFF, 0xFFFF, 0xFFFF, 0x1234];
        for (int index = 0; index < frame.Length; index++)
        {
            ARM7.Memory.Write16(address + (uint)(index * 2), frame[index]);
        }
    }

    [Theory]
    [InlineData(Model.DS, 0x1440)]
    [InlineData(Model.DSLite, 0xC340)]
    [InlineData(Model.DSi, 0xC340)]
    public void TheChipIDDependsOnTheModel(Model model, int chipID)
    {
        PowerOn(model);

        Assert.Equal(chipID, ARM7.Memory.Read16(W_ID));
    }

    [Fact]
    public void TheWifiHardwareIsOutOfReachWhileItsPowerIsOff()
    {
        Hardware.Reset(Model.DSLite);

        Assert.Throws<NotSupportedException>(() => ARM7.Memory.Read16(W_ID));
    }

    [Fact]
    public void TransferRequestsAreSetAndResetBitByBit()
    {
        PowerOn(Model.DSLite);

        ARM7.Memory.Write16(W_TXREQ_SET, 0x0D);
        ARM7.Memory.Write16(W_TXREQ_RESET, 0x04);

        Assert.Equal(0x19, ARM7.Memory.Read16(W_TXREQ_READ));
    }

    [Theory]
    [InlineData(0x006, 0x003F)]
    [InlineData(0x018, 0xFFFF)]
    [InlineData(0x01A, 0xFFFF)]
    [InlineData(0x01C, 0xFFFF)]
    [InlineData(0x020, 0xFFFF)]
    [InlineData(0x022, 0xFFFF)]
    [InlineData(0x024, 0xFFFF)]
    [InlineData(0x02A, 0x07FF)]
    [InlineData(0x050, 0xFFFF)]
    [InlineData(0x052, 0xFFFF)]
    [InlineData(0x056, 0x0FFE)]
    [InlineData(0x058, 0x1FFE)]
    [InlineData(0x05A, 0x0FFE)]
    [InlineData(0x05C, 0x0FFF)]
    [InlineData(0x062, 0x1FFE)]
    [InlineData(0x064, 0x0FFF)]
    [InlineData(0x068, 0x1FFE)]
    [InlineData(0x06C, 0x0FFF)]
    [InlineData(0x074, 0x1FFE)]
    [InlineData(0x122, 0xFFFF)]
    [InlineData(0x124, 0xFFFF)]
    [InlineData(0x128, 0xFFFF)]
    [InlineData(0x130, 0x0FFF)]
    [InlineData(0x132, 0x8FFF)]
    [InlineData(0x134, 0xFFFF)]
    [InlineData(0x140, 0xFFFF)]
    [InlineData(0x142, 0xFFFF)]
    public void RegistersPassTheWirelessDriversSelfTest(int port, int mask)
    {
        PowerOn(Model.DSLite);
        uint address = 0x04808000 + (uint)port;

        foreach (ushort pattern in (ushort[])[0xFFFF, 0x5A5A, 0xA5A5, 0x1234, 0x0000])
        {
            ARM7.Memory.Write16(address, (ushort)(pattern & mask));
            Assert.Equal(pattern & mask, ARM7.Memory.Read16(address));
        }
    }

    [Fact]
    public void TheWifiIRQFiresOnlyWhenRequestsAppear()
    {
        PowerOn(Model.DSLite);

        ARM7.Memory.Write16(W_IE, 0x0001);
        ARM7.Memory.Write16(W_IF_SET, 0x0003);
        Assert.Equal((uint)Interrupt.Wifi, ARM7.IO.Read32(IF));

        ARM7.IO.Write32(IF, (uint)Interrupt.Wifi);
        ARM7.Memory.Write16(W_IF_SET, 0x0001);
        Assert.Equal(0u, ARM7.IO.Read32(IF));

        ARM7.Memory.Write16(W_IF, 0x0001);
        Assert.Equal(0x0002, ARM7.Memory.Read16(W_IF));
        ARM7.Memory.Write16(W_IF_SET, 0x0001);
        Assert.Equal((uint)Interrupt.Wifi, ARM7.IO.Read32(IF));
    }

    [Fact]
    public void TheRandomRegisterReturnsTheValueLatchedAtTheLastRead()
    {
        PowerOn(Model.DSLite);

        ARM7.Scheduler.Cycles = 0;
        ushort first = ARM7.Memory.Read16(W_RANDOM);
        ushort second = ARM7.Memory.Read16(W_RANDOM);

        Assert.Equal((1, 0x007F), (first, second));
    }

    [Theory]
    [InlineData(Model.DS, 0x0050, 0xFFFF)]
    [InlineData(Model.DSLite, 0x0000, 0xEEEE)]
    public void WriteOnlyPortsReadTheWayEachChipDoes(Model model, int transferRequestRead, int config254)
    {
        PowerOn(model);

        Assert.Equal(transferRequestRead, ARM7.Memory.Read16(W_TXREQ_SET));
        Assert.Equal(config254, ARM7.Memory.Read16(W_CONFIG_254));
    }

    [Fact]
    public void BasebandTransfersReachTheChipItsFirmwareInitialized()
    {
        Hardware.Reset(Model.DSLite);
        Firmware.Build(new UserSettings("Player", "", Language.English, 0, 1, 1), []);
        ARM7.IO.Write16(POWCNT2, 1 | WifiOn);
        ARM7.Memory.Write16(W_POWER_US, 0);
        ARM7.Memory.Write16(W_BB_POWER, 0);

        ARM7.Memory.Write16(W_BB_CNT, 0x6001);
        Assert.Equal(0x17, ARM7.Memory.Read16(W_BB_READ));

        ARM7.Memory.Write16(W_BB_WRITE, 0x42);
        ARM7.Memory.Write16(W_BB_CNT, 0x5001);
        ARM7.Memory.Write16(W_BB_CNT, 0x6001);
        Assert.Equal(0x42, ARM7.Memory.Read16(W_BB_READ));

        ARM7.Memory.Write16(W_BB_CNT, 0x6000);
        Assert.Equal(0x6D, ARM7.Memory.Read16(W_BB_READ));
    }

    [Fact]
    public void BasebandPortsWorkWithTheirPowerOff()
    {
        Hardware.Reset(Model.DSLite);
        Firmware.Build(new UserSettings("Player", "", Language.English, 0, 1, 1), []);
        ARM7.IO.Write16(POWCNT2, 1 | WifiOn);

        ARM7.Memory.Write16(W_BB_CNT, 0x6000);

        Assert.Equal(0x6D, ARM7.Memory.Read16(W_BB_READ));
    }

    [Fact]
    public void TheDSLitesRFChipTakesAnIndexAndAByte()
    {
        Hardware.Reset(Model.DSLite);
        Firmware.Build(new UserSettings("Player", "", Language.English, 0, 1, 1), []);
        ARM7.IO.Write16(POWCNT2, 1 | WifiOn);

        ARM7.Memory.Write16(W_RF_DATA1, 0x0100);
        ARM7.Memory.Write16(W_RF_DATA2, 6);
        Assert.Equal(0x014C, ARM7.Memory.Read16(W_RF_DATA1));

        ARM7.Memory.Write16(W_RF_DATA1, 0x0199);
        ARM7.Memory.Write16(W_RF_DATA2, 5);
        ARM7.Memory.Write16(W_RF_DATA1, 0x4100);
        ARM7.Memory.Write16(W_RF_DATA2, 6);
        Assert.Equal(0x4199, ARM7.Memory.Read16(W_RF_DATA1));
    }

    [Fact]
    public void TheOriginalDSsRFChipResetsToItsPowerOnValues()
    {
        PowerOn(Model.DS);

        ARM7.Memory.Write16(W_RF_DATA1, 0x1234);
        ARM7.Memory.Write16(W_RF_DATA2, 0x0006);
        ARM7.Memory.Write16(W_RF_DATA1, 0x0000);
        ARM7.Memory.Write16(W_RF_DATA2, 0x1F << 2);
        ARM7.Memory.Write16(W_RF_DATA2, 0x0084);

        Assert.Equal((0x9003, 0x0084), (ARM7.Memory.Read16(W_RF_DATA1), ARM7.Memory.Read16(W_RF_DATA2)));
    }

    [Fact]
    public void ByteWritesAreIgnored()
    {
        PowerOn(Model.DSLite);

        ARM7.Memory.Write8(W_TXREQ_SET, 0x01);

        Assert.Equal(0x10, ARM7.Memory.Read16(W_TXREQ_READ));
    }

    [Fact]
    public void ThePowerRequestRaisesIRQ11AndWakesTheChipLater()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_POWER_US, 0);
        ARM7.Memory.Write16(W_MODE_RST, 1);
        ARM7.Memory.Write16(W_IE, 1 << 11);

        ARM7.Memory.Write16(W_POWERSTATE, 2);
        Assert.Equal((0x0302, (uint)Interrupt.Wifi), (ARM7.Memory.Read16(W_POWERSTATE), ARM7.IO.Read32(IF)));

        Advance(Microseconds(2040));
        Assert.Equal(0x0302, ARM7.Memory.Read16(W_POWERSTATE));

        Advance(Microseconds(10));
        Assert.Equal((0x0000, 1, 0x0084), (ARM7.Memory.Read16(W_POWERSTATE), ARM7.Memory.Read16(W_RF_STATUS), ARM7.Memory.Read16(W_RF_PINS)));
    }

    [Fact]
    public void ForcingThePowerOffPutsTheChipToSleepAtOnce()
    {
        WakeUp();
        ARM7.Memory.Write16(W_TXREQ_SET, 0x000D);

        ARM7.Memory.Write16(W_POWERFORCE, 0x8001);

        Assert.Equal(0x0200, ARM7.Memory.Read16(W_POWERSTATE));
        Assert.Equal(0x0000, ARM7.Memory.Read16(W_TXREQ_READ));
        Assert.Equal((9, 0x0046), (ARM7.Memory.Read16(W_RF_STATUS), ARM7.Memory.Read16(W_RF_PINS)));
    }

    [Fact]
    public void ForcingThePowerOnWakesTheChipLater()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_POWER_US, 0);

        ARM7.Memory.Write16(W_POWERFORCE, 0x8000);
        Assert.Equal(0x0300, ARM7.Memory.Read16(W_POWERSTATE));

        Advance(Microseconds(2050));
        Assert.Equal(0x0000, ARM7.Memory.Read16(W_POWERSTATE));
    }

    [Fact]
    public void ABeaconGoesOutAfterIRQ14AndStaysEnabled()
    {
        WakeUp();
        WriteFrame(Frame, 0xFFFF, 34);
        ARM7.Memory.Write16(W_POWER_TX, 0);
        ARM7.Memory.Write16(W_TXSTATCNT, 0x8000);
        ARM7.Memory.Write16(W_TXBUF_BEACON, 0x8000 | FrameLocation);
        ARM7.Memory.Write16(W_BEACON_COUNT, 1);
        ARM7.Memory.Write16(W_US_COMPARECNT, 1);
        StartCounting();

        Advance(Microseconds(1024 + 20));
        Assert.Equal((0x0010, 3), (ARM7.Memory.Read16(W_TXBUSY), ARM7.Memory.Read16(W_RF_STATUS)));

        Advance(Microseconds(192 + (34 * 4)));
        Assert.Equal(0x0082, ARM7.Memory.Read16(W_IF) & 0x0082);
        Assert.Equal((0x0301, 0x0000), (ARM7.Memory.Read16(W_TXSTAT), ARM7.Memory.Read16(W_TXBUSY)));
        Assert.Equal(0x8000 | FrameLocation, ARM7.Memory.Read16(W_TXBUF_BEACON));
    }

    [Fact]
    public void Port048Bit1WakesTheChipOnceItsPowerIsNoLongerForcedOff()
    {
        WakeUp();
        ARM7.Memory.Write16(W_POWERFORCE, 0x8001);

        ARM7.Memory.Write16(W_POWER_048, 3);
        Assert.Equal(0x0200, ARM7.Memory.Read16(W_POWERSTATE));

        ARM7.Memory.Write16(W_POWERFORCE, 0);
        ARM7.Memory.Write16(W_POWER_048, 3);
        Advance(Microseconds(2050));
        Assert.Equal(0x0000, ARM7.Memory.Read16(W_POWERSTATE));
    }

    [Fact]
    public void TheMicrosecondCounterOnlyRunsWhileEnabledAndPowered()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_US_COUNTCNT, 1);
        Advance(Microseconds(500));
        Assert.Equal(0, ARM7.Memory.Read16(W_US_COUNT0));

        ARM7.Memory.Write16(W_POWER_US, 0);
        Advance(Microseconds(1000));
        Assert.Equal(1000, ARM7.Memory.Read16(W_US_COUNT0));

        ARM7.Memory.Write16(W_US_COUNTCNT, 0);
        Advance(Microseconds(1000));
        Assert.Equal(1000, ARM7.Memory.Read16(W_US_COUNT0));
    }

    [Fact]
    public void WritingTheCounterSetsIt()
    {
        PowerOn(Model.DSLite);
        StartCounting();

        ARM7.Memory.Write16(W_US_COUNT0 + 2, 0x0012);
        ARM7.Memory.Write16(W_US_COUNT0, 0x3400);
        Advance(Microseconds(100));

        Assert.Equal((0x3464, 0x0012), (ARM7.Memory.Read16(W_US_COUNT0), ARM7.Memory.Read16(W_US_COUNT0 + 2)));
    }

    [Fact]
    public void ACompareMatchRaisesIRQ14OnTheMillisecondItNames()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_IE, 1 << 14);
        ARM7.Memory.Write16(W_US_COMPARE0, 3 << 10);
        for (uint half = 2; half < 8; half += 2)
        {
            ARM7.Memory.Write16(W_US_COMPARE0 + half, 0);
        }

        ARM7.Memory.Write16(W_US_COMPARECNT, 1);
        StartCounting();

        Advance(Microseconds((3 * 1024) - 20));
        Assert.Equal(0, ARM7.Memory.Read16(W_IF));

        Advance(Microseconds(40));
        Assert.Equal((1 << 14, 0xFFFF), (ARM7.Memory.Read16(W_IF), ARM7.Memory.Read16(W_POST_BEACON)));
    }

    [Fact]
    public void TheBeaconCountRaisesIRQ14AndReloadsFromTheInterval()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_IE, 1 << 14);
        ARM7.Memory.Write16(W_POWER_TX, 0);
        ARM7.Memory.Write16(W_BEACONINT, 5);
        ARM7.Memory.Write16(W_BEACON_COUNT, 2);
        ARM7.Memory.Write16(W_TXREQ_SET, 0x000D);
        ARM7.Memory.Write16(W_US_COMPARECNT, 1);
        StartCounting();

        Advance(Microseconds((2 * 1024) + 20));

        Assert.Equal((1 << 14) | (1 << 15), ARM7.Memory.Read16(W_IF));
        Assert.Equal((5, 0x0010), (ARM7.Memory.Read16(W_BEACON_COUNT), ARM7.Memory.Read16(W_TXREQ_READ)));
    }

    [Fact]
    public void AZeroBeaconCountReloadsWithoutAnIRQ()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_IE, 1 << 14);
        ARM7.Memory.Write16(W_US_COMPARECNT, 1);
        StartCounting();

        Advance(Microseconds(1024 + 20));

        Assert.Equal((0, 0x0064), (ARM7.Memory.Read16(W_IF), ARM7.Memory.Read16(W_BEACON_COUNT)));
    }

    [Fact]
    public void IRQ15ComesPreBeaconMicrosecondsBeforeTheBeacon()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_IE, 1 << 15);
        ARM7.Memory.Write16(W_POWER_TX, 0);
        ARM7.Memory.Write16(W_BEACON_COUNT, 3);
        ARM7.Memory.Write16(W_PRE_BEACON, 0x0200);
        ARM7.Memory.Write16(W_US_COMPARECNT, 1);
        StartCounting();

        Advance(Microseconds(2560 - 20));
        Assert.Equal(0, ARM7.Memory.Read16(W_IF));

        Advance(Microseconds(40));
        Assert.Equal(1 << 15, ARM7.Memory.Read16(W_IF));
    }

    [Theory]
    [InlineData(0x0000, 0x0200, 9)]
    [InlineData(0x0002, 0x0000, 1)]
    public void IRQ13PutsTheChipToSleepUnlessAutoSleepIsOff(int powerTX, int powerState, int rfStatus)
    {
        WakeUp();
        ARM7.Memory.Write16(W_IE, 1 << 13);
        ARM7.Memory.Write16(W_POWER_TX, (ushort)powerTX);
        ARM7.Memory.Write16(W_POST_BEACON, 2);
        StartCounting();

        Advance(Microseconds((2 * 1024) + 20));

        Assert.Equal(1 << 13, ARM7.Memory.Read16(W_IF));
        Assert.Equal((powerState, rfStatus), (ARM7.Memory.Read16(W_POWERSTATE), ARM7.Memory.Read16(W_RF_STATUS)));
    }

    [Fact]
    public void ABroadcastFrameGoesOutAtItsRateAfterALongPreamble()
    {
        WakeUp();
        WriteFrame(Frame, 0xFFFF, 34);
        ARM7.Memory.Write16(W_IE, 0x0082);
        ARM7.Memory.Write16(W_TXBUF_LOC3, 0x8000 | FrameLocation);
        ARM7.Memory.Write16(W_TXREQ_SET, 0x0008);
        Assert.Equal((3, 0x0008), (ARM7.Memory.Read16(W_RF_STATUS), ARM7.Memory.Read16(W_TXBUSY)));

        Advance(Microseconds(192 - 5));
        Assert.Equal(0, ARM7.Memory.Read16(W_IF));

        Advance(Microseconds(10));
        Assert.Equal((0x0080, 0x0046), (ARM7.Memory.Read16(W_IF), ARM7.Memory.Read16(W_RF_PINS)));
        Assert.Equal((0x0000, 1), (ARM7.Memory.Read16(Frame + 0x22), ARM7.Memory.Read16(W_TX_SEQNO)));

        Advance(Microseconds(34 * 4));
        Assert.Equal(0x0082, ARM7.Memory.Read16(W_IF));
        Assert.Equal((0x0001, 0x2001), (ARM7.Memory.Read16(Frame), ARM7.Memory.Read16(W_TXSTAT)));
        Assert.Equal((FrameLocation, 0x0000), (ARM7.Memory.Read16(W_TXBUF_LOC3), ARM7.Memory.Read16(W_TXBUSY)));
        Assert.Equal((1, 0x0084), (ARM7.Memory.Read16(W_RF_STATUS), ARM7.Memory.Read16(W_RF_PINS)));
    }

    [Fact]
    public void LOC3GoesOutBeforeLOC1()
    {
        WakeUp();
        WriteFrame(Frame, 0xFFFF, 34);
        WriteFrame(Frame + 0x40, 0xFFFF, 34);
        ARM7.Memory.Write16(W_TXBUF_LOC1, 0x8000 | FrameLocation);
        ARM7.Memory.Write16(W_TXBUF_LOC3, 0x8000 | (FrameLocation + 0x20));
        ARM7.Memory.Write16(W_TXREQ_SET, 0x0009);
        Assert.Equal(0x0009, ARM7.Memory.Read16(W_TXBUSY));

        Advance(Microseconds(192 + (34 * 4) + 5));
        Assert.Equal((0x2001, 0x0001), (ARM7.Memory.Read16(W_TXSTAT), ARM7.Memory.Read16(W_TXBUSY)));

        Advance(Microseconds(192 + (34 * 4) + 5));
        Assert.Equal((0x0001, 0x0000), (ARM7.Memory.Read16(W_TXSTAT), ARM7.Memory.Read16(W_TXBUSY)));
    }

    [Fact]
    public void AFrameWaitsForTheChipToWakeUp()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_POWER_US, 0);
        ARM7.Memory.Write16(W_MODE_RST, 1);
        WriteFrame(Frame, 0xFFFF, 34);
        ARM7.Memory.Write16(W_TXBUF_LOC1, 0x8000 | FrameLocation);
        ARM7.Memory.Write16(W_TXREQ_SET, 0x0001);
        Assert.Equal((9, 0x0001), (ARM7.Memory.Read16(W_RF_STATUS), ARM7.Memory.Read16(W_TXBUSY)));

        ARM7.Memory.Write16(W_POWERSTATE, 2);
        Advance(Microseconds(2050));

        Assert.Equal(3, ARM7.Memory.Read16(W_RF_STATUS));
    }

    [Fact]
    public void FramesToASingleAddressArentSupported()
    {
        WakeUp();
        WriteFrame(Frame, 0x1600, 34);
        ARM7.Memory.Write16(W_TXBUF_LOC1, 0x8000 | FrameLocation);

        Assert.Throws<NotSupportedException>(() => ARM7.Memory.Write16(W_TXREQ_SET, 0x0001));
    }

    [Fact]
    public void LatchingTheReceiveCursorCopiesTheWriteAddress()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_RXBUF_WR_ADDR, 0x0123);

        ARM7.Memory.Write16(W_RXCNT, 0x8001);

        Assert.Equal((0x0123, 0x8000), (ARM7.Memory.Read16(W_RXBUF_WRCSR), ARM7.Memory.Read16(W_RXCNT)));
    }

    [Fact]
    public void ReceiveStatisticsShowTheirHalfOverflowsAndClearWhenRead()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_RXSTAT, 0x0081);
        ARM7.Memory.Write16(W_RXSTAT + 2, 0x8001);

        Assert.Equal(0x0005, ARM7.Memory.Read16(W_RXSTAT_OVF_IF));
        Assert.Equal((0x0081, 0x0000), (ARM7.Memory.Read16(W_RXSTAT), ARM7.Memory.Read16(W_RXSTAT)));
    }

    [Fact]
    public void TheCommandCounterCountsDownEvery10MicrosecondsAndStopsAtZero()
    {
        PowerOn(Model.DSLite);
        ARM7.Memory.Write16(W_CMD_COUNT, 100);

        Advance(Microseconds(505));
        Assert.Equal(50, ARM7.Memory.Read16(W_CMD_COUNT));

        Advance(Microseconds(1000));
        Assert.Equal(0, ARM7.Memory.Read16(W_CMD_COUNT));
    }
}
