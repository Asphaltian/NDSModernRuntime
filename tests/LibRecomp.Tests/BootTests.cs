using System.Buffers.Binary;
using NTRModern;

namespace LibRecomp.Tests;

public class BootTests
{
    private const uint ARM9Entry = 0x02000000;
    private const uint ARM7Entry = 0x02380000;
    private const uint IRQMode = 0x12;
    private const uint SupervisorMode = 0x13;

    private static readonly UserSettings Settings = new("Chimecho", "Hello", Language.French, FavoriteColor: 3, BirthdayMonth: 7, BirthdayDay: 21);

    private readonly Dictionary<Processor, uint[]> _registers = [];

    public BootTests()
    {
        ARM9.Recomp.RegisterSections([(0x200, ARM9Entry, 0x10, [(0x0, Record)])], []);
        ARM7.Recomp.RegisterSections([(0x300, ARM7Entry, 0x10, [(0x0, Record)])], []);
    }

    private static byte[] ROM()
    {
        byte[] rom = new byte[0x400];
        var header = rom.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x20..], 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x24..], ARM9Entry);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x28..], ARM9Entry);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x2C..], 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x30..], 0x300);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x34..], ARM7Entry);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x38..], ARM7Entry);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x3C..], 0x10);
        header[0x14] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x60..], 0x00586000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x64..], 0x001808F8);
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x6C..], 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x15E..], 0xABCD);
        rom[0x200] = 0x99;
        rom[0x300] = 0x77;
        rom[0xC0] = 0x24;
        rom[0x15B] = 0x42;
        return rom;
    }

    private void Record(RecompContext ctx)
    {
        uint[] registers = [ctx.R0, ctx.R1, ctx.R2, ctx.R3, ctx.R4, ctx.R5, ctx.R6, ctx.R7, ctx.R8, ctx.R9, ctx.R10, ctx.R11, ctx.R12, ctx.R13, ctx.R14, 0, 0, ctx.CPSR];
        ctx.WriteCPSR(IRQMode, 0b0001);
        registers[15] = ctx.R13;
        ctx.WriteCPSR(SupervisorMode, 0b0001);
        registers[16] = ctx.R13;
        _registers[ctx.Processor] = registers;
    }

    [Fact]
    public void BothBinariesAreLoadedAndStarted()
    {
        Boot.Run(ROM(), Model.DSLite, SaveType.None, Settings);

        Assert.Equal(0x99, NTRModern.ARM9.Memory.Read8(ARM9Entry));
        Assert.Equal(0x77, NTRModern.ARM7.Memory.Read8(ARM7Entry));
        Assert.Equal(2, _registers.Count);
    }

    [Theory]
    [InlineData(Processor.ARM9, ARM9Entry, 0x03002F7Cu, 0x03003F80u, 0x03003FC0u)]
    [InlineData(Processor.ARM7, ARM7Entry, 0x0380FD80u, 0x0380FF80u, 0x0380FFC0u)]
    public void RegistersAreSetTheWayTheFirmwareLeavesThem(Processor processor, uint entry, uint stack, uint irqStack, uint supervisorStack)
    {
        Boot.Run(ROM(), Model.DSLite, SaveType.None, Settings);

        uint[] registers = _registers[processor];
        Assert.All(registers[..12], register => Assert.Equal(0u, register));
        Assert.Equal((entry, stack, entry), (registers[12], registers[13], registers[14]));
        Assert.Equal((irqStack, supervisorStack), (registers[15], registers[16]));
    }

    [Fact]
    public void TheARM9BIOSGetsTheLogoFromTheHeader()
    {
        Boot.Run(ROM(), Model.DSLite, SaveType.None, Settings);

        Assert.Equal((0x24, 0x42), (NTRModern.ARM9.Memory.Read8(0xFFFF0020), NTRModern.ARM9.Memory.Read8(0xFFFF00BB)));
    }

    [Fact]
    public void TheHeaderAndBootValuesAreInMainMemory()
    {
        Boot.Run(ROM(), Model.DSLite, SaveType.None, Settings);

        var memory = NTRModern.ARM9.Memory.Read16;
        Assert.Equal(0xABCD, memory(0x027FFE00 + 0x15E));
        Assert.Equal((0xABCD, 0x1234), (memory(0x027FF808), memory(0x027FF80A)));
        Assert.Equal((0xABCD, 0x1234), (memory(0x027FFC08), memory(0x027FFC0A)));
        Assert.Equal((0x5835, 0x5835), (memory(0x027FF850), memory(0x027FFC10)));
        Assert.Equal((7u, 6u), (NTRModern.ARM9.Memory.Read32(0x027FF880), NTRModern.ARM9.Memory.Read32(0x027FF884)));
        Assert.Equal(1, memory(0x027FFC40));
        Assert.All((uint[])[0x027FF800, 0x027FF804, 0x027FFC00, 0x027FFC04], address => Assert.Equal(0xC2u, NTRModern.ARM9.Memory.Read32(address)));
    }

    [Fact]
    public void TheSoundBiasIsWhereTheFirmwareLeavesIt()
    {
        Boot.Run(ROM(), Model.DSLite, SaveType.None, Settings);

        Assert.Equal(0x200, NTRModern.ARM7.Memory.Read16(0x04000504));
    }

    [Fact]
    public void TheCurrentUserSettingsAreInMainMemory()
    {
        Boot.Run(ROM(), Model.DSLite, SaveType.None, Settings);

        var read = NTRModern.ARM9.Memory.Read8;
        Assert.Equal((3, 7, 21), (read(0x027FFC82), read(0x027FFC83), read(0x027FFC84)));
        Assert.Equal((ushort)'C', NTRModern.ARM9.Memory.Read16(0x027FFC86));
        Assert.Equal(8, NTRModern.ARM9.Memory.Read16(0x027FFC9A));
        Assert.Equal((int)Language.French, NTRModern.ARM9.Memory.Read16(0x027FFCE4) & 7);
    }

    [Fact]
    public void AROMTooSmallForAHeaderIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Boot.Run(new byte[0x100], Model.DSLite, SaveType.None, Settings));
    }

    [Fact]
    public void BootCodePastTheEndOfTheROMIsRejected()
    {
        byte[] rom = ROM();
        BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x3C), 0x200);

        Assert.Throws<ArgumentException>(() => Boot.Run(rom, Model.DSLite, SaveType.None, Settings));
    }
}
