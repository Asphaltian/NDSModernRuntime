namespace NTRModern;

internal static partial class Wifi
{
    private const int W_LISTENCOUNT = 0x088;
    private const int W_BEACONINT = 0x08C;
    private const int W_LISTENINT = 0x08E;
    private const int W_CMD_COUNTCNT = 0x0EE;
    private const int W_US_COMPARE0 = 0x0F0;
    private const int W_US_COUNT0 = 0x0F8;
    private const int W_US_COUNT3 = 0x0FE;
    private const int W_PRE_BEACON = 0x110;
    private const int W_CMD_COUNT = 0x118;
    private const int W_BEACON_COUNT = 0x11C;
    private const int W_POST_BEACON = 0x134;

    private const long MicrosecondsPerSecond = 1_000_000;
    private const int MillisecondShift = 10;
    private const ulong MillisecondMask = (1 << MillisecondShift) - 1;
    private const ulong CompareReset = 0xFFFFFFFFFFFFFC00;
    private const long MicrosecondsPerCommandCount = 10;
    private const ushort CounterEnable = 1 << 0;
    private const ushort CompareEnable = 1 << 0;
    private const ushort ForceIRQ14 = 1 << 1;
    private const ushort BlockBeaconIRQ14 = 1 << 0;
    private const ushort USCounterDisabled = 1 << 0;
    private const ushort AutoWakeup = 1 << 0;
    private const ushort NoAutoSleep = 1 << 1;
    private const ushort BeaconIRQsKeepRequests = 0xFFF2;
    private const int PostBeaconIRQ = 13;
    private const int BeaconIRQ = 14;
    private const int PreBeaconIRQ = 15;

    private static readonly SystemClock.Event MillisecondTick = SystemClock.CreateEvent(OnMillisecondTick);
    private static readonly SystemClock.Event PreBeaconTime = SystemClock.CreateEvent(RequestPreBeacon);

    private static ulong _countOrigin;
    private static long _cycleOrigin;
    private static ulong _compare;
    private static bool _isBeaconIRQ14Blocked;
    private static long _commandCountSince;

    private static bool IsCounting => (Ports[W_US_COUNTCNT / 2] & CounterEnable) != 0 && (Ports[W_POWER_US / 2] & USCounterDisabled) == 0;

    private static bool IsCompareEnabled => (Ports[W_US_COMPARECNT / 2] & CompareEnable) != 0;

    private static void ResetTimers()
    {
        (_countOrigin, _cycleOrigin, _compare, _isBeaconIRQ14Blocked) = (0, SystemClock.CyclesOf(Processor.ARM7), CompareReset, false);
        _commandCountSince = SystemClock.CyclesOf(Processor.ARM7);
        MillisecondTick.Cancel();
        PreBeaconTime.Cancel();
    }

    private static ushort ReadTimer(int port) => port switch
    {
        >= W_US_COUNT0 => (ushort)(Count() >> ((port - W_US_COUNT0) * 8)),
        _ => (ushort)(_compare >> ((port - W_US_COMPARE0) * 8)),
    };

    private static void WriteTimer(int port, ushort value)
    {
        Settle();
        switch (port)
        {
            case W_US_COUNTCNT:
                Ports[port / 2] = (ushort)(value & CounterEnable);
                break;
            case W_US_COMPARECNT:
                Ports[port / 2] = (ushort)(value & CompareEnable);
                if ((value & ForceIRQ14) != 0)
                {
                    StartBeaconIRQ14(isForced: true);
                }

                break;
            case W_POWER_US:
                Ports[port / 2] = (ushort)(value & PlainRegisters[W_POWER_US].Writable);
                break;
            case >= W_US_COUNT0:
                _countOrigin = Replace(_countOrigin, port - W_US_COUNT0, value);
                break;
            case W_US_COMPARE0:
                _compare = Replace(_compare, 0, value) & ~MillisecondMask;
                _isBeaconIRQ14Blocked = (value & BlockBeaconIRQ14) != 0;
                break;
            default:
                _compare = Replace(_compare, port - W_US_COMPARE0, value);
                break;
        }

        ScheduleTimers();
    }

    private static ushort ReadCommandCount()
    {
        ushort count = Ports[W_CMD_COUNT / 2];
        if ((Ports[W_CMD_COUNTCNT / 2] & CounterEnable) == 0)
        {
            return count;
        }

        long steps = (SystemClock.CyclesOf(Processor.ARM7) - _commandCountSince) * MicrosecondsPerSecond / SystemClock.CyclesPerSecond / MicrosecondsPerCommandCount;
        return (ushort)Math.Max(count - steps, 0);
    }

    private static void WriteCommandCount(int port, ushort value)
    {
        Ports[W_CMD_COUNT / 2] = ReadCommandCount();
        _commandCountSince = SystemClock.CyclesOf(Processor.ARM7);
        Ports[port / 2] = port == W_CMD_COUNT ? value : (ushort)(value & PlainRegisters[port].Writable);
    }

    private static ulong Replace(ulong value, int byteOffset, ushort part)
    {
        int shift = byteOffset * 8;
        return (value & ~(0xFFFFUL << shift)) | ((ulong)part << shift);
    }

    private static ulong Count()
    {
        if (!IsCounting)
        {
            return _countOrigin;
        }

        long elapsed = SystemClock.CyclesOf(Processor.ARM7) - _cycleOrigin;
        return _countOrigin + (ulong)((Int128)elapsed * MicrosecondsPerSecond / SystemClock.CyclesPerSecond);
    }

    private static long CycleOf(ulong count)
    {
        Int128 cycles = ((Int128)(count - _countOrigin) * SystemClock.CyclesPerSecond) + MicrosecondsPerSecond - 1;
        return _cycleOrigin + (long)(cycles / MicrosecondsPerSecond);
    }

    private static void Settle()
    {
        ulong count = Count();
        _cycleOrigin = IsCounting ? CycleOf(count) : SystemClock.CyclesOf(Processor.ARM7);
        _countOrigin = count;
    }

    private static void ScheduleTimers()
    {
        if (!IsCounting)
        {
            MillisecondTick.Cancel();
            PreBeaconTime.Cancel();
            return;
        }

        MillisecondTick.ScheduleAt(CycleOf((_countOrigin | MillisecondMask) + 1));
    }

    private static void OnMillisecondTick(long when)
    {
        ulong tick = (_countOrigin | MillisecondMask) + 1;
        (_countOrigin, _cycleOrigin) = (tick, when);

        ushort postBeacon = Ports[W_POST_BEACON / 2];
        if (postBeacon != 0)
        {
            Ports[W_POST_BEACON / 2] = --postBeacon;
            if (postBeacon == 0)
            {
                RequestPostBeacon();
            }
        }

        CountBeacon();
        if (tick >> MillisecondShift == _compare >> MillisecondShift)
        {
            _isBeaconIRQ14Blocked = false;
            StartBeaconIRQ14(isForced: false);
        }

        UpdateTransmit(when);
        ScheduleTimers();
    }

    private static void CountBeacon()
    {
        ushort beaconCount = Ports[W_BEACON_COUNT / 2];
        ushort interval = Ports[W_BEACONINT / 2];
        if (beaconCount == 0)
        {
            Ports[W_BEACON_COUNT / 2] = interval;
            return;
        }

        Ports[W_BEACON_COUNT / 2] = --beaconCount;
        ushort preBeacon = Ports[W_PRE_BEACON / 2];
        if (beaconCount == 0)
        {
            if (preBeacon == 0)
            {
                RequestPreBeacon(_cycleOrigin);
            }

            if (!_isBeaconIRQ14Blocked)
            {
                StartBeaconIRQ14(isForced: false);
            }

            Ports[W_BEACON_COUNT / 2] = interval;
            return;
        }

        ulong beaconAt = _countOrigin + ((ulong)beaconCount << MillisecondShift);
        ulong preBeaconAt = beaconAt - preBeacon;
        if (preBeacon != 0 && preBeaconAt >= _countOrigin && preBeaconAt <= (_countOrigin | MillisecondMask))
        {
            PreBeaconTime.ScheduleAt(CycleOf(preBeaconAt));
        }
    }

    private static void StartBeaconIRQ14(bool isForced)
    {
        if (!isForced)
        {
            Ports[W_BEACON_COUNT / 2] = Ports[W_BEACONINT / 2];
        }

        if (!IsCompareEnabled)
        {
            return;
        }

        _isBeaconDue |= (Ports[W_TXBUF_BEACON / 2] & LocationEnable) != 0;
        Ports[W_POST_BEACON / 2] = 0xFFFF;
        Ports[W_TXREQ_READ / 2] &= BeaconIRQsKeepRequests;
        byte listenCount = (byte)Ports[W_LISTENCOUNT / 2];
        if (listenCount == 0)
        {
            listenCount = (byte)Ports[W_LISTENINT / 2];
        }

        Ports[W_LISTENCOUNT / 2] = (byte)(listenCount - 1);
        RequestInterrupt(BeaconIRQ);
    }

    private static void RequestPreBeacon(long when)
    {
        if (IsCompareEnabled)
        {
            RequestInterrupt(PreBeaconIRQ);
        }

        if ((Ports[W_POWER_TX / 2] & AutoWakeup) != 0)
        {
            StartPowerUp(when);
        }
    }

    private static void RequestPostBeacon()
    {
        RequestInterrupt(PostBeaconIRQ);
        if ((Ports[W_POWER_TX / 2] & NoAutoSleep) == 0)
        {
            PowerDown();
        }
    }
}
