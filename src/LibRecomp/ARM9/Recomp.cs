namespace LibRecomp.ARM9;

/// <summary>
/// <c>Recomp</c> is where the ARM9's recompiled code gets registered and looked up. Don't forget to pass
/// <c>Funcs.Sections</c> and <c>Funcs.OverlaySections</c> from the generated ARM9 code to
/// <see cref="RegisterSections"/> before the game starts.
/// </summary>
public static class Recomp
{
    /// <summary>This is read by recompiled code, so you won't need it yourself.</summary>
    public static bool IsUnwinding => CPU.ARM9.Functions.IsUnwinding;

    /// <summary>Registers the ARM9's generated function tables.</summary>
    public static void RegisterSections(
        (uint ROMAddress, uint Address, uint Size, (uint Offset, RecompFunc Function)[] Functions)[] sections, int[] overlaySections)
    {
        CPU.ARM9.Functions.Register(sections, overlaySections);
    }

    /// <summary>
    /// Runs the game's <c>OS_SaveContext</c> and remembers the thread it saved, so the game can switch back to that
    /// thread later. To use it, list <c>OS_SaveContext</c> in <c>returns_twice_funcs</c> and point its patch here.
    /// </summary>
    /// <example>
    /// <code>
    /// Funcs.Patches.OS_SaveContext = ctx => Recomp.SaveContext(ctx, Funcs.Original.OS_SaveContext);
    /// </code>
    /// </example>
    public static void SaveContext(RecompContext ctx, RecompFunc saveContext) => CPU.ARM9.Threads.SaveContext(ctx, saveContext);

    /// <summary>
    /// Continues the game at <paramref name="pc"/> with the registers in <paramref name="ctx"/>, on the stack that
    /// <c>ctx.R13</c> points to. Usually you may need this in a patch for game code that switches stacks by itself,
    /// in place of its jump to the other stack. Your patch carries on once the game gets back to
    /// <paramref name="resumePC"/> with <paramref name="resumeSP"/> in R13.
    /// </summary>
    /// <example>
    /// <code>
    /// // Switch to the task whose stack is in taskSP, and carry on here once the game switches back
    /// (uint resumePC, uint resumeSP) = (ctx.R14, ctx.R13);
    /// ctx.R13 = taskSP;
    /// Recomp.ContinueAt(ctx, taskPC, resumePC, resumeSP);
    /// </code>
    /// </example>
    public static void ContinueAt(RecompContext ctx, uint pc, uint resumePC, uint resumeSP)
    {
        CPU.ARM9.Threads.ContinueAt(ctx, pc, (resumePC & ~1u, resumeSP));
    }

    /// <summary>
    /// Runs the game's <c>CARDi_ReadRom</c>, and lets the game call the code of any overlay that it loads. Point the
    /// patch of <c>CARDi_ReadRom</c> here, otherwise the game can't run any overlay code.
    /// </summary>
    /// <example>
    /// <code>
    /// Funcs.Patches.CARDi_ReadRom = ctx => Recomp.ReadRom(ctx, Funcs.Original.CARDi_ReadRom);
    /// </code>
    /// </example>
    public static void ReadRom(RecompContext ctx, RecompFunc readRom)
    {
        CPU.ARM9.Functions.LoadOverlays(ctx.R1, ctx.R2, ctx.R3);
        readRom(ctx);
    }

    /// <summary>Returns the ARM9 function at an address. Please note that it throws if there isn't one there.</summary>
    public static RecompFunc LookupFunc(uint address) => CPU.ARM9.Functions.Lookup(address);

    /// <summary>This is called by recompiled code, so you won't need it yourself.</summary>
    public static void IndirectJump(RecompContext ctx, uint target) => CPU.ARM9.Functions.IndirectJump(ctx, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static void TailJump(RecompContext ctx, uint link, uint returnAddress) => CPU.ARM9.Functions.TailJump(ctx, link, returnAddress);

    /// <inheritdoc cref="IndirectJump"/>
    public static bool TryResumeAt(uint address) => CPU.ARM9.Functions.TryResumeAt(address);

    /// <inheritdoc cref="IndirectJump"/>
    public static Exception SwitchError(string function, uint address, uint target) => Functions.SwitchError(function, address, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static Exception JumpError(string function, uint address, uint target) => Functions.JumpError(function, address, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static void HandleEvents(RecompContext ctx, uint address) => CPU.ARM9.HandleEvents(ctx, address);

    /// <inheritdoc cref="IndirectJump"/>
    public static void ReturnFromException(RecompContext ctx, uint target) => CPU.ARM9.Threads.ReturnFromException(ctx, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static void SWI(RecompContext ctx, int function) => BIOS.Call(CPU.ARM9, ctx, function);
}
