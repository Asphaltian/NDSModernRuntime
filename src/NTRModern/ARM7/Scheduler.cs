namespace NTRModern.ARM7;

/// <summary><c>Scheduler</c> keeps track of time on the ARM7, which is counted in cycles.</summary>
public static class Scheduler
{
    /// <summary>
    /// How many cycles the ARM7 has run so far. If you want to turn it into seconds, keep in mind that there are
    /// 67,027,964 cycles in one second.
    /// </summary>
    public static long Cycles;

    /// <summary>This is read by recompiled code, so you won't need it yourself.</summary>
    public static long NextEvent = long.MaxValue;
}
