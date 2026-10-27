namespace NTRModern.ARM9;

internal sealed class Cache
{
    private const int Ways = 4;
    private const int IndexShift = 30;
    private const int LineShift = 5;
    private const uint LineMask = (1u << LineShift) - 1;
    private const uint Invalid = 1;

    private readonly uint[] _lines;
    private readonly byte[] _nextWays;
    private readonly uint _setMask;

    public Cache(int size)
    {
        int sets = size / (Ways << LineShift);
        _lines = new uint[sets * Ways];
        _nextWays = new byte[sets];
        _setMask = (uint)sets - 1;
        Reset();
    }

    public bool Load(uint address, bool isRoundRobin)
    {
        if (Contains(address))
        {
            return true;
        }

        if (!isRoundRobin)
        {
            throw new NotSupportedException("Pseudo-random cache replacement isn't supported.");
        }

        int set = SetOf(address);
        int replaced = _nextWays[set];
        _nextWays[set] = (byte)((replaced + 1) % Ways);
        _lines[(set * Ways) + replaced] = address & ~LineMask;
        return false;
    }

    public bool Contains(uint address)
    {
        uint line = address & ~LineMask;
        int first = SetOf(address) * Ways;
        for (int way = 0; way < Ways; way++)
        {
            if (_lines[first + way] == line)
            {
                return true;
            }
        }

        return false;
    }

    public void Invalidate(uint address)
    {
        uint line = address & ~LineMask;
        int first = SetOf(address) * Ways;
        for (int way = 0; way < Ways; way++)
        {
            if (_lines[first + way] == line)
            {
                _lines[first + way] = Invalid;
            }
        }
    }

    public void InvalidateAt(uint setAndIndex)
    {
        int way = (int)(setAndIndex >> IndexShift);
        _lines[(SetOf(setAndIndex) * Ways) + way] = Invalid;
    }

    public void InvalidateAll() => Array.Fill(_lines, Invalid);

    public (uint[] Lines, byte[] NextWays) Save() => ([.. _lines], [.. _nextWays]);

    public void Restore((uint[] Lines, byte[] NextWays) saved)
    {
        saved.Lines.CopyTo(_lines, 0);
        saved.NextWays.CopyTo(_nextWays, 0);
    }

    public void Reset()
    {
        InvalidateAll();
        Array.Clear(_nextWays);
    }

    private int SetOf(uint address) => (int)((address >> LineShift) & _setMask);
}
