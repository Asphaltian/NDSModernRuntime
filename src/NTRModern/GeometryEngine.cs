using System.Runtime.InteropServices;

namespace NTRModern;

internal static partial class GeometryEngine
{
    public const uint RegistersStart = 0x400;
    public const uint RegistersEnd = 0x700;

    private const uint GXFIFO = 0x400;
    private const uint CommandPorts = 0x440;
    private const uint CommandPortsEnd = 0x600;
    private const uint GXSTAT = 0x600;
    private const uint RAM_COUNT = 0x604;
    private const uint DISP_1DOT_DEPTH = 0x610;
    private const uint POS_RESULT = 0x620;
    private const uint TestResultsEnd = 0x638;
    private const uint CLIPMTX_RESULT = 0x640;
    private const uint VECMTX_RESULT = 0x680;
    private const uint VECMTX_RESULTEnd = 0x6A4;
    private const int FIFOSize = 256;
    private const int PIPESize = 4;
    private const int PIPERefillBelow = 3;
    private const int PIPERefill = 2;
    private const int One = 1 << 12;
    private const uint MatrixStackBusy = 1 << 14;
    private const uint MatrixStackError = 1 << 15;
    private const uint FIFOLessThanHalfFull = 1 << 25;
    private const uint FIFOEmpty = 1 << 26;
    private const uint GeometryEngineBusy = 1 << 27;
    private const int FIFOIRQShift = 30;
    private const int FIFOIRQLessThanHalfFull = 1;
    private const int FIFOIRQEmpty = 2;
    private const int FIFOIRQReserved = 3;
    private const int SimultaneousSetExtraCycles = 30;
    private const ushort OneDotDepthMask = 0x7FFF;
    private const ushort OneDotDepthAtBoot = OneDotDepthMask;

    private const byte NOP = 0x00;
    private const byte MTX_MODE = 0x10;
    private const byte MTX_PUSH = 0x11;
    private const byte MTX_POP = 0x12;
    private const byte MTX_STORE = 0x13;
    private const byte MTX_RESTORE = 0x14;
    private const byte MTX_IDENTITY = 0x15;
    private const byte MTX_LOAD_4x4 = 0x16;
    private const byte MTX_LOAD_4x3 = 0x17;
    private const byte MTX_MULT_4x4 = 0x18;
    private const byte MTX_MULT_4x3 = 0x19;
    private const byte MTX_MULT_3x3 = 0x1A;
    private const byte MTX_SCALE = 0x1B;
    private const byte MTX_TRANS = 0x1C;
    private const byte COLOR = 0x20;
    private const byte TEXCOORD = 0x22;
    private const byte VTX_16 = 0x23;
    private const byte VTX_10 = 0x24;
    private const byte VTX_XY = 0x25;
    private const byte VTX_XZ = 0x26;
    private const byte VTX_YZ = 0x27;
    private const byte VTX_DIFF = 0x28;
    private const byte POLYGON_ATTR = 0x29;
    private const byte TEXIMAGE_PARAM = 0x2A;
    private const byte PLTT_BASE = 0x2B;
    private const byte SHININESS = 0x34;
    private const byte BEGIN_VTXS = 0x40;
    private const byte END_VTXS = 0x41;
    private const byte SWAP_BUFFERS = 0x50;
    private const byte VIEWPORT = 0x60;

    private const int ProjectionMode = 0;
    private const int PositionMode = 1;
    private const int PositionAndVectorMode = 2;
    private const int TextureMode = 3;
    private const int UnknownMode = -1;

    public static readonly byte[] Shininess = new byte[128];

    private static readonly int[] Parameters = CreateTable(parameters: true);
    private static readonly int[] Cycles = CreateTable(parameters: false);
    private static readonly SystemClock.Event Finished = SystemClock.CreateEvent(Run);
    private static readonly Queue<Entry> FIFO = new(FIFOSize);
    private static readonly Queue<Entry> PIPE = new(PIPESize);
    private static readonly Matrix Projection = new();
    private static readonly Matrix Position = new();
    private static readonly Matrix Vector = new();
    private static readonly Matrix Texture = new();
    private static readonly Matrix ProjectionStack = new();
    private static readonly Matrix ClipCoordinatesMatrix = new();
    private static readonly Matrix[] PositionStack = CreateStack();
    private static readonly Matrix[] VectorStack = CreateStack();

    private static uint _packed;
    private static int _packedLeft;
    private static byte _parameterCommand;
    private static int _parametersLeft;
    private static bool _isExecuting;
    private static long _executingUntil;
    private static bool _isStackBusy;
    private static bool _isStackError;
    private static int _positionPointer;
    private static int _projectionPointer;
    private static int _fifoIRQ;
    private static int _mode;
    private static bool _isClipCurrent;

    public static uint? PolygonAttributes { get; private set; }

    public static uint? TextureParameters { get; private set; }

    public static uint? PaletteBase { get; private set; }

    public static ushort OneDotDepth { get; private set; } = OneDotDepthAtBoot;

    public static uint? Viewport { get; private set; }

    public static uint? SwapParameters { get; private set; }

    private static long Now => SystemClock.CyclesOf(Processor.ARM9);

    private static bool IsBusy => _isExecuting || PIPE.Count != 0 || FIFO.Count != 0;

    public static uint Read(uint word)
    {
        Run(Now);
        return word switch
        {
            GXSTAT => ReadGXSTAT(),
            RAM_COUNT => ReadRAMCount(),
            >= POS_RESULT and < TestResultsEnd => throw new NotSupportedException("The ARM9 read a position or vector test result, which isn't supported."),
            >= CLIPMTX_RESULT and < VECMTX_RESULT => ReadMatrix(ClipMatrix(), (int)(word - CLIPMTX_RESULT) / 4, word),
            >= VECMTX_RESULT and < VECMTX_RESULTEnd => ReadMatrix(Vector, VectorResultIndex((int)(word - VECMTX_RESULT) / 4), word),
            >= GXFIFO and < CommandPortsEnd or DISP_1DOT_DEPTH => throw new NotSupportedException($"The ARM9 read the write-only geometry register at 0x{0x04000000 + word:X8}."),
            _ => 0,
        };
    }

    public static void Write(uint word, uint value, uint mask)
    {
        if (!ARM9.IO.IsGeometryEnginePowered)
        {
            return;
        }

        Run(Now);
        switch (word)
        {
            case GXSTAT:
                WriteGXSTAT(value, mask);
                break;
            case >= GXFIFO and < CommandPortsEnd when mask != uint.MaxValue:
                throw new NotSupportedException($"The ARM9 wrote the geometry command register at 0x{0x04000000 + word:X8} with mask 0x{mask:X8}, which isn't a 32-bit write.");
            case >= GXFIFO and < CommandPorts:
                WriteGXFIFO(value);
                break;
            case >= CommandPorts and < CommandPortsEnd:
                WriteCommandPort((byte)((word - GXFIFO) / 4), value);
                break;
            case DISP_1DOT_DEPTH:
                OneDotDepth = (ushort)IORegisters.Merge(OneDotDepth, value, mask & OneDotDepthMask);
                break;
            case RAM_COUNT or (>= POS_RESULT and < TestResultsEnd) or (>= CLIPMTX_RESULT and < VECMTX_RESULTEnd):
                throw new NotSupportedException($"The ARM9 wrote the read-only geometry register at 0x{0x04000000 + word:X8}.");
        }
    }

    public static void UpdateInterrupt() => Run(Now);

    public static void Reset()
    {
        Finished.Cancel();
        FIFO.Clear();
        PIPE.Clear();
        (_packed, _packedLeft, _parameterCommand, _parametersLeft) = (0, 0, 0, 0);
        (_isExecuting, _executingUntil, _isStackBusy, _isStackError) = (false, 0, false, false);
        (_positionPointer, _projectionPointer, _fifoIRQ, _mode, _isClipCurrent) = (0, 0, 0, UnknownMode, false);
        foreach (var matrix in (Matrix[])[Projection, Position, Vector, Texture, ProjectionStack, .. PositionStack, .. VectorStack])
        {
            matrix.Forget();
        }

        Array.Clear(Shininess);
        (PolygonAttributes, TextureParameters, PaletteBase, OneDotDepth, Viewport, SwapParameters) = (null, null, null, OneDotDepthAtBoot, null, null);
        ResetPolygons();
    }

    private static uint ReadGXSTAT()
    {
        uint value = ((uint)(_positionPointer & 31) << 8) | ((uint)_projectionPointer << 13) | ((uint)FIFO.Count << 16) | ((uint)_fifoIRQ << FIFOIRQShift);
        value |= _isStackBusy ? MatrixStackBusy : 0;
        value |= _isStackError ? MatrixStackError : 0;
        value |= FIFO.Count < FIFOSize / 2 ? FIFOLessThanHalfFull : 0;
        value |= FIFO.Count == 0 ? FIFOEmpty : 0;
        value |= IsBusy ? GeometryEngineBusy : 0;
        return value;
    }

    private static void WriteGXSTAT(uint value, uint mask)
    {
        if ((value & mask & MatrixStackError) != 0)
        {
            (_isStackError, _projectionPointer) = (false, 0);
        }

        if ((mask & (3u << FIFOIRQShift)) != 0)
        {
            int irq = (int)(IORegisters.Merge((uint)_fifoIRQ << FIFOIRQShift, value, mask) >> FIFOIRQShift);
            _fifoIRQ = irq != FIFOIRQReserved ? irq : throw new NotSupportedException("The ARM9 set the geometry FIFO IRQ to reserved mode 3.");
        }

        CheckInterrupt();
    }

    private static uint ReadMatrix(Matrix matrix, int index, uint word)
    {
        if (IsBusy)
        {
            throw new NotSupportedException($"The ARM9 read 0x{0x04000000 + word:X8} while the geometry engine was busy.");
        }

        return matrix.IsKnown
            ? (uint)matrix.Values[index]
            : throw new NotSupportedException($"The ARM9 read 0x{0x04000000 + word:X8} before the game set that matrix.");
    }

    private static int VectorResultIndex(int index) => ((index / 3) * 4) + (index % 3);

    private static Matrix ClipMatrix()
    {
        if (!_isClipCurrent)
        {
            ClipCoordinatesMatrix.CopyFrom(Projection);
            ClipCoordinatesMatrix.MultiplyBy(Position.Values);
            ClipCoordinatesMatrix.IsKnown &= Position.IsKnown;
            _isClipCurrent = true;
        }

        return ClipCoordinatesMatrix;
    }

    private static void WriteGXFIFO(uint value)
    {
        if (_parametersLeft > 0)
        {
            Enqueue(_parameterCommand, value);
            _parametersLeft--;
            if (_parametersLeft == 0)
            {
                DecodePacked();
            }

            return;
        }

        if (value == 0)
        {
            Enqueue(NOP, 0);
            return;
        }

        (_packed, _packedLeft) = (value, 4);
        DecodePacked();
    }

    private static void DecodePacked()
    {
        while (_packedLeft > 0)
        {
            byte command = (byte)_packed;
            _packed >>= 8;
            _packedLeft--;
            if (command == NOP)
            {
                continue;
            }

            int parameters = ParametersOf(command);
            if (parameters == 0)
            {
                Enqueue(command, 0);
                continue;
            }

            (_parameterCommand, _parametersLeft) = (command, parameters);
            return;
        }
    }

    private static void WriteCommandPort(byte command, uint value)
    {
        if (_parametersLeft > 0)
        {
            throw new NotSupportedException($"The ARM9 wrote geometry command port 0x{0x04000000 + GXFIFO + (command * 4u):X8} while GXFIFO still expected parameters for command 0x{_parameterCommand:X2}.");
        }

        ParametersOf(command);
        Enqueue(command, value);
    }

    private static int ParametersOf(byte command)
    {
        int parameters = Parameters[command];
        return parameters >= 0 ? parameters : throw new NotSupportedException($"The ARM9 sent invalid geometry command 0x{command:X2}.");
    }

    private static void Enqueue(byte command, uint parameter)
    {
        while (FIFO.Count == FIFOSize)
        {
            SystemClock.CyclesOf(Processor.ARM9) = Math.Max(Now, _executingUntil);
            Run(_executingUntil);
        }

        if (FIFO.Count == 0 && PIPE.Count < PIPESize)
        {
            PIPE.Enqueue(new Entry(command, parameter));
        }
        else
        {
            FIFO.Enqueue(new Entry(command, parameter));
        }

        if (!_isExecuting)
        {
            TryStart(Now);
        }

        CheckInterrupt();
    }

    private static Entry Dequeue()
    {
        var entry = PIPE.Dequeue();
        while (PIPE.Count < PIPERefillBelow && FIFO.Count > 0)
        {
            for (int moved = 0; moved < PIPERefill && FIFO.Count > 0; moved++)
            {
                PIPE.Enqueue(FIFO.Dequeue());
            }
        }

        return entry;
    }

    private static void Run(long now)
    {
        while (_isExecuting && _executingUntil <= now)
        {
            (_isExecuting, _isStackBusy) = (false, false);
            CompleteSwap(_executingUntil);
            TryStart(_executingUntil);
        }

        CompleteSwap(now);
        CheckInterrupt();
    }

    private static void TryStart(long at)
    {
        if (PIPE.Count == 0)
        {
            return;
        }

        byte command = PIPE.Peek().Command;
        int entries = Math.Max(1, Parameters[command]);
        if (PIPE.Count + FIFO.Count < entries)
        {
            return;
        }

        Span<int> parameters = stackalloc int[entries];
        for (int index = 0; index < entries; index++)
        {
            parameters[index] = (int)Dequeue().Parameter;
        }

        _executingUntil = at + Execute(command, parameters, at);
        _isExecuting = true;
        Finished.ScheduleAt(_executingUntil);
    }

    private static long Execute(byte command, ReadOnlySpan<int> parameters, long at)
    {
        long cycles = Cycles[command] * SystemClock.BusCycle;
        if (command is >= MTX_POP and <= MTX_TRANS)
        {
            _isClipCurrent = false;
        }

        switch (command)
        {
            case NOP or END_VTXS:
                break;
            case COLOR:
                SetColor(parameters[0]);
                break;
            case TEXCOORD:
                SetTexCoord(parameters[0]);
                break;
            case >= VTX_16 and <= VTX_DIFF:
                SetVertex(command, parameters);
                break;
            case BEGIN_VTXS:
                BeginVertexList(parameters[0]);
                break;
            case MTX_MODE:
                _mode = parameters[0] & 3;
                break;
            case MTX_PUSH:
                Push();
                _isStackBusy = true;
                break;
            case MTX_POP:
                Pop(parameters[0]);
                _isStackBusy = true;
                break;
            case MTX_STORE:
                Store(parameters[0]);
                break;
            case MTX_RESTORE:
                Restore(parameters[0]);
                break;
            case MTX_IDENTITY:
                ForEachCurrent(matrix => matrix.LoadIdentity(), includesVector: true);
                break;
            case MTX_LOAD_4x4 or MTX_LOAD_4x3:
                int[] loaded = Expand(command, parameters);
                ForEachCurrent(matrix => matrix.Load(loaded), includesVector: true);
                break;
            case MTX_MULT_4x4 or MTX_MULT_4x3 or MTX_MULT_3x3 or MTX_SCALE or MTX_TRANS:
                int[] factor = Expand(command, parameters);
                ForEachCurrent(matrix => matrix.MultiplyBy(factor), includesVector: command != MTX_SCALE);
                if (_mode == PositionAndVectorMode && command != MTX_SCALE)
                {
                    cycles += SimultaneousSetExtraCycles * SystemClock.BusCycle;
                }

                break;
            case POLYGON_ATTR:
                PolygonAttributes = (uint)parameters[0];
                break;
            case TEXIMAGE_PARAM:
                TextureParameters = (uint)parameters[0];
                break;
            case PLTT_BASE:
                PaletteBase = (uint)parameters[0];
                break;
            case SWAP_BUFFERS:
                cycles += SwapBuffers((uint)parameters[0], at);
                break;
            case VIEWPORT:
                Viewport = (uint)parameters[0];
                break;
            case SHININESS:
                MemoryMarshal.AsBytes(parameters).CopyTo(Shininess);
                break;
            default:
                throw new NotSupportedException($"The geometry engine ran command 0x{command:X2}, which isn't supported.");
        }

        return cycles;
    }

    private static void ForEachCurrent(Action<Matrix> action, bool includesVector)
    {
        switch (CurrentMode())
        {
            case ProjectionMode:
                action(Projection);
                break;
            case PositionMode:
                action(Position);
                break;
            case PositionAndVectorMode:
                action(Position);
                if (includesVector)
                {
                    action(Vector);
                }

                break;
            case TextureMode:
                action(Texture);
                break;
        }
    }

    private static int CurrentMode() => _mode != UnknownMode
        ? _mode
        : throw new NotSupportedException("The geometry engine ran a matrix command before MTX_MODE.");

    private static void Push()
    {
        switch (CurrentMode())
        {
            case ProjectionMode:
                _isStackError |= _projectionPointer != 0;
                ProjectionStack.CopyFrom(Projection);
                _projectionPointer ^= 1;
                break;
            case PositionMode or PositionAndVectorMode:
                _isStackError |= _positionPointer >= 31;
                PositionStack[_positionPointer & 31].CopyFrom(Position);
                VectorStack[_positionPointer & 31].CopyFrom(Vector);
                _positionPointer = (_positionPointer + 1) & 63;
                break;
            default:
                throw TextureStack();
        }
    }

    private static void Pop(int parameter)
    {
        switch (CurrentMode())
        {
            case ProjectionMode:
                _projectionPointer ^= 1;
                _isStackError |= _projectionPointer != 0;
                Projection.CopyFrom(ProjectionStack);
                break;
            case PositionMode or PositionAndVectorMode:
                int offset = (parameter << 26) >> 26;
                _positionPointer = (_positionPointer - offset) & 63;
                _isStackError |= _positionPointer >= 31;
                Position.CopyFrom(PositionStack[_positionPointer & 31]);
                Vector.CopyFrom(VectorStack[_positionPointer & 31]);
                break;
            default:
                throw TextureStack();
        }
    }

    private static void Store(int parameter)
    {
        switch (CurrentMode())
        {
            case ProjectionMode:
                ProjectionStack.CopyFrom(Projection);
                break;
            case PositionMode or PositionAndVectorMode:
                int address = parameter & 31;
                _isStackError |= address == 31;
                PositionStack[address].CopyFrom(Position);
                VectorStack[address].CopyFrom(Vector);
                break;
            default:
                throw TextureStack();
        }
    }

    private static void Restore(int parameter)
    {
        switch (CurrentMode())
        {
            case ProjectionMode:
                Projection.CopyFrom(ProjectionStack);
                break;
            case PositionMode or PositionAndVectorMode:
                int address = parameter & 31;
                _isStackError |= address == 31;
                Position.CopyFrom(PositionStack[address]);
                Vector.CopyFrom(VectorStack[address]);
                break;
            default:
                throw TextureStack();
        }
    }

    private static NotSupportedException TextureStack()
    {
        return new NotSupportedException("The geometry engine used the texture matrix stack, which isn't supported.");
    }

    private static int[] Expand(byte command, ReadOnlySpan<int> m) => command switch
    {
        MTX_LOAD_4x4 or MTX_MULT_4x4 => m.ToArray(),
        MTX_LOAD_4x3 or MTX_MULT_4x3 => [m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], One],
        MTX_MULT_3x3 => [m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, 0, 0, 0, One],
        MTX_SCALE => [m[0], 0, 0, 0, 0, m[1], 0, 0, 0, 0, m[2], 0, 0, 0, 0, One],
        _ => [One, 0, 0, 0, 0, One, 0, 0, 0, 0, One, 0, m[0], m[1], m[2], One],
    };

    private static void CheckInterrupt()
    {
        bool isRequested = _fifoIRQ switch
        {
            FIFOIRQLessThanHalfFull => FIFO.Count < FIFOSize / 2,
            FIFOIRQEmpty => FIFO.Count == 0,
            _ => false,
        };

        if (isRequested)
        {
            ARM9.IO.Interrupts.Raise(Interrupt.GeometryCommandFIFO);
        }
    }

    private static int[] CreateTable(bool parameters)
    {
        int[] table = new int[256];
        Array.Fill(table, -1);
        foreach (var (command, parameterCount, cycles) in ((int, int, int)[])[
            (0x00, 0, 1),
            (0x10, 1, 1), (0x11, 0, 17), (0x12, 1, 36), (0x13, 1, 17), (0x14, 1, 36), (0x15, 0, 19), (0x16, 16, 34),
            (0x17, 12, 30), (0x18, 16, 35), (0x19, 12, 31), (0x1A, 9, 28), (0x1B, 3, 22), (0x1C, 3, 22),
            (0x20, 1, 1), (0x21, 1, 9), (0x22, 1, 1), (0x23, 2, 9), (0x24, 1, 8), (0x25, 1, 8), (0x26, 1, 8),
            (0x27, 1, 8), (0x28, 1, 8), (0x29, 1, 1), (0x2A, 1, 1), (0x2B, 1, 1),
            (0x30, 1, 4), (0x31, 1, 4), (0x32, 1, 6), (0x33, 1, 1), (0x34, 32, 32),
            (0x40, 1, 1), (0x41, 0, 1), (0x50, 1, 392), (0x60, 1, 1), (0x70, 3, 103), (0x71, 2, 9), (0x72, 1, 5)])
        {
            table[command] = parameters ? parameterCount : cycles;
        }

        return table;
    }

    private static Matrix[] CreateStack() => [.. Enumerable.Range(0, 32).Select(_ => new Matrix())];

    private readonly record struct Entry(byte Command, uint Parameter);

    private sealed class Matrix
    {
        public readonly int[] Values = new int[16];

        public bool IsKnown { get; set; }

        public void Forget()
        {
            Array.Clear(Values);
            IsKnown = false;
        }

        public void CopyFrom(Matrix other)
        {
            other.Values.CopyTo(Values, 0);
            IsKnown = other.IsKnown;
        }

        public void LoadIdentity() => Load([One, 0, 0, 0, 0, One, 0, 0, 0, 0, One, 0, 0, 0, 0, One]);

        public void Load(ReadOnlySpan<int> values)
        {
            values.CopyTo(Values);
            IsKnown = true;
        }

        public void MultiplyBy(ReadOnlySpan<int> m)
        {
            Span<int> result = stackalloc int[16];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    long sum = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        sum += (long)m[(y * 4) + k] * Values[(k * 4) + x];
                    }

                    result[(y * 4) + x] = (int)(sum >> 12);
                }
            }

            result.CopyTo(Values);
        }
    }
}
