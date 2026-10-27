namespace NTRModern.ARM9;

internal static class Maths
{
    private const uint DivisionMode = 3;
    private const uint DivisionByZero = 1 << 14;
    private const uint Busy = 1 << 15;
    private const uint SquareRootMode = 1;
    private const long DivisionCycles32 = 18 * SystemClock.BusCycle;
    private const long DivisionCycles64 = 34 * SystemClock.BusCycle;
    private const long SquareRootCycles = 13 * SystemClock.BusCycle;

    private static uint _divisionControl;
    private static long _numerator;
    private static long _denominator;
    private static long _result;
    private static long _remainder;
    private static long _divisionDoneAt;
    private static uint _squareRootControl;
    private static ulong _parameter;
    private static uint _squareRoot;
    private static long _squareRootDoneAt;

    public static uint DIVCNT => _divisionControl | (_denominator == 0 ? DivisionByZero : 0) | (Scheduler.Cycles < _divisionDoneAt ? Busy : 0);

    public static long DIV_NUMER => _numerator;

    public static long DIV_DENOM => _denominator;

    public static long DIV_RESULT => _result;

    public static long DIVREM_RESULT => _remainder;

    public static uint SQRTCNT => _squareRootControl | (Scheduler.Cycles < _squareRootDoneAt ? Busy : 0);

    public static uint SQRT_RESULT => _squareRoot;

    public static ulong SQRT_PARAM => _parameter;

    public static void WriteDIVCNT(uint value)
    {
        _divisionControl = value & DivisionMode;
        Divide();
    }

    public static void WriteDIV_NUMER(long value)
    {
        _numerator = value;
        Divide();
    }

    public static void WriteDIV_DENOM(long value)
    {
        _denominator = value;
        Divide();
    }

    public static void WriteSQRTCNT(uint value)
    {
        _squareRootControl = value & SquareRootMode;
        SquareRoot();
    }

    public static void WriteSQRT_PARAM(ulong value)
    {
        _parameter = value;
        SquareRoot();
    }

    public static void Reset()
    {
        (_divisionControl, _numerator, _denominator, _result, _remainder, _divisionDoneAt) = (0, 0, 0, 0, 0, 0);
        (_squareRootControl, _parameter, _squareRoot, _squareRootDoneAt) = (0, 0, 0, 0);
    }

    private static void Divide()
    {
        if (_divisionControl == 0)
        {
            _divisionDoneAt = Scheduler.Cycles + DivisionCycles32;
            (int result, int remainder, bool overflows) = Divide32((int)_numerator, (int)_denominator);
            _result = overflows ? (long)((ulong)(uint)result | ((ulong)~(result >> 31) << 32)) : result;
            _remainder = remainder;
            return;
        }

        _divisionDoneAt = Scheduler.Cycles + DivisionCycles64;
        (_result, _remainder) = Divide64(_numerator, _divisionControl == 2 ? _denominator : (int)_denominator);
    }

    private static (int Result, int Remainder, bool Overflows) Divide32(int numerator, int denominator) => denominator switch
    {
        0 => (numerator < 0 ? 1 : -1, numerator, true),
        -1 when numerator == int.MinValue => (int.MinValue, 0, true),
        _ => (numerator / denominator, numerator % denominator, false),
    };

    private static (long Result, long Remainder) Divide64(long numerator, long denominator) => denominator switch
    {
        0 => (numerator < 0 ? 1 : -1, numerator),
        -1 when numerator == long.MinValue => (long.MinValue, 0),
        _ => (numerator / denominator, numerator % denominator),
    };

    private static void SquareRoot()
    {
        _squareRootDoneAt = Scheduler.Cycles + SquareRootCycles;
        ulong value = (_squareRootControl & SquareRootMode) == 0 ? (uint)_parameter : _parameter;
        ulong root = (ulong)Math.Min(Math.Sqrt(value), uint.MaxValue);
        while (root * root > value)
        {
            root--;
        }

        while (root < uint.MaxValue && (root + 1) * (root + 1) <= value)
        {
            root++;
        }

        _squareRoot = (uint)root;
    }
}
