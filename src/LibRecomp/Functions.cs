using System.Diagnostics.CodeAnalysis;

namespace LibRecomp;

internal sealed class Functions
{
    private readonly Dictionary<uint, RecompFunc> _map = [];
    private readonly List<(int Section, uint Address)> _loadedOverlays = [];

    private (uint ROMAddress, uint Address, uint Size, (uint Offset, RecompFunc Function)[] Functions)[] _sections = [];
    private int[] _overlaySections = [];
    private uint _unwindTarget;

    public bool IsUnwinding { get; private set; }

    public void Register(
        (uint ROMAddress, uint Address, uint Size, (uint Offset, RecompFunc Function)[] Functions)[] sections, int[] overlaySections)
    {
        (_sections, _overlaySections) = (sections, overlaySections);
        _map.Clear();
        _loadedOverlays.Clear();
        for (int section = 0; section < sections.Length; section++)
        {
            if (!overlaySections.Contains(section))
            {
                Load(section, sections[section].Address);
            }
        }
    }

    public void LoadOverlays(uint romAddress, uint ramAddress, uint size)
    {
        foreach (int section in _overlaySections.Where(s => s >= 0))
        {
            var (sectionROMAddress, _, sectionSize, _) = _sections[section];
            if (sectionROMAddress >= romAddress && (ulong)sectionROMAddress + sectionSize <= (ulong)romAddress + size)
            {
                uint address = sectionROMAddress - romAddress + ramAddress;
                UnloadOverlays(address, sectionSize);
                Load(section, address);
                _loadedOverlays.Add((section, address));
            }
        }
    }

    public void UnloadOverlays(uint ramAddress, uint size)
    {
        _loadedOverlays.RemoveAll(loaded =>
        {
            var (_, _, loadedSize, functions) = _sections[loaded.Section];
            if ((ulong)ramAddress + size <= loaded.Address || ramAddress >= (ulong)loaded.Address + loadedSize)
            {
                return false;
            }

            foreach (var (offset, _) in functions)
            {
                _map.Remove(loaded.Address + offset);
            }

            return true;
        });
    }

    public RecompFunc Lookup(uint address)
    {
        return TryLookup(address, out var function)
            ? function
            : throw new InvalidOperationException($"No recompiled function at 0x{address & ~1u:X8}.");
    }

    public bool Contains(uint address) => TryLookup(address, out _);

    public void RunFrom(RecompContext ctx, uint address)
    {
        uint link = ctx.R14;
        Lookup(address)(ctx);
        while (true)
        {
            ThrowIfUnwinding();
            uint target = link;
            link = ctx.R14;
            IndirectJump(ctx, target);
        }
    }

    public void IndirectJump(RecompContext ctx, uint target)
    {
        if (TryLookup(target, out var function))
        {
            function(ctx);
        }
        else
        {
            (IsUnwinding, _unwindTarget) = (true, target & ~1u);
        }
    }

    public void TailJump(RecompContext ctx, uint link, uint returnAddress)
    {
        while (!IsUnwinding && (link & ~1u) != returnAddress)
        {
            uint target = link;
            link = ctx.R14;
            IndirectJump(ctx, target);
        }
    }

    public bool TryResumeAt(uint address)
    {
        if (_unwindTarget != address)
        {
            return false;
        }

        IsUnwinding = false;
        return true;
    }

    public void ThrowIfUnwinding()
    {
        if (IsUnwinding)
        {
            throw new InvalidOperationException($"The game jumped to 0x{_unwindTarget:X8}, which is not a recompiled function or anywhere a call returns to.");
        }
    }

    public static Exception SwitchError(string function, uint address, uint target)
    {
        return new InvalidOperationException(
            $"The jump table dispatch at 0x{address:X8} in {function} went to 0x{target:X8}, which is not one of its cases.");
    }

    public static Exception JumpError(string function, uint address, uint target)
    {
        return new InvalidOperationException(
            $"The jump at 0x{address:X8} in {function} went to 0x{target:X8}, which the recompiled code can't follow.");
    }

    private void Load(int section, uint address)
    {
        foreach (var (offset, function) in _sections[section].Functions)
        {
            _map[address + offset] = function;
        }
    }

    private bool TryLookup(uint address, [MaybeNullWhen(false)] out RecompFunc function) => _map.TryGetValue(address & ~1u, out function);
}
