namespace NTRModern.Tests;

public class SoundTests
{
    private const uint SOUNDCNT = 0x04000500;
    private const uint SOUNDBIAS = 0x04000504;
    private const uint Data = 0x02001000;
    private const uint Start = 1u << 31;
    private const uint FullVolume = 0x7F;
    private const uint Loop = 1u << 27;
    private const uint OneShot = 2u << 27;
    private const uint PCM16 = 1u << 29;
    private const uint ADPCM = 2u << 29;
    private const uint PSG = 3u << 29;
    private const ushort EightOutputSamples = 0xF000;
    private const short High = 32704;
    private const short Low = -32768;

    public SoundTests()
    {
        Hardware.Reset(Model.DS);
        Sound.Start();
        ARM7.IO.Write16(SOUNDCNT, 0x807F);
        ARM7.IO.Write16(SOUNDBIAS, 0x200);
    }

    private static uint Channel(int channel) => 0x04000400 + ((uint)channel * 0x10);

    private static void Play(int channel, uint control, ushort timer, ushort loopStart, uint length)
    {
        ARM7.IO.Write32(Channel(channel) + 4, Data);
        ARM7.IO.Write16(Channel(channel) + 8, timer);
        ARM7.IO.Write16(Channel(channel) + 0xA, loopStart);
        ARM7.IO.Write32(Channel(channel) + 0xC, length);
        ARM7.IO.Write32(Channel(channel), control | Start);
    }

    private static bool IsBusy(int channel, long cycles)
    {
        ARM7.Scheduler.Cycles = cycles;
        return (ARM7.IO.Read32(Channel(channel)) & Start) != 0;
    }

    private static short[] LeftSamples()
    {
        var samples = new List<short>();
        Action<ReadOnlySpan<short>, int> record = (buffer, _) => samples.AddRange(buffer.ToArray());
        Sound.SamplesReady += record;
        try
        {
            ARM7.Scheduler.Cycles = 512 * SystemClock.CyclesPerSecond / Sound.SampleRate;
            ARM7.IO.Read16(SOUNDCNT);
        }
        finally
        {
            Sound.SamplesReady -= record;
        }

        return [.. samples.Where((_, index) => index % 2 == 0)];
    }

    private static List<(short Level, int Count)> Runs(short[] samples)
    {
        var runs = new List<(short Level, int Count)>();
        foreach (short sample in samples)
        {
            if (runs.Count > 0 && runs[^1].Level == sample)
            {
                runs[^1] = (sample, runs[^1].Count + 1);
            }
            else
            {
                runs.Add((sample, 1));
            }
        }

        return runs;
    }

    [Fact]
    public void AOneShotSoundIsNoLongerBusyOnceItsLastSampleStarts()
    {
        Play(0, OneShot | FullVolume, timer: 0xFFF0, loopStart: 0, length: 4);

        Assert.True(IsBusy(0, (18 * 64) - 4));
        Assert.False(IsBusy(0, 18 * 64));
    }

    [Fact]
    public void ASoundShorterThanFourWordsStaysBusyForever()
    {
        Play(0, OneShot | FullVolume, timer: 0xFFFF, loopStart: 1, length: 2);

        Assert.True(IsBusy(0, 1_000_000));
    }

    [Fact]
    public void OneChannelAtFullVolumeSpansItsWholeSpeaker()
    {
        ARM9.Memory.Write32(Data, 0x7FFF7FFF);
        ARM9.Memory.Write32(Data + 4, 0x7FFF7FFF);
        ARM9.Memory.Write32(Data + 8, 0x7FFF7FFF);
        ARM9.Memory.Write32(Data + 12, 0x7FFF7FFF);

        Play(0, Loop | PCM16 | FullVolume, timer: 0xFFFF, loopStart: 0, length: 4);

        Assert.Equal(High, LeftSamples()[^1]);
    }

    [Fact]
    public void PSGDutyStartsLow()
    {
        Play(8, PSG | (3u << 24) | FullVolume, EightOutputSamples, loopStart: 0, length: 0);

        var runs = Runs(LeftSamples());

        Assert.Equal([0, Low, High, Low, High], runs.Take(5).Select(run => run.Level));
        Assert.InRange(runs[1].Count, 32, 33);
    }

    [Fact]
    public void NoiseFollowsItsShiftRegister()
    {
        Play(14, PSG | FullVolume, EightOutputSamples, loopStart: 0, length: 0);

        var runs = Runs(LeftSamples());

        Assert.Equal([0, Low, High, Low, High], runs.Take(5).Select(run => run.Level));
        Assert.InRange(runs[1].Count, 112, 113);
        Assert.InRange(runs[2].Count, 112, 113);
        Assert.InRange(runs[3].Count, 8, 9);
    }

    [Fact]
    public void ADPCMStartsFromItsHeaderAfterElevenSamples()
    {
        ARM9.Memory.Write32(Data, 88 << 16);
        ARM9.Memory.Write32(Data + 4, 0x000030F7);

        Play(0, OneShot | ADPCM | FullVolume, EightOutputSamples, loopStart: 0, length: 4);

        var runs = Runs(LeftSamples());

        Assert.Equal([0, High, -28672, -24576, 1472], runs.Take(5).Select(run => run.Level));
        Assert.InRange(runs[0].Count, 87, 89);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u << 27)]
    public void RepeatModesGBATEKDoesNotDescribeAreNotSupported(uint repeatMode)
    {
        Assert.Throws<NotSupportedException>(() => Play(0, repeatMode | FullVolume, timer: 0xFFFF, loopStart: 0, length: 4));
    }

    [Fact]
    public void PSGOnlyWorksOnChannelsEightToFifteen()
    {
        Assert.Throws<NotSupportedException>(() => Play(7, PSG | FullVolume, timer: 0xFFFF, loopStart: 0, length: 0));
    }
}
