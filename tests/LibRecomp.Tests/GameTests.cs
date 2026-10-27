using System.Security.Cryptography;
using System.Text;
using NTRModern;

namespace LibRecomp.Tests;

public sealed class GameTests : IDisposable
{
    private static readonly UserSettings Settings = new("Player", "", Language.English, FavoriteColor: 0, BirthdayMonth: 1, BirthdayDay: 1);

    private readonly string _folder = Directory.CreateTempSubdirectory("game").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static byte[] ROM(string title, byte revision = 0)
    {
        byte[] rom = new byte[0x200];
        Encoding.ASCII.GetBytes(title).CopyTo(rom, 0x000);
        rom[0x01E] = revision;
        rom[0x15C] = 0x56;
        rom[0x15D] = 0xCF;
        return rom;
    }

    private string WriteFile(string name, byte[] contents)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    [Fact]
    public void AFileKeepsWhatItHeldBeforeAsItsBackup()
    {
        string path = Path.Combine(_folder, "data.bin");
        BackedUpFile.Write(path, [1]);
        BackedUpFile.Write(path, [2]);

        Assert.Equal([1], File.ReadAllBytes(path + ".bak"));

        File.WriteAllBytes(path, [0xFF]);
        Assert.True(BackedUpFile.TryRead(path, bytes => bytes[0] == 0xFF ? throw new InvalidDataException() : bytes, out var contents));
        Assert.Equal([1], contents);
    }

    [Fact]
    public void TheSaveAndFirmwareAreWrittenOnceTheGameStopsChangingThem()
    {
        string path = Path.Combine(_folder, "saves", "game.sav");
        string firmwarePath = Path.Combine(_folder, "saves", "game.DSLite.firmware");
        Hardware.Reset(Model.DSLite);
        SaveMemory.Initialize(SaveType.EEPROM8K);
        NTRModern.Firmware.Build(Settings, []);
        var save = new SaveFile(path, firmwarePath);

        SaveMemory.Data[0x10] = 0x42;
        SaveMemory.IsModified = true;
        NTRModern.Firmware.IsModified = true;
        for (int frame = 0; frame < 31; frame++)
        {
            save.Update();
        }

        Assert.Equal(0x42, File.ReadAllBytes(path)[0x10]);
        Assert.Equal(NTRModern.Firmware.Contents.ToArray(), File.ReadAllBytes(firmwarePath));

        var reopened = new SaveFile(path, firmwarePath);
        Assert.Equal(0x42, reopened.Save![0x10]);
        Assert.Equal(NTRModern.Firmware.Contents.Length, reopened.Firmware!.Length);
    }

    [Fact]
    public void ROMsAreCheckedAndTheRightOneIsStored()
    {
        byte[] good = ROM("TESTGAME");
        var game = new GameEntry("romtest", "TESTGAME", Convert.ToHexString(SHA1.HashData(good)), SaveType.None);
        Game.RegisterConfigPath(_folder);
        Game.Register(game);

        Assert.False(Game.IsROMValid("romtest"));
        Assert.Equal(ROMValidationError.FailedToOpen, Game.SelectROM(Path.Combine(_folder, "missing.nds"), "romtest"));
        Assert.Equal(ROMValidationError.NotAROM, Game.SelectROM(WriteFile("text.nds", new byte[0x200]), "romtest"));
        Assert.Equal(ROMValidationError.IncorrectROM, Game.SelectROM(WriteFile("other.nds", ROM("OTHERGAME")), "romtest"));
        Assert.Equal(ROMValidationError.IncorrectVersion, Game.SelectROM(WriteFile("revision.nds", ROM("TESTGAME", revision: 1)), "romtest"));
        Assert.False(Game.IsROMValid("romtest"));

        Assert.Equal(ROMValidationError.Good, Game.SelectROM(WriteFile("good.nds", good), "romtest"));
        Assert.True(Game.IsROMValid("romtest"));
    }
}
