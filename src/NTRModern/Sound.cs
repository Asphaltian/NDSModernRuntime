namespace NTRModern;

/// <summary><c>Sound</c> is the DS's sound. Subscribe to <see cref="SamplesReady"/> and play whatever it gives you.</summary>
public static class Sound
{
    internal const uint FirstRegister = 0x400;
    internal const uint LastRegister = 0x51C;

    private const uint ChannelSize = 0x10;
    private const uint SOUNDCNT = 0x500;
    private const uint SOUNDBIAS = 0x504;
    private const uint SNDCAPCNT = 0x508;
    private const uint SNDCAP0DAD = 0x510;
    private const uint SNDCAP0LEN = 0x514;
    private const uint SNDCAP1DAD = 0x518;
    private const uint SNDCAP1LEN = 0x51C;
    private const ushort ControlWritable = 0xBF7F;
    private const ushort BiasWritable = 0x3FF;
    private const uint CaptureControlWritable = 0x8F8F;
    private const uint CaptureStart = (1 << 7) | (1 << 15);
    private const uint CaptureAddressMask = 0x07FFFFFC;
    private const ushort MasterEnable = 1 << 15;
    private const ushort Channel1NotToMixer = 1 << 12;
    private const ushort Channel3NotToMixer = 1 << 13;
    private const int OutputRate = 32768;
    private const int BufferedSamples = 512;

    private static readonly Channel[] Channels = [.. Enumerable.Range(0, 16).Select(index => new Channel(index))];
    private static readonly short[] Buffer = new short[2 * BufferedSamples];
    private static readonly uint[] CaptureAddress = new uint[2];
    private static readonly ushort[] CaptureLength = new ushort[2];
    private static readonly SystemClock.Event CatchUpEvent = SystemClock.CreateEvent(CatchUp);

    private static ushort _control;
    private static ushort _bias;
    private static uint _captureControl;
    private static long _secondStartedAt;
    private static int _samplesThisSecond;
    private static int _buffered;

    /// <summary>How many samples the DS makes in one second.</summary>
    public static int SampleRate => OutputRate;

    /// <summary>
    /// Gives you interleaved stereo samples along with their sample rate. Keep in mind that this is called on the
    /// game's thread, and the samples are only yours until you return, so copy them if you need them later.
    /// </summary>
    public static event Action<ReadOnlySpan<short>, int>? SamplesReady;

    internal static uint? Read(uint word)
    {
        Update(Now);
        if (word < SOUNDCNT)
        {
            var channel = Channels[(word - FirstRegister) / ChannelSize];
            return (word - FirstRegister) % ChannelSize == 0 ? channel.ReadControl() : null;
        }

        return word switch
        {
            SOUNDCNT => _control,
            SOUNDBIAS => _bias,
            SNDCAPCNT => _captureControl,
            SNDCAP0DAD => CaptureAddress[0],
            SNDCAP1DAD => CaptureAddress[1],
            _ => null,
        };
    }

    internal static bool Write(uint word, uint value, uint mask)
    {
        Update(Now);
        if (word < SOUNDCNT)
        {
            var channel = Channels[(word - FirstRegister) / ChannelSize];
            switch ((word - FirstRegister) % ChannelSize)
            {
                case 0x0:
                    channel.WriteControl(IORegisters.Merge(channel.ReadControl(), value, mask));
                    break;
                case 0x4:
                    channel.Source = IORegisters.Merge(channel.Source, value, mask) & Channel.SourceMask;
                    break;
                case 0x8:
                    uint timers = IORegisters.Merge(channel.Timer | ((uint)channel.LoopStart << 16), value, mask);
                    (channel.Timer, channel.LoopStart) = ((ushort)timers, (ushort)(timers >> 16));
                    break;
                default:
                    channel.Length = IORegisters.Merge(channel.Length, value, mask) & Channel.LengthMask;
                    break;
            }

            return true;
        }

        switch (word)
        {
            case SOUNDCNT:
                _control = (ushort)(IORegisters.Merge(_control, value, mask) & ControlWritable);
                return true;
            case SOUNDBIAS:
                _bias = (ushort)(IORegisters.Merge(_bias, value, mask) & BiasWritable);
                return true;
            case SNDCAPCNT:
                uint captureControl = IORegisters.Merge(_captureControl, value, mask) & CaptureControlWritable;
                if ((captureControl & CaptureStart) != 0)
                {
                    throw new NotSupportedException($"The ARM7 started sound capture with SNDCAPCNT 0x{captureControl:X4}, which isn't supported.");
                }

                _captureControl = captureControl;
                return true;
            case SNDCAP0DAD or SNDCAP1DAD:
                int capture = word == SNDCAP0DAD ? 0 : 1;
                CaptureAddress[capture] = IORegisters.Merge(CaptureAddress[capture], value, mask) & CaptureAddressMask;
                return true;
            case SNDCAP0LEN or SNDCAP1LEN:
                int length = word == SNDCAP0LEN ? 0 : 1;
                CaptureLength[length] = (ushort)IORegisters.Merge(CaptureLength[length], value, mask);
                return true;
            default:
                return false;
        }
    }

    internal static void Start()
    {
        long now = Now;
        (_secondStartedAt, _samplesThisSecond) = (now, 0);
        foreach (var channel in Channels)
        {
            channel.AdvanceTo(now);
        }

        CatchUpEvent.ScheduleAt(now + CatchUpCycles);
    }

    internal static void Reset()
    {
        foreach (var channel in Channels)
        {
            channel.Reset();
        }

        Array.Clear(CaptureAddress);
        Array.Clear(CaptureLength);
        (_control, _bias, _captureControl, _secondStartedAt, _samplesThisSecond, _buffered) = (0, 0, 0, 0, 0, 0);
    }

    private static long Now => SystemClock.CyclesOf(Processor.ARM7);

    private static long CatchUpCycles => BufferedSamples * SystemClock.CyclesPerSecond / OutputRate;

    private static long NextSampleAt => _secondStartedAt + ((_samplesThisSecond + 1) * SystemClock.CyclesPerSecond / OutputRate);

    private static void CatchUp(long dueAt)
    {
        Update(Now);
        CatchUpEvent.ScheduleAt(dueAt + CatchUpCycles);
    }

    private static void Update(long now)
    {
        for (long sampleAt = NextSampleAt; sampleAt <= now; sampleAt = NextSampleAt)
        {
            foreach (var channel in Channels)
            {
                channel.AdvanceTo(sampleAt);
            }

            Buffer[_buffered++] = Mix(isLeft: true);
            Buffer[_buffered++] = Mix(isLeft: false);
            if (_buffered == Buffer.Length)
            {
                SamplesReady?.Invoke(Buffer, SampleRate);
                _buffered = 0;
            }

            if (++_samplesThisSecond == OutputRate)
            {
                (_secondStartedAt, _samplesThisSecond) = (_secondStartedAt + SystemClock.CyclesPerSecond, 0);
            }
        }

        foreach (var channel in Channels)
        {
            channel.AdvanceTo(now);
        }
    }

    private static short Mix(bool isLeft)
    {
        if ((_control & MasterEnable) == 0)
        {
            return 0;
        }

        long channel1 = Channels[1].Output(isLeft);
        long channel3 = Channels[3].Output(isLeft);
        long mixer = ((_control & Channel1NotToMixer) != 0 ? 0 : channel1) + ((_control & Channel3NotToMixer) != 0 ? 0 : channel3);
        for (int index = 0; index < Channels.Length; index++)
        {
            if (index is not (1 or 3))
            {
                mixer += Channels[index].Output(isLeft);
            }
        }

        long selected = ((_control >> (isLeft ? 8 : 10)) & 3) switch
        {
            0 => mixer,
            1 => channel1,
            2 => channel3,
            _ => channel1 + channel3,
        };

        int output = Math.Clamp((int)((selected * Factor(_control & 0x7Fu)) >> 21) + _bias, 0, 0x3FF);
        return (short)Math.Clamp((output - _bias) << 6, short.MinValue, short.MaxValue);
    }

    private static int Factor(uint setting) => setting == 127 ? 128 : (int)setting;

    private sealed class Channel(int index)
    {
        public const uint SourceMask = 0x07FFFFFC;
        public const uint LengthMask = 0x3FFFFF;

        private const uint Writable = 0x7F7F837F;
        private const uint Busy = 1u << 31;
        private const uint Hold = 1 << 15;
        private const long CyclesPerTick = 2 * SystemClock.BusCycle;
        private const int Loop = 1;
        private const int OneShot = 2;
        private const int PCM8 = 0;
        private const int PCM16 = 1;
        private const int ADPCM = 2;
        private const int PSG = 3;
        private const int MinimumLength = 4;
        private const int High = 0x7FFF;
        private const int Low = -0x7FFF;

        private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8];
        private static readonly int[] ADPCMTable =
        [
            0x0007, 0x0008, 0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x000E, 0x0010, 0x0011, 0x0013, 0x0015,
            0x0017, 0x0019, 0x001C, 0x001F, 0x0022, 0x0025, 0x0029, 0x002D, 0x0032, 0x0037, 0x003C, 0x0042,
            0x0049, 0x0050, 0x0058, 0x0061, 0x006B, 0x0076, 0x0082, 0x008F, 0x009D, 0x00AD, 0x00BE, 0x00D1,
            0x00E6, 0x00FD, 0x0117, 0x0133, 0x0151, 0x0173, 0x0198, 0x01C1, 0x01EE, 0x0220, 0x0256, 0x0292,
            0x02D4, 0x031C, 0x036C, 0x03C3, 0x0424, 0x048E, 0x0502, 0x0583, 0x0610, 0x06AB, 0x0756, 0x0812,
            0x08E0, 0x09C3, 0x0ABD, 0x0BD0, 0x0CFF, 0x0E4C, 0x0FBA, 0x114C, 0x1307, 0x14EE, 0x1706, 0x1954,
            0x1BDC, 0x1EA5, 0x21B6, 0x2515, 0x28CA, 0x2CDF, 0x315B, 0x364B, 0x3BB9, 0x41B2, 0x4844, 0x4F7E,
            0x5771, 0x602F, 0x69CE, 0x7462, 0x7FFF, .. new int[39],
        ];

        private uint _control;
        private bool _isBusy;
        private bool _isPlaying;
        private long _updatedAt;
        private int _counter;
        private int _dummySamples;
        private int _position;
        private int _sample;
        private int _adpcmValue;
        private int _adpcmIndex;
        private bool _isLoopSaved;
        private int _loopValue;
        private int _loopIndex;
        private int _noise;

        public uint Source { get; set; }

        public ushort Timer { get; set; }

        public ushort LoopStart { get; set; }

        public uint Length { get; set; }

        private int Format => (int)(_control >> 29) & 3;

        private int RepeatMode => (int)(_control >> 27) & 3;

        public uint ReadControl() => _control | (_isBusy ? Busy : 0);

        public void WriteControl(uint value)
        {
            bool starts = !_isBusy && (value & Busy) != 0;
            bool stops = _isBusy && (value & Busy) == 0;
            _control = value & Writable;
            if (starts)
            {
                Start();
            }
            else if (stops)
            {
                (_isBusy, _isPlaying, _sample) = (false, false, 0);
            }
            else if (!_isPlaying && (_control & Hold) == 0)
            {
                _sample = 0;
            }
        }

        public long Output(bool isLeft)
        {
            int divider = (int)(_control >> 8) & 3;
            long value = ((long)_sample << 4) >> (divider == 3 ? 4 : divider);
            value *= Factor(_control & 0x7F);
            int panning = Factor((_control >> 16) & 0x7F);
            value *= isLeft ? 128 - panning : panning;
            return value >> 10;
        }

        public void AdvanceTo(long now)
        {
            long ticks = (now / CyclesPerTick) - (_updatedAt / CyclesPerTick);
            _updatedAt = Math.Max(_updatedAt, now);
            while (_isPlaying && ticks > 0)
            {
                int untilOverflow = 0x10000 - _counter;
                if (ticks < untilOverflow)
                {
                    _counter += (int)ticks;
                    return;
                }

                ticks -= untilOverflow;
                _counter = Timer;
                Step();
            }
        }

        public void Reset()
        {
            (_control, Source, Timer, LoopStart, Length) = (0, 0, 0, 0, 0);
            (_isBusy, _isPlaying, _updatedAt, _counter, _dummySamples, _position, _sample) = (false, false, 0, 0, 0, 0, 0);
            (_adpcmValue, _adpcmIndex, _isLoopSaved, _loopValue, _loopIndex, _noise) = (0, 0, false, 0, 0, 0);
        }

        private void Start()
        {
            if (Format == PSG && index < 8)
            {
                throw new NotSupportedException($"The ARM7 started sound channel {index} as PSG, which only channels 8 to 15 have.");
            }

            if (Format != PSG && RepeatMode is not (Loop or OneShot))
            {
                throw new NotSupportedException($"The ARM7 started sound channel {index} in repeat mode {RepeatMode}, which isn't supported.");
            }

            if (Format != PSG && RepeatMode == Loop && Length == 0)
            {
                throw new NotSupportedException($"The ARM7 looped sound channel {index} with nothing to loop.");
            }

            if (Format == ADPCM && RepeatMode == Loop && LoopStart == 0)
            {
                throw new NotSupportedException($"The ARM7 looped ADPCM sound channel {index} from its header, which isn't supported.");
            }

            if (Format is ADPCM or PSG && (_control & Hold) != 0 && _sample != 0)
            {
                throw new NotSupportedException($"The ARM7 restarted sound channel {index} while it held its last sample, which isn't supported.");
            }

            _isBusy = true;
            _isPlaying = Format == PSG || LoopStart + Length >= MinimumLength;
            _counter = Timer;
            _position = 0;
            _dummySamples = Format switch
            {
                PSG => 1,
                ADPCM => 11,
                _ => 3,
            };

            if ((_control & Hold) == 0 || !_isPlaying)
            {
                _sample = 0;
            }

            if (Format == ADPCM)
            {
                uint header = Peek(Source, 4);
                (_adpcmValue, _adpcmIndex, _isLoopSaved) = ((short)header, (int)(header >> 16) & 0x7F, false);
            }

            _noise = 0x7FFF;
        }

        private void Step()
        {
            if (_dummySamples > 0)
            {
                _dummySamples--;
                if (_dummySamples > 0)
                {
                    _sample = 0;
                    return;
                }
            }
            else
            {
                _position++;
            }

            if (Format == PSG)
            {
                _sample = index < 14 ? Duty() : Noise();
                return;
            }

            int end = SampleCount(LoopStart + (int)Length);
            if (_position == end)
            {
                if (RepeatMode == OneShot)
                {
                    _isPlaying = false;
                    if ((_control & Hold) == 0)
                    {
                        _sample = 0;
                    }

                    return;
                }

                _position = SampleCount(LoopStart);
                if (Format == ADPCM)
                {
                    (_adpcmValue, _adpcmIndex) = (_loopValue, _loopIndex);
                }
            }

            if (RepeatMode == OneShot && _position == end - 1)
            {
                _isBusy = false;
            }

            _sample = Format switch
            {
                PCM8 => (sbyte)Peek(Source + (uint)_position, 1) << 8,
                PCM16 => (short)Peek(Source + ((uint)_position * 2), 2),
                _ => DecodeADPCM(),
            };
        }

        private int SampleCount(int words) => Format switch
        {
            PCM8 => words * 4,
            PCM16 => words * 2,
            _ => (words - 1) * 8,
        };

        private int Duty()
        {
            int duty = (int)(_control >> 24) & 7;
            return duty != 7 && _position % 8 >= 7 - duty ? High : Low;
        }

        private int Noise()
        {
            bool carry = (_noise & 1) != 0;
            _noise >>= 1;
            if (!carry)
            {
                return High;
            }

            _noise ^= 0x6000;
            return Low;
        }

        private int DecodeADPCM()
        {
            if (RepeatMode == Loop && !_isLoopSaved && _position == SampleCount(LoopStart))
            {
                (_loopValue, _loopIndex, _isLoopSaved) = (_adpcmValue, _adpcmIndex, true);
            }

            int data = (int)(Peek(Source + 4 + ((uint)_position / 2), 1) >> ((_position & 1) * 4)) & 0xF;
            int step = ADPCMTable[_adpcmIndex];
            int difference = step / 8;
            if ((data & 1) != 0)
            {
                difference += step / 4;
            }

            if ((data & 2) != 0)
            {
                difference += step / 2;
            }

            if ((data & 4) != 0)
            {
                difference += step;
            }

            _adpcmValue = (data & 8) == 0 ? Math.Min(_adpcmValue + difference, High) : Math.Max(_adpcmValue - difference, Low);
            _adpcmIndex = Math.Clamp(_adpcmIndex + IndexTable[data & 7], 0, 88);
            return _adpcmValue;
        }

        private static uint Peek(uint address, int size)
        {
            uint word = ARM7.Memory.Peek32(address & ~3u) >> (int)((address & 3) * 8);
            return size == 4 ? word : word & ((1u << (size * 8)) - 1);
        }
    }
}
