using System.Buffers.Binary;

namespace NDSRenderer.Tests;

public class ObjectLinesTests
{
    private const int Lines = 192;
    private const int Engines = 2;
    private const int ObjectGroups = 4;

    private readonly byte[][] _oam = [new byte[0x400], new byte[0x400]];
    private readonly uint[] _objectLines = new uint[Lines * Engines * ObjectGroups];

    public ObjectLinesTests()
    {
        for (int engine = 0; engine < Engines; engine++)
        {
            for (int n = 0; n < 128; n++)
            {
                SetObject(engine, n, attribute0: 0x0200, attribute1: 0);
            }
        }
    }

    private void SetObject(int engine, int n, ushort attribute0, ushort attribute1)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_oam[engine].AsSpan(n * 8), attribute0);
        BinaryPrimitives.WriteUInt16LittleEndian(_oam[engine].AsSpan((n * 8) + 2), attribute1);
    }

    private int[] LinesOf(int engine, int n)
    {
        FrameRenderer.FindObjectLines((_, oamEngine) => _oam[oamEngine], _objectLines);
        return [.. Enumerable.Range(0, Lines).Where(line => (_objectLines[(((line * Engines) + engine) * ObjectGroups) + (n / 32)] & (1u << (n % 32))) != 0)];
    }

    [Fact]
    public void AnObjectIsOnTheLinesItCovers()
    {
        SetObject(0, 40, attribute0: 150, attribute1: 0xC000);

        Assert.Equal(Enumerable.Range(150, 42), LinesOf(0, 40));
    }

    [Fact]
    public void EachEngineHasItsOwnObjects()
    {
        SetObject(1, 5, attribute0: 20, attribute1: 0);

        Assert.Empty(LinesOf(0, 5));
        Assert.Equal(Enumerable.Range(20, 8), LinesOf(1, 5));
    }

    [Fact]
    public void AnObjectPastTheBottomOfTheYRangeShowsAtTheTop()
    {
        SetObject(0, 1, attribute0: 250, attribute1: 0);

        Assert.Equal(Enumerable.Range(0, 2), LinesOf(0, 1));
    }

    [Fact]
    public void ADoubleSizeObject128PixelsTallShowsAtTheTopAndTheBottom()
    {
        SetObject(0, 2, attribute0: 0x0300 | 140, attribute1: 0xC000);

        Assert.Equal(Enumerable.Range(0, 12).Concat(Enumerable.Range(140, 52)), LinesOf(0, 2));
    }

    [Fact]
    public void ADisabledObjectIsOnNoLines()
    {
        SetObject(0, 3, attribute0: 0x0200 | 10, attribute1: 0);

        Assert.Empty(LinesOf(0, 3));
    }
}
