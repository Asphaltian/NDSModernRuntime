namespace NTRModern.Tests;

public class GeometryEngineTests
{
    private const uint GXFIFO = 0x04000400;
    private const uint MTX_MODE = 0x04000440;
    private const uint MTX_PUSH = 0x04000444;
    private const uint MTX_POP = 0x04000448;
    private const uint MTX_STORE = 0x0400044C;
    private const uint MTX_IDENTITY = 0x04000454;
    private const uint MTX_SCALE = 0x0400046C;
    private const uint MTX_TRANS = 0x04000470;
    private const uint SHININESS = 0x040004D0;
    private const uint SWAP_BUFFERS = 0x04000540;
    private const uint VIEWPORT = 0x04000580;
    private const uint GXSTAT = 0x04000600;
    private const uint CLIPMTX_RESULT = 0x04000640;
    private const uint VECMTX_RESULT = 0x04000680;
    private const uint CLEAR_COLOR = 0x04000350;
    private const uint IF = 0x04000214;
    private const uint POWCNT1 = 0x04000304;
    private const ushort GeometryEngineEnable = 1 << 3;
    private const uint Busy = 1 << 27;
    private const uint StackError = 1 << 15;
    private const int One = 1 << 12;
    private const int Projection = 0;
    private const int Position = 1;
    private const int PositionAndVector = 2;

    public GeometryEngineTests() => Hardware.Reset(Model.DS);

    private static void Wait(long busCycles) => ARM9.Scheduler.Cycles += busCycles * 2;

    private static int[] ReadClipMatrix() => [.. Enumerable.Range(0, 16).Select(i => (int)ARM9.IO.Read32(CLIPMTX_RESULT + ((uint)i * 4)))];

    private static void IdentityEverywhere()
    {
        ARM9.IO.Write32(MTX_MODE, Projection);
        ARM9.IO.Write32(MTX_IDENTITY, 0);
        ARM9.IO.Write32(MTX_MODE, PositionAndVector);
        ARM9.IO.Write32(MTX_IDENTITY, 0);
        Wait(1000);
    }

    [Fact]
    public void ACommandKeepsTheEngineBusyForItsCycles()
    {
        ARM9.IO.Write32(MTX_MODE, Position);
        ARM9.IO.Write32(MTX_IDENTITY, 0);

        Wait(1 + 18);
        Assert.Equal(Busy, ARM9.IO.Read32(GXSTAT) & Busy);

        Wait(1);
        Assert.Equal(0u, ARM9.IO.Read32(GXSTAT) & Busy);
    }

    [Fact]
    public void EntriesFillThePIPEBeforeTheFIFO()
    {
        ARM9.IO.Write32(MTX_MODE, Position);
        for (int i = 0; i < 6; i++)
        {
            ARM9.IO.Write32(MTX_IDENTITY, 0);
        }

        Assert.Equal(2u, (ARM9.IO.Read32(GXSTAT) >> 16) & 0x1FF);
    }

    [Fact]
    public void PackedCommandsTakeTheirParametersInOrder()
    {
        ARM9.IO.Write32(GXFIFO, 0x00001510);
        ARM9.IO.Write32(GXFIFO, Projection);
        ARM9.IO.Write32(GXFIFO, 0x00001510);
        ARM9.IO.Write32(GXFIFO, Position);
        Wait(1000);

        Assert.Equal([One, 0, 0, 0, 0, One, 0, 0, 0, 0, One, 0, 0, 0, 0, One], ReadClipMatrix());
    }

    [Fact]
    public void MatricesMultiplyInGBATEKsOrder()
    {
        IdentityEverywhere();
        ARM9.IO.Write32(MTX_MODE, Position);
        foreach (int value in (int[])[1 * One, 2 * One, 3 * One])
        {
            ARM9.IO.Write32(MTX_TRANS, (uint)value);
        }

        foreach (int value in (int[])[2 * One, 2 * One, 2 * One])
        {
            ARM9.IO.Write32(MTX_SCALE, (uint)value);
        }

        Wait(1000);

        Assert.Equal([2 * One, 0, 0, 0, 0, 2 * One, 0, 0, 0, 0, 2 * One, 0, 1 * One, 2 * One, 3 * One, One], ReadClipMatrix());
    }

    [Fact]
    public void ScaleLeavesTheVectorMatrixAlone()
    {
        IdentityEverywhere();
        foreach (int value in (int[])[2 * One, 2 * One, 2 * One])
        {
            ARM9.IO.Write32(MTX_SCALE, (uint)value);
        }

        Wait(1000);

        Assert.Equal((uint)One, ARM9.IO.Read32(VECMTX_RESULT));
        Assert.Equal((uint)One, ARM9.IO.Read32(VECMTX_RESULT + 16));
        Assert.Equal((uint)(2 * One), ARM9.IO.Read32(CLIPMTX_RESULT));
    }

    [Fact]
    public void PushAndPopMoveTheStackLevel()
    {
        IdentityEverywhere();
        ARM9.IO.Write32(MTX_PUSH, 0);
        ARM9.IO.Write32(MTX_PUSH, 0);
        Wait(1000);
        Assert.Equal(2u, (ARM9.IO.Read32(GXSTAT) >> 8) & 31);

        ARM9.IO.Write32(MTX_POP, 2);
        Wait(1000);
        Assert.Equal(0u, (ARM9.IO.Read32(GXSTAT) >> 8) & 31);
    }

    [Fact]
    public void TheProjectionStackHasOneEntry()
    {
        IdentityEverywhere();
        ARM9.IO.Write32(MTX_MODE, Projection);
        ARM9.IO.Write32(MTX_PUSH, 0);
        Wait(1000);
        Assert.Equal(1u << 13, ARM9.IO.Read32(GXSTAT) & (StackError | (1 << 13)));

        ARM9.IO.Write32(MTX_PUSH, 0);
        ARM9.IO.Write32(MTX_PUSH, 0);
        Wait(1000);
        Assert.Equal(StackError | (1 << 13), ARM9.IO.Read32(GXSTAT) & (StackError | (1 << 13)));

        ARM9.IO.Write32(GXSTAT, StackError);
        Assert.Equal(0u, ARM9.IO.Read32(GXSTAT) & (StackError | (1 << 13)));
    }

    [Fact]
    public void StoringToEntry31SetsTheError()
    {
        IdentityEverywhere();
        ARM9.IO.Write32(MTX_STORE, 31);
        Wait(1000);

        Assert.Equal(StackError, ARM9.IO.Read32(GXSTAT) & StackError);
    }

    [Fact]
    public void TheFIFOIRQStaysWhileItsConditionHolds()
    {
        ARM9.IO.Write32(GXSTAT, 2u << 30);
        Assert.Equal((uint)Interrupt.GeometryCommandFIFO, ARM9.IO.Read32(IF));

        ARM9.IO.Write32(IF, (uint)Interrupt.GeometryCommandFIFO);
        Assert.Equal((uint)Interrupt.GeometryCommandFIFO, ARM9.IO.Read32(IF));
    }

    [Fact]
    public void AFullFIFOStallsTheCPU()
    {
        ARM9.IO.Write32(MTX_MODE, Position);
        for (int i = 0; i < 300; i++)
        {
            ARM9.IO.Write32(MTX_IDENTITY, 0);
        }

        Assert.Equal(2 + (39 * 38), ARM9.Scheduler.Cycles);
        Assert.Equal(256u, (ARM9.IO.Read32(GXSTAT) >> 16) & 0x1FF);
    }

    [Fact]
    public void MatricesAreUnknownUntilSet()
    {
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Read32(CLIPMTX_RESULT));
        Assert.Throws<NotSupportedException>(() => ARM9.IO.Write32(MTX_IDENTITY, 0));
    }

    [Fact]
    public void ResultsCantBeReadWhileBusy()
    {
        IdentityEverywhere();
        ARM9.IO.Write32(MTX_IDENTITY, 0);

        Assert.Throws<NotSupportedException>(() => ARM9.IO.Read32(CLIPMTX_RESULT));
    }

    [Fact]
    public void APoweredOffGeometryEngineIgnoresCommands()
    {
        ARM9.IO.Write16(POWCNT1, (ushort)(ARM9.IO.Read16(POWCNT1) & ~GeometryEngineEnable));

        ARM9.IO.Write32(MTX_MODE, Position);

        Assert.Equal(0u, ARM9.IO.Read32(GXSTAT) & Busy);
    }

    [Fact]
    public void TheShininessTableTakesFourEntriesPerWord()
    {
        for (uint word = 0; word < 32; word++)
        {
            ARM9.IO.Write32(SHININESS, 0x03020100 + (word * 0x04040404));
        }

        Wait(1000);

        Assert.Equal(Enumerable.Range(0, 128).Select(i => (byte)i), GeometryEngine.Shininess);
    }

    [Fact]
    public void SwapBuffersWaitsForTheNextVBlank()
    {
        Video.Start(paced: false);
        ARM9.Scheduler.Cycles = 200 * Video.CyclesPerLine;
        ARM9.IO.Write32(SWAP_BUFFERS, 3);
        long done = ((263 + 192) * Video.CyclesPerLine) + (392 * 2);

        ARM9.Scheduler.Cycles = done - 1;
        Assert.Equal(Busy, ARM9.IO.Read32(GXSTAT) & Busy);

        ARM9.Scheduler.Cycles = done;
        Assert.Equal(0u, ARM9.IO.Read32(GXSTAT) & Busy);
        Assert.Equal(3u, GeometryEngine.SwapParameters);
    }

    [Fact]
    public void TheViewportIsKeptForRendering()
    {
        ARM9.IO.Write32(VIEWPORT, 0xBFFF0000);
        Wait(1000);

        Assert.Equal(0xBFFF0000u, GeometryEngine.Viewport);
    }

    [Fact]
    public void RenderingRegistersAreWriteOnly()
    {
        ARM9.IO.Write32(CLEAR_COLOR, 0x7FFF);

        Assert.Throws<NotSupportedException>(() => ARM9.IO.Read32(CLEAR_COLOR));
        Assert.Equal(0u, ARM9.IO.Read32(0x04000344));
    }
}
