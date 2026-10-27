namespace NTRModern;

internal static partial class Wifi
{
    public const uint RegionStart = 0x04800000;
    public const uint RegionEnd = 0x04810000;

    private const uint WS1 = 0x8000;
    private const uint RegisterMask = 0xFFF;
    private const uint UnusedStart = 0x2000;
    private const uint RAMStart = 0x4000;
    private const uint RAMEnd = 0x6000;
    private const int RAMSize = 0x2000;
    private const ushort Unused = 0xFFFF;

    private const int W_ID = 0x000;
    private const int W_MODE_RST = 0x004;
    private const int W_IF = 0x010;
    private const int W_IE = 0x012;
    private const int W_RXCNT = 0x030;
    private const int W_INTERNAL_034 = 0x034;
    private const int W_POWER_US = 0x036;
    private const int W_POWER_TX = 0x038;
    private const int W_POWERSTATE = 0x03C;
    private const int W_POWERFORCE = 0x040;
    private const int W_RANDOM = 0x044;
    private const int W_POWER_048 = 0x048;
    private const int W_RXBUF_WRCSR = 0x054;
    private const int W_RXBUF_WR_ADDR = 0x056;
    private const int W_TXBUF_BEACON = 0x080;
    private const int W_TXBUF_CMD = 0x090;
    private const int W_TXBUF_REPLY1 = 0x094;
    private const int W_TXBUF_REPLY2 = 0x098;
    private const int W_INTERNAL_09C = 0x09C;
    private const int W_TXBUF_LOC1 = 0x0A0;
    private const int W_TXBUF_LOC2 = 0x0A4;
    private const int W_TXBUF_LOC3 = 0x0A8;
    private const int W_TXREQ_RESET = 0x0AC;
    private const int W_TXREQ_SET = 0x0AE;
    private const int W_TXREQ_READ = 0x0B0;
    private const int W_TXBUF_RESET = 0x0B4;
    private const int W_PREAMBLE = 0x0BC;
    private const int W_US_COUNTCNT = 0x0E8;
    private const int W_US_COMPARECNT = 0x0EA;
    private const int W_BB_CNT = 0x158;
    private const int W_BB_WRITE = 0x15A;
    private const int W_BB_READ = 0x15C;
    private const int W_BB_BUSY = 0x15E;
    private const int W_BB_POWER = 0x168;
    private const int W_RF_DATA2 = 0x17C;
    private const int W_RF_DATA1 = 0x17E;
    private const int W_RF_BUSY = 0x180;
    private const int W_RF_CNT = 0x184;
    private const int W_RF_PINS = 0x19C;
    private const int W_RXSTAT_INC_IF = 0x1A8;
    private const int W_RXSTAT_OVF_IF = 0x1AC;
    private const int W_RXSTAT = 0x1B0;
    private const int W_RX_COUNT = 0x1C4;
    private const int W_RF_STATUS = 0x214;
    private const int W_IF_SET = 0x21C;
    private const int W_RX_MAC_ADDR_0 = 0x24C;
    private const int W_RX_MAC_ADDR_2 = 0x250;
    private const int W_CONFIG_254 = 0x254;
    private const int W_X_290 = 0x290;
    private const int W_INTERNAL_2B8 = 0x2B8;

    private const ushort ModeResetWritable = 0x9FFF;
    private const ushort ReceiveControlWritable = 0xFF0E;
    private const ushort LatchWriteCursor = 1 << 0;
    private const ushort CopyReply = 1 << 7;
    private const ushort RequestPowerEnable = 1 << 1;
    private const ushort PowerEnablePending = 1 << 8;
    private const ushort PowerDisabled = 1 << 9;
    private const long PowerUpMicroseconds = 2048;
    private const int RFWakeupIRQ = 11;
    private const ushort PowerForceWritable = 0x8001;
    private const ushort ForcedPowerDisable = 1 << 0;
    private const ushort ApplyPowerForce = 1 << 15;
    private const ushort WakeUp = 1 << 1;
    private const ushort TransmitEnable = 1 << 0;
    private const ushort ResetRXPorts = 1 << 13;
    private const ushort ResetMACPorts = 1 << 14;
    private const ushort TransferRequests = 0x000F;
    private const ushort LocationEnable = 1 << 15;
    private const ushort NoIRQ10 = unchecked((ushort)~(1 << 10));
    private const ushort PreambleReadable = 0x0003;
    private const ushort PreambleWritable = 0x0007;
    private const ushort RandomSeed = 1;
    private const ushort RFReceive = 1;
    private const ushort RFIdle = 9;
    private const ushort ReceivePins = 0x0084;
    private const ushort IdlePins = 0x0046;
    private const int BasebandWrite = 5;
    private const int BasebandRead = 6;

    private static readonly byte[] RAM = new byte[RAMSize];
    private static readonly ushort[] Ports = new ushort[0x800];
    private static readonly int[] FirstAccessTimes = [10, 8, 6, 18];
    private static readonly ushort[] RandomSequence = CreateRandomSequence();

    private static readonly Dictionary<int, (ushort Writable, ushort Initial)> PlainRegisters = new()
    {
        [0x006] = (0x007F, 0x0000),
        [0x008] = (0xFFFF, 0x0000),
        [0x00A] = (0xFFFF, 0x0000),
        [0x018] = (0xFFFF, 0x0000),
        [0x01A] = (0xFFFF, 0x0000),
        [0x01C] = (0xFFFF, 0x0000),
        [0x020] = (0xFFFF, 0x0000),
        [0x022] = (0xFFFF, 0x0000),
        [0x024] = (0xFFFF, 0x0000),
        [0x028] = (0x000F, 0x0000),
        [0x02A] = (0x07FF, 0x0000),
        [0x02C] = (0xFFFF, 0x0707),
        [0x032] = (0xFFFF, 0x0000),
        [W_POWER_US] = (0x0003, 0x0001),
        [W_POWER_TX] = (0x0007, 0x0003),
        [W_POWER_048] = (0x0003, 0x0000),
        [0x050] = (0xFFFF, 0x4000),
        [0x052] = (0xFFFF, 0x4800),
        [0x056] = (0x0FFF, 0x0000),
        [0x058] = (0x1FFE, 0x0000),
        [0x05A] = (0x0FFF, 0x0000),
        [0x05C] = (0x0FFF, 0x0000),
        [0x062] = (0x1FFE, 0x0000),
        [0x064] = (0x0FFF, 0x0000),
        [0x068] = (0x1FFE, 0x0000),
        [0x06C] = (0x0FFF, 0x0000),
        [0x074] = (0x1FFE, 0x0000),
        [0x076] = (0x0FFF, 0x0000),
        [W_TXBUF_BEACON] = (0xFFFF, 0x0000),
        [0x084] = (0x00FF, 0x0000),
        [W_LISTENCOUNT] = (0x00FF, 0x0000),
        [W_BEACONINT] = (0x03FF, 0x0064),
        [W_LISTENINT] = (0x00FF, 0x0000),
        [W_TXBUF_CMD] = (0xFFFF, 0x0000),
        [W_TXBUF_REPLY1] = (0xFFFF, 0x0000),
        [W_INTERNAL_09C] = (0xFFFF, 0x0050),
        [W_TXBUF_LOC1] = (0xFFFF, 0x0000),
        [W_TXBUF_LOC2] = (0xFFFF, 0x0000),
        [W_TXBUF_LOC3] = (0xFFFF, 0x0000),
        [0x0C0] = (0xFFFF, 0x0000),
        [0x0C4] = (0xFFFF, 0x0000),
        [0x0D0] = (0x1FFF, 0x0401),
        [0x0D4] = (0x0003, 0x0001),
        [0x0D8] = (0x0FFF, 0x0004),
        [0x0DA] = (0xFFFF, 0x0602),
        [0x0E0] = (0x000F, 0x0008),
        [0x0EC] = (0x3F1F, 0x3F03),
        [W_CMD_COUNTCNT] = (0x0001, 0x0001),
        [W_PRE_BEACON] = (0xFFFF, 0x0000),
        [0x120] = (0x81FF, 0x0048),
        [0x122] = (0xFFFF, 0x4840),
        [0x124] = (0xFFFF, 0x0000),
        [0x128] = (0xFFFF, 0x0000),
        [0x130] = (0x0FFF, 0x0142),
        [0x132] = (0x8FFF, 0x8064),
        [W_POST_BEACON] = (0xFFFF, 0xFFFF),
        [0x140] = (0xFFFF, 0x0000),
        [0x142] = (0xFFFF, 0x2443),
        [0x144] = (0x00FF, 0x0042),
        [0x146] = (0x00FF, 0x0016),
        [0x148] = (0x00FF, 0x0016),
        [0x14A] = (0x00FF, 0x0016),
        [0x14C] = (0xFFFF, 0x162C),
        [0x150] = (0xFF3F, 0x0204),
        [0x154] = (0x7A7F, 0x0058),
        [0x160] = (0x4100, 0x0100),
        [W_BB_POWER] = (0x800F, 0x800D),
        [W_RF_DATA2] = (0xFFFF, 0x0800),
        [W_RF_DATA1] = (0xFFFF, 0xC008),
        [W_RF_CNT] = (0x413F, 0x0018),
        [W_TX_HDR_CNT] = (0x0007, 0x0000),
        [0x1A0] = (0x0933, 0x0000),
        [0x1A2] = (0x0003, 0x0001),
        [0x1AA] = (0xFFFF, 0x0000),
        [0x1AE] = (0xFFFF, 0x0000),
        [0x244] = (0xFFFF, 0x0000),
    };

    private static readonly Dictionary<int, ushort> ClearedOnReadPorts = new()
    {
        [W_RXSTAT] = 0x00FF,
        [0x1B2] = 0xFFFF,
        [0x1B4] = 0x00FF,
        [0x1B6] = 0xFFFF,
        [0x1B8] = 0x00FF,
        [0x1BA] = 0x00FF,
        [0x1BC] = 0xFFFF,
        [0x1BE] = 0xFFFF,
        [0x1C0] = 0x00FF,
        [W_RX_COUNT] = 0x0000,
        [0x1D0] = 0xFF00,
        [0x1D2] = 0xFFFF,
        [0x1D4] = 0xFFFF,
        [0x1D6] = 0xFFFF,
        [0x1D8] = 0xFFFF,
        [0x1DA] = 0xFFFF,
        [0x1DC] = 0xFFFF,
        [0x1DE] = 0xFFFF,
    };

    private static readonly int[] ReceiveStatisticsBytes = [0x0, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8, 0xA, 0xC, 0xD, 0xE, 0xF];

    private static readonly (int Port, ushort Value)[] ModeResetRisingPorts = [(W_INTERNAL_034, 0x0002), (W_RF_PINS, 0x0046), (W_RF_STATUS, 0x0009), (0x27C, 0x0005)];
    private static readonly (int Port, ushort Value)[] ModeResetFallingPorts = [(0x27C, 0x000A)];
    private static readonly (int Port, ushort Value)[] RXPortResets = [(0x056, 0x0000), (0x0C0, 0x0000), (0x0C4, 0x0000), (0x1A4, 0x0000), (0x278, 0x000F)];

    private static readonly (int Port, ushort Value)[] MACPortResets =
    [
        (0x006, 0x0000), (0x008, 0x0000), (0x00A, 0x0000), (0x018, 0x0000), (0x01A, 0x0000), (0x01C, 0x0000),
        (0x020, 0x0000), (0x022, 0x0000), (0x024, 0x0000), (0x028, 0x0000), (0x02A, 0x0000), (0x02C, 0x0707),
        (0x02E, 0x0000), (0x050, 0x4000), (0x052, 0x4800), (0x084, 0x0000), (0x0BC, 0x0001), (0x0D0, 0x0401),
        (0x0D4, 0x0001), (0x0E0, 0x0008), (0x0EC, 0x3F03), (0x194, 0x0000), (0x198, 0x0000), (0x1A2, 0x0001),
        (0x224, 0x0003), (0x230, 0x0047),
    ];

    private static readonly SystemClock.Event PowerUp = SystemClock.CreateEvent(FinishPowerUp);

    private static ushort _randomLatch;

    public static bool Contains(uint address) => address - RegionStart < RegionEnd - RegionStart;

    public static long Cycles(uint address, int size)
    {
        ushort waitControl = ARM7.IO.WifiWaitControl;
        bool isWS1 = (address & WS1) != 0;
        int control = isWS1 ? waitControl >> 3 : waitControl;
        long nonsequential = FirstAccessTimes[control & 3];
        long sequential = (control & (1 << 2)) != 0 ? 4 : isWS1 ? 10 : 6;
        return (size == 4 ? nonsequential + sequential : nonsequential) * SystemClock.BusCycle;
    }

    public static uint Read32(uint address) => Read16(address) | ((uint)Read16(address + 2) << 16);

    public static ushort Read16(uint address)
    {
        uint offset = address & (WS1 - 2);
        return offset switch
        {
            >= RAMStart and < RAMEnd => (ushort)(RAM[offset - RAMStart] | (RAM[offset - RAMStart + 1] << 8)),
            >= UnusedStart and < RAMStart => Unused,
            _ => ReadRegister((int)(offset & RegisterMask), address),
        };
    }

    public static byte Read8(uint address) => (byte)(Read16(address & ~1u) >> (int)((address & 1) * 8));

    public static void Write32(uint address, uint value)
    {
        Write16(address, (ushort)value);
        Write16(address + 2, (ushort)(value >> 16));
    }

    public static void Write16(uint address, ushort value)
    {
        uint offset = address & (WS1 - 2);
        switch (offset)
        {
            case >= RAMStart and < RAMEnd:
                RAM[offset - RAMStart] = (byte)value;
                RAM[offset - RAMStart + 1] = (byte)(value >> 8);
                break;
            case >= UnusedStart and < RAMStart:
                throw new NotSupportedException($"The ARM7 wrote to unused wifi memory at 0x{address:X8}.");
            default:
                WriteRegister((int)(offset & RegisterMask), value, address);
                break;
        }
    }

    public static void Reset()
    {
        Array.Clear(RAM);
        Array.Clear(Ports);
        foreach (var (port, (_, initial)) in PlainRegisters)
        {
            Ports[port / 2] = initial;
        }

        Ports[W_POWERSTATE / 2] = 0x0200;
        Ports[W_TXREQ_READ / 2] = 0x0010;
        Ports[W_BB_READ / 2] = 0x00B5;
        Ports[W_PREAMBLE / 2] = 0x0001;
        Ports[W_RF_PINS / 2] = 0x0004;
        Ports[W_RF_STATUS / 2] = 0x0009;
        Ports[W_RXTX_ADDR / 2] = 0x0005;
        _randomLatch = RandomSeed;
        PowerUp.Cancel();
        ResetTimers();
        ResetTransmit();
        WifiBaseband.Reset();
        WifiRF.Reset();
    }

    public static void LoadFirmwareSettings(ReadOnlySpan<byte> header)
    {
        WifiBaseband.Load(header.Slice(Firmware.InitialBBValuesOffset, Firmware.BBRegisters));
        WifiRF.Load(header);
        byte rfBits = header[Firmware.RFBitsPerEntry];
        Ports[W_RF_CNT / 2] = (ushort)((rfBits & 0x3F) | ((rfBits & 0x80) << 1));
    }

    private static bool IsOriginalChip => Hardware.Model == Model.DS;

    private static ushort ReadRegister(int port, uint address) => port switch
    {
        W_ID => (ushort)(IsOriginalChip ? 0x1440 : 0xC340),
        W_MODE_RST or W_IF or W_IE or W_RXCNT or W_INTERNAL_034 or W_POWERSTATE or W_POWERFORCE or W_RXBUF_WRCSR or W_TXBUF_REPLY2
            or W_TXREQ_READ or W_TXBUSY or W_TXSTAT or W_BB_READ or W_US_COUNTCNT or W_US_COMPARECNT or W_RF_PINS or W_RF_STATUS or W_TX_SEQNO
            or W_BEACON_COUNT or W_RXSTAT_INC_IF or (>= W_RX_MAC_ADDR_0 and <= W_RX_MAC_ADDR_2) or W_INTERNAL_2B8 => Ports[port / 2],
        W_RXTX_ADDR => ReadTransmitAddress(),
        W_CMD_COUNT => ReadCommandCount(),
        W_RXSTAT_OVF_IF => ReadOverflowFlags(),
        _ when ClearedOnReadPorts.ContainsKey(port) => ReadAndClear(port),
        >= W_US_COMPARE0 and <= W_US_COUNT3 => ReadTimer(port),
        W_PREAMBLE => (ushort)(Ports[port / 2] & PreambleReadable),
        W_BB_BUSY or W_RF_BUSY => 0,
        W_TXREQ_RESET or W_TXREQ_SET => IsOriginalChip ? Ports[W_INTERNAL_09C / 2] : (ushort)0,
        W_TXBUF_RESET when !IsOriginalChip => 0,
        W_BB_CNT => IsOriginalChip ? Ports[W_BB_READ / 2] : (ushort)0,
        W_BB_WRITE => 0,
        W_IF_SET when IsOriginalChip => Ports[W_IF / 2],
        W_CONFIG_254 => (ushort)(IsOriginalChip ? 0xFFFF : 0xEEEE),
        W_X_290 when IsOriginalChip => Unused,
        W_RANDOM => ReadRandom(),
        _ when PlainRegisters.ContainsKey(port) => Ports[port / 2],
        _ => throw Unsupported("read", address),
    };

    private static void WriteRegister(int port, ushort value, uint address)
    {
        switch (port)
        {
            case W_MODE_RST:
                WriteModeReset(value);
                break;
            case W_IF:
                SetInterrupts((ushort)(Ports[W_IF / 2] & ~value), Ports[W_IE / 2]);
                break;
            case W_IE:
                SetInterrupts(Ports[W_IF / 2], value);
                break;
            case W_IF_SET:
                SetInterrupts((ushort)(Ports[W_IF / 2] | (value & NoIRQ10)), Ports[W_IE / 2]);
                break;
            case W_POWERSTATE:
                WritePowerState(value);
                break;
            case W_POWERFORCE:
                WritePowerForce(value);
                break;
            case W_POWER_048:
                Ports[port / 2] = (ushort)(value & PlainRegisters[port].Writable);
                if ((value & WakeUp) != 0)
                {
                    StartPowerUp(SystemClock.CyclesOf(Processor.ARM7));
                }

                break;
            case W_RXCNT:
                WriteReceiveControl(value);
                break;
            case >= W_RX_MAC_ADDR_0 and <= W_RX_MAC_ADDR_2:
                break;
            case W_TXBUF_RESET:
                ResetLocation(value, 0, W_TXBUF_LOC1);
                ResetLocation(value, 1, W_TXBUF_CMD);
                ResetLocation(value, 2, W_TXBUF_LOC2);
                ResetLocation(value, 3, W_TXBUF_LOC3);
                ResetLocation(value, 6, W_TXBUF_REPLY2);
                ResetLocation(value, 7, W_TXBUF_REPLY1);
                break;
            case W_PREAMBLE:
                Ports[W_PREAMBLE / 2] = (ushort)(value & PreambleWritable);
                break;
            case W_US_COUNTCNT or W_US_COMPARECNT or W_POWER_US or (>= W_US_COMPARE0 and <= W_US_COUNT3):
                WriteTimer(port, value);
                break;
            case W_BEACON_COUNT:
                Ports[port / 2] = value;
                break;
            case W_CMD_COUNT or W_CMD_COUNTCNT:
                WriteCommandCount(port, value);
                break;
            case W_CONFIG_254 or W_X_290:
                break;
            case W_TXREQ_RESET:
                Ports[W_TXREQ_READ / 2] &= unchecked((ushort)~(value & TransferRequests));
                break;
            case W_BB_CNT:
                TransferBaseband(value);
                break;
            case W_BB_WRITE:
                Ports[W_BB_WRITE / 2] = value;
                break;
            case W_RF_DATA2:
                ushort data1 = Ports[W_RF_DATA1 / 2];
                WifiRF.Transfer(ref data1, ref value);
                (Ports[W_RF_DATA1 / 2], Ports[W_RF_DATA2 / 2]) = (data1, value);
                break;
            case W_TXREQ_SET:
                if ((value & ~TransferRequests) != 0)
                {
                    throw new NotSupportedException($"The ARM7 wrote 0x{value:X4} to W_TXREQ_SET, which isn't supported.");
                }

                Ports[W_TXREQ_READ / 2] |= value;
                break;
            case var _ when ClearedOnReadPorts.TryGetValue(port, out ushort writable) && writable != 0:
                Ports[port / 2] = (ushort)(value & writable);
                break;
            case var _ when PlainRegisters.TryGetValue(port, out var register):
                Ports[port / 2] = (ushort)(value & register.Writable);
                break;
            default:
                throw Unsupported("write", address);
        }

        UpdateTransmit(SystemClock.CyclesOf(Processor.ARM7));
    }

    private static ushort ReadAndClear(int port)
    {
        ushort value = Ports[port / 2];
        Ports[port / 2] = 0;
        return value;
    }

    private static ushort ReadOverflowFlags()
    {
        ushort flags = 0;
        for (int counter = 0; counter < ReceiveStatisticsBytes.Length; counter++)
        {
            int offset = W_RXSTAT + ReceiveStatisticsBytes[counter];
            int value = Ports[offset / 2] >> ((offset & 1) * 8);
            flags |= (ushort)(((value >> 7) & 1) << counter);
        }

        return flags;
    }

    private static ushort ReadRandom()
    {
        ushort latched = _randomLatch;
        _randomLatch = RandomSequence[SystemClock.CyclesOf(Processor.ARM7) / SystemClock.BusCycle % RandomSequence.Length];
        return latched;
    }

    private static ushort[] CreateRandomSequence()
    {
        var sequence = new List<ushort>();
        for (int value = RandomSeed; sequence.Count == 0 || value != RandomSeed; value = (value & 1) ^ (((value << 1) | (value >> 10)) & 0x7FF))
        {
            sequence.Add((ushort)value);
        }

        return [.. sequence];
    }

    private static void RequestInterrupt(int irq) => SetInterrupts((ushort)(Ports[W_IF / 2] | (1 << irq)), Ports[W_IE / 2]);

    private static void SetInterrupts(ushort flags, ushort enabled)
    {
        bool wasRequesting = (Ports[W_IF / 2] & Ports[W_IE / 2]) != 0;
        (Ports[W_IF / 2], Ports[W_IE / 2]) = (flags, enabled);
        if (!wasRequesting && (flags & enabled) != 0)
        {
            ARM7.IO.Interrupts.Raise(Interrupt.Wifi);
        }
    }

    private static void ResetLocation(ushort value, int bit, int port)
    {
        if ((value & (1 << bit)) != 0)
        {
            Ports[port / 2] &= unchecked((ushort)~LocationEnable);
        }
    }

    private static void TransferBaseband(ushort control)
    {
        int index = control & 0xFF;
        switch (control >> 12)
        {
            case BasebandWrite when (control & 0x0F00) == 0:
                WifiBaseband.Write(index, (byte)Ports[W_BB_WRITE / 2]);
                break;
            case BasebandRead when (control & 0x0F00) == 0:
                Ports[W_BB_READ / 2] = WifiBaseband.Read(index);
                break;
            default:
                throw new NotSupportedException($"The ARM7 wrote 0x{control:X4} to W_BB_CNT, which isn't supported.");
        }
    }

    private static void WriteModeReset(ushort value)
    {
        ushort previous = Ports[W_MODE_RST / 2];
        Ports[W_MODE_RST / 2] = (ushort)(value & ModeResetWritable);
        bool wasEnabled = (previous & TransmitEnable) != 0;
        bool isEnabled = (value & TransmitEnable) != 0;
        if (!wasEnabled && isEnabled)
        {
            Apply(ModeResetRisingPorts);
        }
        else if (wasEnabled && !isEnabled)
        {
            Apply(ModeResetFallingPorts);
        }

        if ((value & ResetRXPorts) != 0)
        {
            Apply(RXPortResets);
        }

        if ((value & ResetMACPorts) != 0)
        {
            Apply(MACPortResets);
        }
    }

    private static void WriteReceiveControl(ushort value)
    {
        Ports[W_RXCNT / 2] = (ushort)(value & ReceiveControlWritable);
        if ((value & LatchWriteCursor) != 0)
        {
            Ports[W_RXBUF_WRCSR / 2] = Ports[W_RXBUF_WR_ADDR / 2];
        }

        if ((value & CopyReply) != 0)
        {
            (Ports[W_TXBUF_REPLY2 / 2], Ports[W_TXBUF_REPLY1 / 2]) = (Ports[W_TXBUF_REPLY1 / 2], 0);
        }
    }

    private static void WritePowerState(ushort value)
    {
        ushort state = Ports[W_POWERSTATE / 2];
        bool isPending = (state & PowerEnablePending) != 0;
        if ((value & ~RequestPowerEnable) != 0 || (isPending && value == 0))
        {
            throw new NotSupportedException($"The ARM7 wrote 0x{value:X4} to W_POWERSTATE, which isn't supported.");
        }

        if (value == 0 || isPending || (state & PowerDisabled) == 0)
        {
            return;
        }

        if ((Ports[W_POWER_US / 2] & USCounterDisabled) != 0 || (Ports[W_MODE_RST / 2] & TransmitEnable) == 0)
        {
            throw new NotSupportedException("The ARM7 asked for wifi power with the wifi hardware off, which isn't supported.");
        }

        Ports[W_POWERSTATE / 2] |= RequestPowerEnable;
        StartPowerUp(SystemClock.CyclesOf(Processor.ARM7));
    }

    private static void StartPowerUp(long now)
    {
        ushort state = Ports[W_POWERSTATE / 2];
        bool isForcedOff = (Ports[W_POWERFORCE / 2] & (ApplyPowerForce | ForcedPowerDisable)) == (ApplyPowerForce | ForcedPowerDisable);
        if ((state & PowerDisabled) == 0 || (state & PowerEnablePending) != 0 || isForcedOff)
        {
            return;
        }

        Ports[W_POWERSTATE / 2] = (ushort)(state | PowerEnablePending);
        RequestInterrupt(RFWakeupIRQ);
        PowerUp.ScheduleAt(now + CyclesFor(PowerUpMicroseconds));
    }

    private static void FinishPowerUp(long when)
    {
        Ports[W_POWERSTATE / 2] = 0;
        (Ports[W_RF_STATUS / 2], Ports[W_RF_PINS / 2]) = (RFReceive, ReceivePins);
        UpdateTransmit(when);
    }

    private static long CyclesFor(long microseconds) => ((microseconds * SystemClock.CyclesPerSecond) + MicrosecondsPerSecond - 1) / MicrosecondsPerSecond;

    private static void WritePowerForce(ushort value)
    {
        Ports[W_POWERFORCE / 2] = (ushort)(value & PowerForceWritable);
        bool isDisabled = (Ports[W_POWERSTATE / 2] & PowerDisabled) != 0;
        bool disables = (value & ForcedPowerDisable) != 0;
        if ((value & ApplyPowerForce) == 0 || disables == isDisabled)
        {
            return;
        }

        if (!disables)
        {
            StartPowerUp(SystemClock.CyclesOf(Processor.ARM7));
            return;
        }

        PowerDown();
        Ports[W_TXREQ_READ / 2] = 0;
    }

    private static void PowerDown()
    {
        PowerUp.Cancel();
        Ports[W_POWERSTATE / 2] = (ushort)((Ports[W_POWERSTATE / 2] & 0xFF) | PowerDisabled);
        Ports[W_INTERNAL_034 / 2] = 0x0002;
        if (_transmitting is null)
        {
            (Ports[W_RF_STATUS / 2], Ports[W_RF_PINS / 2]) = (RFIdle, IdlePins);
        }
    }

    private static void Apply((int Port, ushort Value)[] resets)
    {
        foreach (var (port, value) in resets)
        {
            Ports[port / 2] = value;
        }
    }

    private static NotSupportedException Unsupported(string access, uint address)
    {
        return new NotSupportedException($"The ARM7 tried to {access} the wifi register at 0x{address:X8}, which isn't supported.");
    }
}
