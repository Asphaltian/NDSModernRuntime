using NTRModern;

namespace LibRecomp;

internal sealed class Threads(CPU cpu)
{
    private const uint ContextSP = 0x38;
    private const uint ContextPCPlus4 = 0x40;

    private readonly Dictionary<(uint PC, uint SP), SystemClock.HostThread> _stopped = [];
    private readonly Dictionary<SystemClock.HostThread, (uint PC, uint SP)> _saved = [];

    public void Reset()
    {
        _stopped.Clear();
        _saved.Clear();
    }

    public void SaveContext(RecompContext ctx, RecompFunc saveContext)
    {
        uint context = ctx.R0;
        saveContext(ctx);
        _saved[SystemClock.CurrentThread(cpu.Processor)] = ((cpu.Peek32(context + ContextPCPlus4) - 4) & ~1u, cpu.Peek32(context + ContextSP));
    }

    public void ReturnFromException(RecompContext ctx, uint target)
    {
        var current = SystemClock.CurrentThread(cpu.Processor);
        ContinueAt(ctx, target, _saved.Remove(current, out var saved) ? saved : null);
        cpu.Functions.IndirectJump(ctx, ctx.R14);
    }

    public void ContinueAt(RecompContext ctx, uint pc, (uint PC, uint SP)? stoppedAt)
    {
        var key = (pc & ~1u, ctx.R13);
        if (stoppedAt == key)
        {
            return;
        }

        var processor = cpu.Processor;
        if (!_stopped.Remove(key, out var target))
        {
            if (!cpu.Functions.Contains(pc))
            {
                throw new InvalidOperationException(
                    $"The {processor} switched to a thread at 0x{pc & ~1u:X8}, which isn't a recompiled function.");
            }

            target = SystemClock.CreateThread(processor, () => cpu.Functions.RunFrom(ctx, pc));
        }

        if (stoppedAt is { } at)
        {
            if (_stopped.Remove(at, out var replaced))
            {
                SystemClock.DestroyThread(replaced);
            }

            _stopped[at] = SystemClock.CurrentThread(processor);
            SystemClock.ResumeThreadAndWait(processor, target);
        }
        else
        {
            SystemClock.ResumeThreadAndExit(processor, target);
        }
    }
}
