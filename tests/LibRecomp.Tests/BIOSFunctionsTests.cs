using NTRModern;

namespace LibRecomp.Tests;

public class BIOSFunctionsTests
{
    private const uint Source = 0x02020000;
    private const uint Destination = 0x02030000;
    private const uint Callbacks = 0x02040000;
    private const uint CallbackCode = 0x02050000;

    public BIOSFunctionsTests() => Hardware.Reset(Model.DS);

    private static RecompContext Call(int function, uint r0 = 0, uint r1 = 0, uint r2 = 0, uint r3 = 0, Processor processor = Processor.ARM9)
    {
        var ctx = new RecompContext(processor) { R0 = r0, R1 = r1, R2 = r2, R3 = r3 };
        BIOS.Call(processor == Processor.ARM9 ? CPU.ARM9 : CPU.ARM7, ctx, function);
        return ctx;
    }

    private static void Put(uint address, params byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            NTRModern.ARM9.Memory.Write8(address + (uint)i, bytes[i]);
        }
    }

    private static byte[] Get(uint address, int length) => [.. Enumerable.Range(0, length).Select(i => NTRModern.ARM9.Memory.Read8(address + (uint)i))];

    private static void RegisterCallbacks(uint openResult = 0, bool hasClose = false)
    {
        RecompFunc open = ctx => ctx.R0 = openResult != 0 ? openResult : NTRModern.ARM9.Memory.Read32(ctx.R0);
        RecompFunc close = ctx => ctx.R0 = 0;
        RecompFunc get8 = ctx => ctx.R0 = NTRModern.ARM9.Memory.Read8(ctx.R0);
        RecompFunc get32 = ctx => ctx.R0 = NTRModern.ARM9.Memory.Read32(ctx.R0);
        CPU.ARM9.Functions.Register([(0, CallbackCode, 0x10, [(0x0, open), (0x4, close), (0x8, get8), (0xC, get32)])], []);

        NTRModern.ARM9.Memory.Write32(Callbacks, CallbackCode);
        NTRModern.ARM9.Memory.Write32(Callbacks + 4, hasClose ? CallbackCode + 4 : 0);
        NTRModern.ARM9.Memory.Write32(Callbacks + 8, CallbackCode + 8);
        NTRModern.ARM9.Memory.Write32(Callbacks + 12, 0);
        NTRModern.ARM9.Memory.Write32(Callbacks + 16, CallbackCode + 0xC);
    }

    [Fact]
    public void DivReturnsTheQuotientTheRemainderAndTheQuotientsSize()
    {
        var ctx = Call(0x09, unchecked((uint)-1234), 10);

        Assert.Equal((-123, -4, 123u), ((int)ctx.R0, (int)ctx.R1, ctx.R3));
    }

    [Fact]
    public void DividingByZeroThrows()
    {
        Assert.Throws<InvalidOperationException>(() => Call(0x09, 5, 0));
    }

    [Theory]
    [InlineData(2u, 1u)]
    [InlineData(2u << 30, 46340u)]
    [InlineData(uint.MaxValue, 65535u)]
    public void SqrtRoundsDown(uint value, uint root)
    {
        Assert.Equal(root, Call(0x0D, value).R0);
    }

    [Fact]
    public void CpuSetCopiesAndFillsHalfwordsOrWords()
    {
        Put(Source, 1, 2, 3, 4, 5, 6, 7, 8);

        Call(0x0B, Source, Destination, 3);
        Assert.Equal([1, 2, 3, 4, 5, 6, 0, 0], Get(Destination, 8));

        Call(0x0B, Source, Destination, (1 << 24) | (1 << 26) | 2);
        Assert.Equal([1, 2, 3, 4, 1, 2, 3, 4], Get(Destination, 8));
    }

    [Fact]
    public void TheARM7WontCopyFromItsBIOS()
    {
        Call(0x0B, 0x00000100, Destination, (1 << 26) | 1, processor: Processor.ARM7);

        Assert.Equal([0, 0, 0, 0], Get(Destination, 4));
    }

    [Fact]
    public void CpuFastSetCopiesAnyNumberOfWords()
    {
        Put(Source, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);

        Call(0x0C, Source, Destination, 3);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0, 0, 0, 0], Get(Destination, 16));
    }

    [Fact]
    public void GetCRC16ReturnsTheCRCAndTheLastHalfword()
    {
        Put(Source, "12345678"u8.ToArray());

        var ctx = Call(0x0E, 0xFFFF, Source, 8);

        Assert.Equal((0x37DDu, 0x3837u), (ctx.R0, ctx.R3));
    }

    [Theory]
    [InlineData(Processor.ARM9, 100, 1600, 0u)]
    [InlineData(Processor.ARM7, 100, 800, 0u)]
    [InlineData(Processor.ARM7, 0, 8, 0xFFFFFFFFu)]
    public void WaitByLoopTakesItsCountInLoops(Processor processor, int count, long cycles, uint r0)
    {
        ref long now = ref SystemClock.CyclesOf(processor);

        var ctx = Call(0x03, (uint)count, processor: processor);

        Assert.Equal((cycles, r0), (now, ctx.R0));
    }

    [Fact]
    public void IsDebuggerSaysThisIsANormalConsole()
    {
        Assert.Equal(0u, Call(0x0F, 5).R0);
    }

    [Theory]
    [InlineData(0x1A, 0x3Fu, 0x7FF5u)]
    [InlineData(0x1B, 0x2FFu, 0xFF8Au)]
    [InlineData(0x1C, 0x2D3u, 0x7Fu)]
    public void TheLastSoundTableEntriesAreTheOnesGBATEKGives(int function, uint index, uint entry)
    {
        Assert.Equal(entry, Call(function, index, processor: Processor.ARM7).R0);
    }

    [Theory]
    [InlineData(0x1A, 0x40u)]
    [InlineData(0x1B, 0x300u)]
    [InlineData(0x1C, 0x2D4u)]
    public void IndexesPastTheSoundTablesThrow(int function, uint index)
    {
        Assert.Throws<NotSupportedException>(() => Call(function, index, processor: Processor.ARM7));
    }

    [Fact]
    public void CustomPostWritesPOSTFLG()
    {
        Call(0x1F, 2);

        Assert.Equal(3, NTRModern.ARM9.IO.Read8(0x04000300));
    }

    [Fact]
    public void BitUnPackWidensEachUnitAndOffsetsTheNonZeroOnes()
    {
        Put(Source + 0x100, 1, 0, 1, 4, 1, 0, 0, 0);
        Put(Source, 0xA5);

        Call(0x10, Source, Destination, Source + 0x100);

        Assert.Equal(0x20200202u, NTRModern.ARM9.Memory.Read32(Destination));
    }

    [Fact]
    public void LZ77CopiesLiteralsAndEarlierOutput()
    {
        Put(Source, 0x10, 9, 0, 0, 0x10, (byte)'A', (byte)'B', (byte)'C', 0x30, 0x02);

        Call(0x11, Source, Destination);

        Assert.Equal("ABCABCABC"u8.ToArray(), Get(Destination, 9));
    }

    [Fact]
    public void RunLengthRepeatsCompressedRunsAndCopiesTheRest()
    {
        Put(Source, 0x30, 6, 0, 0, 0x82, (byte)'A', 0x00, (byte)'B');

        Call(0x14, Source, Destination);

        Assert.Equal("AAAAAB"u8.ToArray(), Get(Destination, 6));
    }

    [Fact]
    public void CallbackVariantsReadThroughTheCallbacksAndReturnTheSize()
    {
        RegisterCallbacks(hasClose: true);
        Put(Source, 0x10, 10, 0, 0, 0x10, (byte)'A', (byte)'B', (byte)'C', 0x30, 0x02, (byte)'D');

        var ctx = Call(0x12, Source, Destination, 0, Callbacks);

        Assert.Equal("ABCABCABCD"u8.ToArray(), Get(Destination, 10));
        Assert.Equal(10u, ctx.R0);
    }

    [Fact]
    public void ANegativeOpenResultStopsTheDecompression()
    {
        RegisterCallbacks(openResult: 0xFFFFFFFE);
        Put(Source, 0x30, 2, 0, 0, 0x80, (byte)'A');

        var ctx = Call(0x15, Source, Destination, 0, Callbacks);

        Assert.Equal(0xFFFFFFFEu, ctx.R0);
        Assert.Equal([0, 0], Get(Destination, 2));
    }

    [Fact]
    public void HuffmanFollowsTheTreeToEachDataNode()
    {
        RegisterCallbacks();
        Put(Source, 0x28, 4, 0, 0, 3, 0x80, (byte)'f', 0xC0, (byte)'H', (byte)'u', 0, 0, 0, 0, 0, 0xB0);

        var ctx = Call(0x13, Source, Destination, 0x02060000, Callbacks);

        Assert.Equal("Huff"u8.ToArray(), Get(Destination, 4));
        Assert.Equal(4u, ctx.R0);
    }
}
