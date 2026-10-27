namespace NTRModern;

internal sealed class Timers
{
    private const ushort CountUp = 1 << 2;
    private const ushort IRQEnable = 1 << 6;
    private const ushort Start = 1 << 7;

    private readonly Processor _processor;
    private readonly Interrupts _interrupts;
    private readonly Timer[] _all;

    public Timers(Processor processor, Interrupts interrupts)
    {
        _processor = processor;
        _interrupts = interrupts;
        _all = [new(this, 0), new(this, 1), new(this, 2), new(this, 3)];
    }

    public ushort ReadCounter(int timer) => _all[timer].Counter;

    public ushort ReadControl(int timer) => _all[timer].Control;

    public ushort ReadReload(int timer) => _all[timer].Reload;

    public void WriteReload(int timer, ushort value) => _all[timer].Reload = value;

    public void WriteControl(int timer, ushort value) => _all[timer].WriteControl(value);

    public void Reset()
    {
        foreach (var timer in _all)
        {
            timer.Reset();
        }
    }

    private long Now => SystemClock.CyclesOf(_processor);

    private sealed class Timer
    {
        private readonly Timers _timers;
        private readonly int _index;
        private readonly SystemClock.Event _overflow;

        private ushort _counter;
        private long _zeroAt;

        public Timer(Timers timers, int index)
        {
            _timers = timers;
            _index = index;
            _overflow = SystemClock.CreateEvent(Overflow);
        }

        public ushort Reload { get; set; }

        public ushort Control { get; private set; }

        public ushort Counter => IsClocked ? (ushort)((_timers.Now - _zeroAt) / CyclesPerTick) : _counter;

        private bool IsRunning => (Control & Start) != 0;

        private bool IsClocked => IsRunning && (Control & CountUp) == 0;

        private long CyclesPerTick => SystemClock.BusCycle << ((Control & 3) switch
        {
            0 => 0,
            1 => 6,
            2 => 8,
            _ => 10,
        });

        public void WriteControl(ushort value)
        {
            ushort newControl = (ushort)(value & (_index == 0 ? 0x00C3 : 0x00C7));
            bool starts = !IsRunning && (newControl & Start) != 0;

            _counter = starts ? Reload : Counter;
            Control = newControl;

            if (IsClocked)
            {
                RunFrom(_counter, _timers.Now);
            }
            else
            {
                _overflow.Cancel();
            }
        }

        public void Reset()
        {
            (Reload, Control, _counter) = (0, 0, 0);
            _overflow.Cancel();
        }

        private void RunFrom(ushort counter, long startedAt)
        {
            _zeroAt = startedAt - (counter * CyclesPerTick);
            _overflow.ScheduleAt(_zeroAt + (0x10000 * CyclesPerTick));
        }

        private void Overflow(long overflowedAt)
        {
            if (IsClocked)
            {
                RunFrom(Reload, overflowedAt);
            }
            else
            {
                _counter = Reload;
            }

            if ((Control & IRQEnable) != 0)
            {
                _timers._interrupts.Raise((Interrupt)((uint)Interrupt.Timer0 << _index));
            }

            if (_index < 3)
            {
                _timers._all[_index + 1].CountOverflowBelow(overflowedAt);
            }
        }

        private void CountOverflowBelow(long overflowedAt)
        {
            if (IsRunning && (Control & CountUp) != 0 && ++_counter == 0)
            {
                Overflow(overflowedAt);
            }
        }
    }
}
