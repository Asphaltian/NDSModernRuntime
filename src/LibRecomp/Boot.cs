using System.Buffers.Binary;
using NTRModern;

namespace LibRecomp;

/// <summary>
/// <c>Boot</c> starts a game straight from its ROM, the same way the DS does once you pick the cartridge in its menu.
/// Usually you'd go through <see cref="Game.Start"/> instead, but this is handy for tests and tools. Don't forget to
/// register the generated function tables of both CPUs first.
/// </summary>
/// <example>
/// <code>
/// LibRecomp.ARM9.Recomp.RegisterSections(RecompiledFuncs.ARM9.Funcs.Sections, RecompiledFuncs.ARM9.Funcs.OverlaySections);
/// LibRecomp.ARM7.Recomp.RegisterSections(RecompiledFuncs.ARM7.Funcs.Sections, RecompiledFuncs.ARM7.Funcs.OverlaySections);
///
/// // This only returns once both CPUs stop, or throws if the game runs into something unsupported
/// Boot.Run(rom, Model.DSLite, SaveType.EEPROM64K, settings);
/// </code>
/// </example>
public static class Boot
{
    private const int HeaderSize = 0x170;
    private const uint HeaderAddress = 0x027FFE00;
    private const uint ARM9Header = 0x20;
    private const uint ARM7Header = 0x30;
    private const int SecureAreaCRC = 0x6C;
    private const int HeaderCRC = 0x15E;
    private const int Logo = 0xC0;
    private const uint UserSettingsAddress = 0x027FFC80;
    private const uint IRQMode = 0x12;
    private const uint SupervisorMode = 0x13;
    private const uint SOUNDBIAS = 0x04000504;
    private const ushort FirmwareSoundBias = 0x200;

    /// <summary>
    /// Loads the game from <paramref name="rom"/> and runs it on a <paramref name="model"/>, with
    /// <paramref name="settings"/> as the player's DS settings.
    /// </summary>
    /// <param name="rom">The game.</param>
    /// <param name="model">Which DS to run the game on.</param>
    /// <param name="saveType">The save chip in the game's cartridge.</param>
    /// <param name="settings">The settings the player entered in the DS's own menu.</param>
    /// <param name="firmware">
    /// The firmware to start from. This can be a dump from a DS of the same model, or the firmware that a game changed
    /// on an earlier run. Leave it out to start a DS Lite or DSi with new firmware. Please note that an original DS
    /// always needs one.
    /// </param>
    /// <param name="save">What the save chip held at the end of an earlier run. Leave it out to start with an empty save.</param>
    /// <param name="paced">Set this to <c>true</c> to run at the DS's own speed, instead of as fast as your computer can.</param>
    public static void Run(byte[] rom, Model model, SaveType saveType, UserSettings settings, byte[]? firmware = null, byte[]? save = null, bool paced = false)
    {
        if (rom.Length < HeaderSize)
        {
            throw new ArgumentException("The ROM is too small to have a cartridge header.", nameof(rom));
        }

        Hardware.Reset(model);
        SaveMemory.Initialize(saveType);
        if (save is not null)
        {
            SaveMemory.Load(save);
        }

        GameCard.Insert(rom);
        CPU.ARM9.Threads.Reset();
        CPU.ARM7.Threads.Reset();
        Firmware.Build(settings, firmware);
        var arm9 = Load(rom, ARM9Header, CPU.ARM9);
        var arm7 = Load(rom, ARM7Header, CPU.ARM7);

        for (int offset = 0; offset < HeaderSize; offset++)
        {
            CPU.ARM9.Write8(HeaderAddress + (uint)offset, rom[offset]);
        }

        rom.AsSpan(Logo, NTRModern.ARM9.Memory.BIOSLogo.Length).CopyTo(NTRModern.ARM9.Memory.BIOSLogo);

        ushort headerCRC = BinaryPrimitives.ReadUInt16LittleEndian(rom.AsSpan(HeaderCRC));
        ushort secureAreaCRC = BinaryPrimitives.ReadUInt16LittleEndian(rom.AsSpan(SecureAreaCRC));
        foreach (uint area in (uint[])[0x027FF800, 0x027FFC00])
        {
            CPU.ARM9.Write32(area, GameCard.ChipID);
            CPU.ARM9.Write32(area + 0x04, GameCard.ChipID);
            CPU.ARM9.Write16(area + 0x08, headerCRC);
            CPU.ARM9.Write16(area + 0x0A, secureAreaCRC);
        }

        CPU.ARM9.Write16(0x027FF850, 0x5835);
        CPU.ARM9.Write16(0x027FFC10, 0x5835);
        CPU.ARM9.Write32(0x027FF880, 7);
        CPU.ARM9.Write32(0x027FF884, 6);
        CPU.ARM9.Write16(0x027FFC40, 1);
        for (int offset = 0; offset < Firmware.UserSettingsSize; offset++)
        {
            CPU.ARM9.Write8(UserSettingsAddress + (uint)offset, Firmware.CurrentUserSettings[offset]);
        }

        (NTRModern.ARM9.Scheduler.Cycles, NTRModern.ARM7.Scheduler.Cycles) = (0, 0);
        CPU.ARM7.Write16(SOUNDBIAS, FirmwareSoundBias);
        Video.Start(paced);
        Sound.Start();

        var arm9Context = CreateContext(Processor.ARM9, arm9, 0x03002F7C, 0x03003F80, 0x03003FC0);
        var arm7Context = CreateContext(Processor.ARM7, arm7, 0x0380FD80, 0x0380FF80, 0x0380FFC0);
        SystemClock.Run(() => Start(CPU.ARM9, arm9, arm9Context), () => Start(CPU.ARM7, arm7, arm7Context));
    }

    private static uint Load(byte[] rom, uint header, CPU cpu)
    {
        uint romOffset = BinaryPrimitives.ReadUInt32LittleEndian(rom.AsSpan((int)header));
        uint entry = BinaryPrimitives.ReadUInt32LittleEndian(rom.AsSpan((int)header + 4));
        uint address = BinaryPrimitives.ReadUInt32LittleEndian(rom.AsSpan((int)header + 8));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(rom.AsSpan((int)header + 12));
        if ((ulong)romOffset + size > (ulong)rom.Length)
        {
            throw new ArgumentException($"The {cpu.Processor} boot code goes past the end of the ROM.", nameof(rom));
        }

        for (uint offset = 0; offset < size; offset++)
        {
            cpu.Write8(address + offset, rom[romOffset + offset]);
        }

        return entry;
    }

    private static RecompContext CreateContext(Processor processor, uint entry, uint stack, uint irqStack, uint supervisorStack)
    {
        var ctx = new RecompContext(processor) { R12 = entry, R13 = stack, R14 = entry };
        ctx.SetBankedR13(IRQMode, irqStack);
        ctx.SetBankedR13(SupervisorMode, supervisorStack);
        return ctx;
    }

    private static void Start(CPU cpu, uint entry, RecompContext ctx)
    {
        cpu.Functions.Lookup(entry)(ctx);
        cpu.Functions.ThrowIfUnwinding();
    }
}
