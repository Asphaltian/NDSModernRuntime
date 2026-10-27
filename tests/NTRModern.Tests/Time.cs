namespace NTRModern.Tests;

internal static class Time
{
    public static void Spend(Processor processor, long cycles)
    {
        ref long now = ref SystemClock.CyclesOf(processor);
        long end = now + cycles;
        while (now < end)
        {
            now = Math.Min(end, SystemClock.NextEventOf(processor));
            if (now >= SystemClock.NextEventOf(processor))
            {
                SystemClock.Synchronize(processor);
            }
        }
    }
}
