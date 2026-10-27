using NTRModern;

namespace LibRecomp;

/// <summary>
/// <c>RecompContext</c> holds a CPU's registers and flags, and every recompiled function takes one. When you call a
/// game function, put its first four arguments in R0 to R3 and the rest on the stack at R13. Whatever it returns
/// ends up in R0.
/// </summary>
/// <example>
/// <code>
/// // Call a game function with two arguments and read what it returns
/// (ctx.R0, ctx.R1) = (x, y);
/// Funcs.sub_02000400(ctx);
/// uint result = ctx.R0;
/// </code>
/// </example>
public sealed class RecompContext(Processor processor)
{
    private const uint ModeMask = 0x1F;
    private const uint ControlMask = 0xFF;
    private const uint UserMode = 0x10;
    private const uint FIQMode = 0x11;
    private const uint SystemMode = 0x1F;
    private const uint ThumbState = 0x20;
    private const uint IRQDisable = 0x80;
    private const uint StickyOverflow = 1u << 27;

    public uint R0, R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14;

    public bool N, Z, C, V;

    /// <summary>The sticky overflow flag. Keep in mind that only the ARM9 has it.</summary>
    public bool Q;

    private readonly uint[] _fiqR8ToR12 = new uint[5];
    private readonly uint[] _otherR8ToR12 = new uint[5];
    private readonly uint[] _bankedR13 = new uint[6];
    private readonly uint[] _bankedR14 = new uint[6];
    private readonly uint[] _spsr = new uint[6];

    private uint _control = SystemMode;

    /// <summary>Which CPU these registers belong to.</summary>
    public Processor Processor { get; } = processor;

    public uint CPSR => _control | (N ? 1u << 31 : 0) | (Z ? 1u << 30 : 0) | (C ? 1u << 29 : 0) | (V ? 1u << 28 : 0) | (Q ? StickyOverflow : 0);

    public uint SPSR => HasSPSR ? _spsr[BankIndex(_control)] : CPSR;

    internal bool AreIRQsEnabled => (_control & IRQDisable) == 0;

    private bool HasSPSR => BankIndex(_control) != 0;

    private uint FlagsMask => Processor == Processor.ARM9 ? 0xF8000000 : 0xF0000000;

    /// <summary>This is called by recompiled code, so you won't need it yourself.</summary>
    public void WriteCPSR(uint value, int fieldMask)
    {
        if ((fieldMask & 8) != 0)
        {
            N = (value & (1u << 31)) != 0;
            Z = (value & (1u << 30)) != 0;
            C = (value & (1u << 29)) != 0;
            V = (value & (1u << 28)) != 0;
            Q = (value & StickyOverflow & FlagsMask) != 0;
        }

        if ((fieldMask & 1) != 0 && (_control & ModeMask) != UserMode)
        {
            SwitchMode(value);
            _control = value & ControlMask;

            if (AreIRQsEnabled && Hardware.InterruptsOf(Processor).IsPending)
            {
                SystemClock.NextEventOf(Processor) = SystemClock.CyclesOf(Processor);
            }
        }
    }

    /// <summary>This is called by recompiled code, so you won't need it yourself.</summary>
    public void WriteSPSR(uint value, int fieldMask)
    {
        if (!HasSPSR)
        {
            return;
        }

        uint mask = ((fieldMask & 8) != 0 ? FlagsMask : 0) | ((fieldMask & 1) != 0 ? ControlMask : 0);
        ref uint spsr = ref _spsr[BankIndex(_control)];
        spsr = (spsr & ~mask) | (value & mask);
    }

    /// <summary>This is called by recompiled code, so you won't need it yourself.</summary>
    public uint ReadUserRegister(int register) => UserRegister(register);

    /// <summary>This is called by recompiled code, so you won't need it yourself.</summary>
    public void WriteUserRegister(int register, uint value) => UserRegister(register) = value;

    internal void EnterException(uint mode)
    {
        uint cpsr = CPSR;
        SwitchMode(mode);
        _control = (_control & ~ModeMask & ~ThumbState) | mode | IRQDisable;
        _spsr[BankIndex(mode)] = cpsr;
    }

    internal void ReturnFromException()
    {
        WriteCPSR(SPSR, 0b1001);
    }

    internal void SetBankedR13(uint mode, uint value)
    {
        if (BankIndex(mode) == BankIndex(_control))
        {
            R13 = value;
        }
        else
        {
            _bankedR13[BankIndex(mode)] = value;
        }
    }

    private ref uint UserRegister(int register)
    {
        bool isBanked = BankIndex(_control) != 0;
        bool isFIQ = (_control & ModeMask) == FIQMode;
        switch (register)
        {
            case 8 when isFIQ:
            case 9 when isFIQ:
            case 10 when isFIQ:
            case 11 when isFIQ:
            case 12 when isFIQ:
                return ref _otherR8ToR12[register - 8];
            case 8:
                return ref R8;
            case 9:
                return ref R9;
            case 10:
                return ref R10;
            case 11:
                return ref R11;
            case 12:
                return ref R12;
            case 13:
                return ref isBanked ? ref _bankedR13[0] : ref R13;
            case 14:
                return ref isBanked ? ref _bankedR14[0] : ref R14;
            default:
                throw new ArgumentOutOfRangeException(nameof(register));
        }
    }

    private void SwitchMode(uint newMode)
    {
        int from = BankIndex(_control);
        int to = BankIndex(newMode);
        if (from == to)
        {
            return;
        }

        _bankedR13[from] = R13;
        _bankedR14[from] = R14;
        R13 = _bankedR13[to];
        R14 = _bankedR14[to];

        bool wasFIQ = (_control & ModeMask) == FIQMode;
        bool isFIQ = (newMode & ModeMask) == FIQMode;
        if (wasFIQ != isFIQ)
        {
            var outgoing = isFIQ ? _otherR8ToR12 : _fiqR8ToR12;
            var incoming = isFIQ ? _fiqR8ToR12 : _otherR8ToR12;
            (outgoing[0], outgoing[1], outgoing[2], outgoing[3], outgoing[4]) = (R8, R9, R10, R11, R12);
            (R8, R9, R10, R11, R12) = (incoming[0], incoming[1], incoming[2], incoming[3], incoming[4]);
        }
    }

    private static int BankIndex(uint mode) => (mode & ModeMask) switch
    {
        0x11 => 1, // FIQ
        0x12 => 2, // IRQ
        0x13 => 3, // Supervisor
        0x17 => 4, // Abort
        0x1B => 5, // Undefined
        _ => 0,    // User and System
    };
}
