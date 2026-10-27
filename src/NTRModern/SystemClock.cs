using System.Runtime.ExceptionServices;

namespace NTRModern;

internal static class SystemClock
{
    public const long CyclesPerSecond = 67027964;
    public const long BusCycle = 2;
    public const long MaxLead = Video.CyclesPerLine;

    private const int StackSize = 64 * 1024 * 1024;

    private static readonly List<Event> Events = [];
    private static readonly List<HostThread> Threads = [];
    private static readonly HostThread?[] Current = new HostThread?[2];
    private static readonly Func<bool>?[] WakeConditions = new Func<bool>?[2];
    private static readonly bool[] IsStopped = new bool[2];

    private static Exception? _failure;
    private static bool _isFinished;
    private static CountdownEvent? _runningCPUs;
    private static int _untimedDepth;

    public static ref long CyclesOf(Processor processor)
    {
        if (processor == Processor.ARM9)
        {
            return ref ARM9.Scheduler.Cycles;
        }

        return ref ARM7.Scheduler.Cycles;
    }

    public static ref long NextEventOf(Processor processor)
    {
        if (processor == Processor.ARM9)
        {
            return ref ARM9.Scheduler.NextEvent;
        }

        return ref ARM7.Scheduler.NextEvent;
    }

    public static Event CreateEvent(Action<long> callback)
    {
        var scheduledEvent = new Event(callback);
        Events.Add(scheduledEvent);
        return scheduledEvent;
    }

    public static void Run(Action arm9, Action arm7)
    {
        (_failure, _isFinished) = (null, false);
        Array.Clear(WakeConditions);
        Array.Clear(IsStopped);
        Threads.Clear();
        using var runningCPUs = new CountdownEvent(2);
        _runningCPUs = runningCPUs;
        Current[(int)Processor.ARM9] = CreateThread(Processor.ARM9, arm9);
        Current[(int)Processor.ARM7] = CreateThread(Processor.ARM7, arm7);
        Current[(int)Processor.ARM9]!.Running.Release();
        runningCPUs.Wait();

        _isFinished = true;
        foreach (var thread in Threads)
        {
            thread.Running.Release();
            thread.Thread.Join();
        }

        if (_failure is not null)
        {
            ExceptionDispatchInfo.Capture(_failure).Throw();
        }
    }

    public static HostThread CurrentThread(Processor processor) => Current[(int)processor]!;

    public static HostThread CreateThread(Processor processor, Action code)
    {
        var thread = new HostThread(processor, self => Run(processor, code, self));
        Threads.Add(thread);
        thread.Thread.Start();
        return thread;
    }

    public static void ResumeThreadAndWait(Processor processor, HostThread thread)
    {
        var current = Current[(int)processor]!;
        Current[(int)processor] = thread;
        thread.Running.Release();
        current.Running.Wait();
        ThrowIfStopping(current);
    }

    public static void ResumeThreadAndExit(Processor processor, HostThread thread)
    {
        Current[(int)processor] = thread;
        throw new ThreadTerminated(thread);
    }

    public static void DestroyThread(HostThread thread)
    {
        thread.IsDestroyed = true;
        thread.Running.Release();
        thread.Thread.Join();
    }

    public static void RunUntimed(Processor self, Action work)
    {
        long cycles = CyclesOf(self);
        NextEventOf(self) = long.MaxValue;
        _untimedDepth++;
        try
        {
            work();
        }
        finally
        {
            CyclesOf(self) = cycles;
            _untimedDepth--;
            if (_untimedDepth == 0)
            {
                NextEventOf(self) = Limit(self);
            }
        }
    }

    public static void Synchronize(Processor self)
    {
        if (_untimedDepth > 0)
        {
            return;
        }

        while (true)
        {
            RunDueEvents();
            if (CyclesOf(self) < Limit(self))
            {
                break;
            }

            if (!IsStopped[(int)Other(self)])
            {
                HandOff(self);
            }
        }

        NextEventOf(self) = Limit(self);
    }

    public static void CatchUp(Processor self)
    {
        if (_untimedDepth > 0)
        {
            return;
        }

        var other = Other(self);
        while (!IsStopped[(int)other] && CyclesOf(other) < CyclesOf(self))
        {
            HandOff(self);
        }
    }

    public static void Halt(Processor self, Func<bool> isInterrupted)
    {
        var other = Other(self);
        WakeConditions[(int)self] = isInterrupted;
        try
        {
            while (!isInterrupted())
            {
                long next = EarliestEvent();
                var otherWakeCondition = WakeConditions[(int)other];
                bool otherWaits = IsStopped[(int)other] || (otherWakeCondition is not null && !otherWakeCondition());
                if (otherWaits && next == long.MaxValue)
                {
                    throw new InvalidOperationException($"The {self} is waiting for an interrupt that will never come.");
                }

                ref long mine = ref CyclesOf(self);
                mine = Math.Max(mine, otherWaits ? next : Math.Min(next, CyclesOf(other)));
                if (otherWakeCondition is not null)
                {
                    CyclesOf(other) = Math.Max(CyclesOf(other), mine);
                }

                RunDueEvents();
                if (!isInterrupted() && !IsStopped[(int)other])
                {
                    HandOff(self);
                }
            }
        }
        finally
        {
            WakeConditions[(int)self] = null;
        }

        NextEventOf(self) = Limit(self);
    }

    public static Processor Other(Processor processor) => processor == Processor.ARM9 ? Processor.ARM7 : Processor.ARM9;

    public static void Reset()
    {
        foreach (var scheduledEvent in Events)
        {
            scheduledEvent.Cancel();
        }

        Array.Clear(WakeConditions);
        Array.Clear(IsStopped);
        (ARM9.Scheduler.Cycles, ARM7.Scheduler.Cycles) = (0, 0);
        (ARM9.Scheduler.NextEvent, ARM7.Scheduler.NextEvent) = (long.MaxValue, long.MaxValue);
    }

    private static void Run(Processor processor, Action code, HostThread thread)
    {
        HostThread? next = null;
        try
        {
            thread.Running.Wait();
            ThrowIfStopping(thread);
            NextEventOf(processor) = Limit(processor);
            code();
        }
        catch (Stopping)
        {
        }
        catch (ThreadTerminated terminated)
        {
            next = terminated.Next;
        }
        catch (Exception e)
        {
            _failure ??= e;
        }
        finally
        {
            if (next is not null)
            {
                next.Running.Release();
            }
            else if (Current[(int)processor] == thread && !IsStopped[(int)processor])
            {
                IsStopped[(int)processor] = true;
                Current[(int)Other(processor)]!.Running.Release();
                _runningCPUs!.Signal();
            }
        }
    }

    private static void HandOff(Processor self)
    {
        var current = Current[(int)self]!;
        Current[(int)Other(self)]!.Running.Release();
        current.Running.Wait();
        ThrowIfStopping(current);
    }

    private static void ThrowIfStopping(HostThread thread)
    {
        if (_failure is not null || _isFinished)
        {
            throw new Stopping();
        }

        if (thread.IsDestroyed)
        {
            throw new ThreadTerminated(null);
        }
    }

    private static long Limit(Processor self)
    {
        var other = Other(self);
        return IsStopped[(int)other] ? EarliestEvent() : Math.Min(EarliestEvent(), CyclesOf(other) + MaxLead);
    }

    private static void RunDueEvents()
    {
        long now = Math.Min(
            IsStopped[(int)Processor.ARM9] ? long.MaxValue : CyclesOf(Processor.ARM9),
            IsStopped[(int)Processor.ARM7] ? long.MaxValue : CyclesOf(Processor.ARM7));
        while (true)
        {
            Event? earliest = null;
            foreach (var candidate in Events)
            {
                if (candidate.When <= now && (earliest is null || candidate.When < earliest.When))
                {
                    earliest = candidate;
                }
            }

            if (earliest is null)
            {
                return;
            }

            long dueAt = earliest.When;
            earliest.When = long.MaxValue;
            earliest.Callback(dueAt);
        }
    }

    private static long EarliestEvent()
    {
        long earliest = long.MaxValue;
        foreach (var scheduledEvent in Events)
        {
            earliest = Math.Min(earliest, scheduledEvent.When);
        }

        return earliest;
    }

    public sealed class Event(Action<long> callback)
    {
        internal readonly Action<long> Callback = callback;

        public long When { get; internal set; } = long.MaxValue;

        public void ScheduleAt(long when)
        {
            When = when;
            if (_untimedDepth == 0)
            {
                ARM9.Scheduler.NextEvent = Math.Min(ARM9.Scheduler.NextEvent, when);
                ARM7.Scheduler.NextEvent = Math.Min(ARM7.Scheduler.NextEvent, when);
            }
        }

        public void Cancel() => When = long.MaxValue;
    }

    public sealed class HostThread
    {
        internal readonly Thread Thread;
        internal readonly SemaphoreSlim Running = new(0);
        internal bool IsDestroyed;

        internal HostThread(Processor processor, Action<HostThread> run)
        {
            Thread = new Thread(() => run(this), StackSize) { Name = processor.ToString(), IsBackground = true };
        }
    }

    private sealed class Stopping : Exception;

    private sealed class ThreadTerminated(HostThread? next) : Exception
    {
        public HostThread? Next { get; } = next;
    }
}
