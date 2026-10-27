namespace NTRModern.ARM9;

/// <summary><c>Scheduler</c> keeps track of time on the ARM9, which is counted in cycles.</summary>
public static class Scheduler
{
    /// <summary>
    /// How many cycles the ARM9 has run so far. If you want to turn it into seconds, keep in mind that there are
    /// 67,027,964 cycles in one second.
    /// </summary>
    public static long Cycles;

    /// <summary>This is read by recompiled code, so you won't need it yourself.</summary>
    public static long NextEvent = long.MaxValue;

    /// <summary>
    /// Runs your code on the ARM9 as if no time has passed at all. Usually you want this whenever a patch of yours
    /// calls one of the game's functions, so the game keeps exactly the same timing it would have without your patch.
    /// </summary>
    /// <example>
    /// <code>
    /// // Ask the game for something from a patch without throwing its timing off
    /// Scheduler.RunUntimed(() => Funcs.sub_02051D8C(ctx));
    /// </code>
    /// </example>
    public static void RunUntimed(Action work)
    {
        var instructions = CP15.InstructionCache.Save();
        var data = CP15.DataCache.Save();
        try
        {
            SystemClock.RunUntimed(Processor.ARM9, work);
        }
        finally
        {
            CP15.InstructionCache.Restore(instructions);
            CP15.DataCache.Restore(data);
        }
    }
}
