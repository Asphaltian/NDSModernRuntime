namespace LibRecomp.ARM7;

/// <summary>
/// <c>Recomp</c> is where the ARM7's recompiled code gets registered and looked up. Don't forget to pass
/// <c>Funcs.Sections</c> and <c>Funcs.OverlaySections</c> from the generated ARM7 code to
/// <see cref="RegisterSections"/> before the game starts.
/// </summary>
public static class Recomp
{
    /// <summary>This is read by recompiled code, so you won't need it yourself.</summary>
    public static bool IsUnwinding => CPU.ARM7.Functions.IsUnwinding;

    /// <summary>Registers the ARM7's generated function tables.</summary>
    public static void RegisterSections(
        (uint ROMAddress, uint Address, uint Size, (uint Offset, RecompFunc Function)[] Functions)[] sections, int[] overlaySections)
    {
        CPU.ARM7.Functions.Register(sections, overlaySections);
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
    public static void SaveContext(RecompContext ctx, RecompFunc saveContext) => CPU.ARM7.Threads.SaveContext(ctx, saveContext);

    /// <summary>Returns the ARM7 function at an address. Please note that it throws if there isn't one there.</summary>
    public static RecompFunc LookupFunc(uint address) => CPU.ARM7.Functions.Lookup(address);

    /// <summary>This is called by recompiled code, so you won't need it yourself.</summary>
    public static void IndirectJump(RecompContext ctx, uint target) => CPU.ARM7.Functions.IndirectJump(ctx, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static void TailJump(RecompContext ctx, uint link, uint returnAddress) => CPU.ARM7.Functions.TailJump(ctx, link, returnAddress);

    /// <inheritdoc cref="IndirectJump"/>
    public static bool TryResumeAt(uint address) => CPU.ARM7.Functions.TryResumeAt(address);

    /// <inheritdoc cref="IndirectJump"/>
    public static Exception SwitchError(string function, uint address, uint target) => Functions.SwitchError(function, address, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static Exception JumpError(string function, uint address, uint target) => Functions.JumpError(function, address, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static void HandleEvents(RecompContext ctx, uint address) => CPU.ARM7.HandleEvents(ctx, address);

    /// <inheritdoc cref="IndirectJump"/>
    public static void ReturnFromException(RecompContext ctx, uint target) => CPU.ARM7.Threads.ReturnFromException(ctx, target);

    /// <inheritdoc cref="IndirectJump"/>
    public static void SWI(RecompContext ctx, int function) => BIOS.Call(CPU.ARM7, ctx, function);
}
