namespace NTRModern;

internal sealed class DMA
{
    public const uint FirstRegister = 0xB0;
    public const uint LastRegister = 0xDC;

    private const uint ChannelSize = 12;
    private const uint Repeat = 1u << 25;
    private const uint Is32Bit = 1u << 26;
    private const uint IRQEnable = 1u << 30;
    private const uint Enable = 1u << 31;
    private const int NoChannel = 4;

    private readonly Processor _processor;
    private readonly Interrupts _interrupts;
    private readonly Channel[] _channels;

    private int _transferring = NoChannel;

    public DMA(Processor processor, Interrupts interrupts)
    {
        _processor = processor;
        _interrupts = interrupts;
        _channels = [new(this, 0), new(this, 1), new(this, 2), new(this, 3)];
    }

    private enum Timing
    {
        Immediate,
        VBlank,
        HBlank,
        Card,
    }

    public uint Read(uint word)
    {
        var channel = _channels[(word - FirstRegister) / ChannelSize];
        return ((word - FirstRegister) % ChannelSize) switch
        {
            0 => channel.SourceRegister,
            4 => channel.DestinationRegister,
            _ => channel.Control,
        };
    }

    public void Write(uint word, uint value)
    {
        var channel = _channels[(word - FirstRegister) / ChannelSize];
        switch ((word - FirstRegister) % ChannelSize)
        {
            case 0:
                channel.SourceRegister = value & channel.SourceMask;
                break;
            case 4:
                channel.DestinationRegister = value & channel.DestinationMask;
                break;
            default:
                channel.WriteControl(value);
                break;
        }
    }

    public void Reset()
    {
        foreach (var channel in _channels)
        {
            channel.Reset();
        }

        _transferring = NoChannel;
    }

    public void OnVBlank() => Run(Timing.VBlank);

    public void OnHBlank() => Run(Timing.HBlank);

    public void OnCardWordReady() => Run(Timing.Card);

    private void Run(Timing timing)
    {
        foreach (var channel in _channels)
        {
            if (channel.IsWaitingFor(timing))
            {
                Start(channel);
            }
        }
    }

    private void Start(Channel channel)
    {
        if (channel.Index >= _transferring)
        {
            channel.IsRequested = true;
            return;
        }

        int paused = _transferring;
        _transferring = channel.Index;
        channel.Transfer();
        _transferring = paused;

        foreach (var waiting in _channels)
        {
            if (waiting.IsRequested && waiting.Index < _transferring)
            {
                waiting.IsRequested = false;
                if (waiting.IsEnabled)
                {
                    Start(waiting);
                }
            }
        }
    }

    private sealed class Channel(DMA dma, int index)
    {
        private uint _source;
        private uint _destination;
        private uint _count;

        public int Index => index;

        public bool IsRequested { get; set; }

        public uint SourceRegister { get; set; }

        public uint DestinationRegister { get; set; }

        public uint Control { get; private set; }

        public bool IsEnabled => (Control & Enable) != 0;

        public uint SourceMask => IsARM9 ? 0x0FFFFFFE : index == 0 ? 0x07FFFFFFu : 0x0FFFFFFFu;

        public uint DestinationMask => IsARM9 ? 0x0FFFFFFE : index == 3 ? 0x0FFFFFFFu : 0x07FFFFFFu;

        private bool IsARM9 => dma._processor == Processor.ARM9;

        private uint CountMask => IsARM9 ? 0x1FFFFF : index == 3 ? 0xFFFFu : 0x3FFFu;

        private uint ControlMask => IsARM9 ? 0xFFE00000 : 0xF7E00000;

        private int StartMode => IsARM9 ? (int)((Control >> 27) & 7) : (int)((Control >> 28) & 3);

        public bool IsWaitingFor(Timing timing) => IsEnabled && StartMode == ModeOf(timing);

        public void WriteControl(uint value)
        {
            bool wasEnabled = IsEnabled;
            Control = value & (ControlMask | CountMask);
            if (wasEnabled || !IsEnabled)
            {
                return;
            }

            if (!Enum.GetValues<Timing>().Any(timing => ModeOf(timing) == StartMode))
            {
                throw new NotSupportedException($"The {dma._processor} started DMA {index} in start mode {StartMode}, which isn't supported.");
            }

            if (IsWaitingFor(Timing.Immediate) && (Control & Repeat) != 0)
            {
                throw new NotSupportedException($"The {dma._processor} started DMA {index} immediately with repeat on, which isn't supported.");
            }

            _source = SourceRegister;
            _destination = DestinationRegister;
            ReloadCount();

            if (IsWaitingFor(Timing.Immediate))
            {
                dma.Start(this);
            }
        }

        public void Transfer()
        {
            uint unit = (Control & Is32Bit) != 0 ? 4u : 2u;
            uint sourceStep = Step((int)(Control >> 23) & 3, unit);
            uint destinationStep = Step((int)(Control >> 21) & 3, unit);
            bool isSequential = _source >> 24 != 0x02 || _destination >> 24 != 0x02;

            ref long cycles = ref SystemClock.CyclesOf(dma._processor);
            for (uint count = _count; count > 0; count--)
            {
                if (!IsUndefined(_source) || !IsUndefined(_destination))
                {
                    Copy(unit);
                }

                if (isSequential && count != _count)
                {
                    cycles -= SequentialSaving(_source) + SequentialSaving(_destination);
                }

                _source += sourceStep;
                _destination += destinationStep;
            }

            Finish();
        }

        public void Reset()
        {
            (SourceRegister, DestinationRegister, Control) = (0, 0, 0);
            (_source, _destination, _count) = (0, 0, 0);
            IsRequested = false;
        }

        private void Finish()
        {
            if ((Control & IRQEnable) != 0)
            {
                dma._interrupts.Raise((Interrupt)((uint)Interrupt.DMA0 << index));
            }

            if ((Control & Repeat) == 0)
            {
                Control &= ~Enable;
                return;
            }

            ReloadCount();
            if (((Control >> 21) & 3) == 3)
            {
                _destination = DestinationRegister;
            }
        }

        private void ReloadCount()
        {
            uint count = Control & CountMask;
            _count = count == 0 ? CountMask + 1 : count;
        }

        private bool IsUndefined(uint address) => IsARM9 && address >> 24 < 0x02;

        private void Copy(uint unit)
        {
            if (unit == 4)
            {
                Write32(_destination, Read32(_source));
            }
            else
            {
                Write16(_destination, Read16(_source));
            }
        }

        private uint Read32(uint address) => IsARM9 ? ARM9.Memory.ReadBus32(address) : ARM7.Memory.Read32(address);

        private ushort Read16(uint address) => IsARM9 ? ARM9.Memory.ReadBus16(address) : ARM7.Memory.Read16(address);

        private void Write32(uint address, uint value)
        {
            if (IsARM9)
            {
                ARM9.Memory.WriteBus32(address, value);
            }
            else
            {
                ARM7.Memory.Write32(address, value);
            }
        }

        private void Write16(uint address, ushort value)
        {
            if (IsARM9)
            {
                ARM9.Memory.WriteBus16(address, value);
            }
            else
            {
                ARM7.Memory.Write16(address, value);
            }
        }

        private long SequentialSaving(uint address) => IsARM9 ? ARM9.Memory.SequentialBusSaving(address) : ARM7.Memory.SequentialSaving(address);

        private int? ModeOf(Timing timing) => (IsARM9, timing) switch
        {
            (_, Timing.Immediate) => 0,
            (_, Timing.VBlank) => 1,
            (true, Timing.HBlank) => 2,
            (true, Timing.Card) => 5,
            (false, Timing.Card) => 2,
            _ => null,
        };

        private static uint Step(int addressControl, uint unit) => addressControl switch
        {
            1 => unchecked(0u - unit),
            2 => 0,
            _ => unit,
        };
    }
}
