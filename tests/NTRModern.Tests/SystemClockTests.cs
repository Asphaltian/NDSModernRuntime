namespace NTRModern.Tests;

public class SystemClockTests
{
    private const uint IPCSYNC = 0x04000180;
    private const uint IME = 0x04000208;
    private const uint IE = 0x04000210;
    private const uint HALTCNT = 0x04000301;
    private const ushort SyncIRQEnable = 1 << 14;
    private const ushort SyncSendIRQ = 1 << 13;

    public SystemClockTests() => Hardware.Reset(Model.DS);

    private static void Spend(Processor processor, long cycles)
    {
        ref long now = ref SystemClock.CyclesOf(processor);
        now += cycles;
        if (now >= SystemClock.NextEventOf(processor))
        {
            SystemClock.Synchronize(processor);
        }
    }

    [Fact]
    public void NothingScheduledRunsWhileTheARM9RunsUntimed()
    {
        bool ran = false;
        var scheduledEvent = SystemClock.CreateEvent(_ => ran = true);

        ARM9.Scheduler.RunUntimed(() =>
        {
            scheduledEvent.ScheduleAt(ARM9.Scheduler.Cycles + 10);
            Assert.Equal(long.MaxValue, ARM9.Scheduler.NextEvent);
            ARM9.Scheduler.Cycles += 1000;
            SystemClock.Synchronize(Processor.ARM9);
        });

        Assert.False(ran);
        Assert.True(ARM9.Scheduler.NextEvent <= scheduledEvent.When);
        scheduledEvent.Cancel();
    }

    [Fact]
    public void NeitherCPUGetsMoreThanAScanlineAhead()
    {
        const long Step = 100;
        long lead = 0;

        SystemClock.Run(() => Work(Processor.ARM9), () => Work(Processor.ARM7));

        Assert.Equal(1000 * Step, ARM9.Scheduler.Cycles);
        Assert.Equal(1000 * Step, ARM7.Scheduler.Cycles);
        Assert.InRange(lead, 1, SystemClock.MaxLead + Step);

        void Work(Processor processor)
        {
            var other = processor == Processor.ARM9 ? Processor.ARM7 : Processor.ARM9;
            for (int step = 0; step < 1000; step++)
            {
                Spend(processor, Step);
                lead = Math.Max(lead, SystemClock.CyclesOf(processor) - SystemClock.CyclesOf(other));
            }
        }
    }

    [Fact]
    public void AnEventRunsOnceBothCPUsHaveReachedIt()
    {
        long ranAt = -1;
        long slowest = -1;
        var scheduledEvent = SystemClock.CreateEvent(when =>
        {
            ranAt = when;
            slowest = Math.Min(ARM9.Scheduler.Cycles, ARM7.Scheduler.Cycles);
        });
        scheduledEvent.ScheduleAt(1000);

        SystemClock.Run(() => Work(Processor.ARM9, 7), () => Work(Processor.ARM7, 13));

        Assert.Equal(1000, ranAt);
        Assert.True(slowest >= 1000);

        static void Work(Processor processor, long step)
        {
            while (SystemClock.CyclesOf(processor) < 5000)
            {
                Spend(processor, step);
            }
        }
    }

    [Fact]
    public void EventsThatPushACPUPastTheNextOneStillRunWhenTheOtherHasStopped()
    {
        var ran = new List<long>();
        SystemClock.CreateEvent(when =>
        {
            ran.Add(when);
            ARM9.Scheduler.Cycles += 1000;
        }).ScheduleAt(100);
        SystemClock.CreateEvent(ran.Add).ScheduleAt(500);

        SystemClock.Run(() => Spend(Processor.ARM9, 200), () => { });

        Assert.Equal([100, 500], ran);
    }

    [Fact]
    public void AnExceptionOnOneCPUStopsBothAndComesOutOfRun()
    {
        var exception = Assert.Throws<InvalidDataException>(() => SystemClock.Run(
            () =>
            {
                while (true)
                {
                    Spend(Processor.ARM9, 100);
                }
            },
            () =>
            {
                Spend(Processor.ARM7, 100);
                throw new InvalidDataException("ARM7");
            }));

        Assert.Equal("ARM7", exception.Message);
    }

    [Fact]
    public void HaltingWithNothingLeftToSendAnInterruptThrows()
    {
        Assert.Throws<InvalidOperationException>(() => SystemClock.Run(
            () => SystemClock.Halt(Processor.ARM9, () => false),
            () => SystemClock.Halt(Processor.ARM7, () => false)));
    }

    [Fact]
    public void AHaltedARM9WakesAtTheTimeTheARM7InterruptsIt()
    {
        long wokeAt = -1;

        SystemClock.Run(
            () =>
            {
                ARM9.IO.Write32(IME, 1);
                ARM9.IO.Write32(IE, (uint)Interrupt.IPCSync);
                ARM9.IO.Write16(IPCSYNC, SyncIRQEnable);
                ARM9.CP15.Write(0, 7, 0, 4, 0);
                wokeAt = ARM9.Scheduler.Cycles;
            },
            () =>
            {
                for (int step = 0; step < 100; step++)
                {
                    Spend(Processor.ARM7, 100);
                }

                ARM7.IO.Write16(IPCSYNC, SyncSendIRQ);
            });

        Assert.Equal(10000, wokeAt);
    }

    [Fact]
    public void HALTCNTWakesTheARM7EvenWithIMEOff()
    {
        long wokeAt = -1;

        SystemClock.Run(
            () =>
            {
                for (int step = 0; step < 50; step++)
                {
                    Spend(Processor.ARM9, 100);
                }

                ARM9.IO.Write16(IPCSYNC, SyncSendIRQ);
            },
            () =>
            {
                ARM7.IO.Write32(IE, (uint)Interrupt.IPCSync);
                ARM7.IO.Write16(IPCSYNC, SyncIRQEnable);
                ARM7.IO.Write8(HALTCNT, 0x80);
                wokeAt = ARM7.Scheduler.Cycles;
            });

        Assert.Equal(5000, wokeAt);
    }

    [Fact]
    public void AHaltedCPUWhoseInterruptHasArrivedWakesWhenTheOtherHalts()
    {
        long wokeAt = -1;

        SystemClock.Run(
            () =>
            {
                ARM9.IO.Write32(IME, 1);
                ARM9.IO.Write32(IE, (uint)Interrupt.IPCSync);
                ARM9.IO.Write16(IPCSYNC, SyncIRQEnable);
                for (int step = 0; step < 50; step++)
                {
                    Spend(Processor.ARM9, 100);
                }

                ARM9.IO.Write16(IPCSYNC, (ushort)(SyncIRQEnable | SyncSendIRQ));
                ARM9.CP15.Write(0, 7, 0, 4, 0);
                wokeAt = ARM9.Scheduler.Cycles;
            },
            () =>
            {
                ARM7.IO.Write32(IE, (uint)Interrupt.IPCSync);
                ARM7.IO.Write16(IPCSYNC, SyncIRQEnable);
                ARM7.IO.Write8(HALTCNT, 0x80);
                ARM7.IO.Write16(IPCSYNC, SyncSendIRQ);
            });

        Assert.Equal(5000, wokeAt);
    }

    [Fact]
    public void TheARM9HaltNeverWakesWithIMEOff()
    {
        Assert.Throws<InvalidOperationException>(() => SystemClock.Run(
            () =>
            {
                ARM9.IO.Write32(IE, (uint)Interrupt.IPCSync);
                ARM9.IO.Write16(IPCSYNC, SyncIRQEnable);
                ARM9.CP15.Write(0, 7, 0, 4, 0);
            },
            () => ARM7.IO.Write16(IPCSYNC, SyncSendIRQ)));
    }

    [Fact]
    public void AThreadThatResumesAnotherAndWaitsCarriesOnOnceItIsResumed()
    {
        var order = new List<string>();

        SystemClock.Run(
            () =>
            {
                var main = SystemClock.CurrentThread(Processor.ARM9);
                var other = SystemClock.CreateThread(Processor.ARM9, () =>
                {
                    order.Add("other");
                    SystemClock.ResumeThreadAndExit(Processor.ARM9, main);
                });

                SystemClock.ResumeThreadAndWait(Processor.ARM9, other);
                order.Add("main");
            },
            () => { });

        Assert.Equal(["other", "main"], order);
    }

    [Fact]
    public void ADestroyedThreadNeverRuns()
    {
        bool ran = false;
        SystemClock.HostThread? destroyed = null;

        SystemClock.Run(
            () =>
            {
                destroyed = SystemClock.CreateThread(Processor.ARM9, () => ran = true);
                SystemClock.DestroyThread(destroyed);
            },
            () => { });

        Assert.False(ran);
        Assert.False(destroyed!.Thread.IsAlive);
    }
}
