using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NTRModern;

namespace LibRecomp;

/// <summary>
/// <c>Game</c> is where your project sets up its game and starts it. First register the game and the generated
/// function tables of both CPUs, and then call <see cref="Start"/> once <see cref="IsROMValid"/> says that the
/// player's ROM is stored.
/// </summary>
/// <example>
/// <code>
/// Game.RegisterConfigPath(dataFolder);
/// Game.Register(game);
/// LibRecomp.ARM9.Recomp.RegisterSections(RecompiledFuncs.ARM9.Funcs.Sections, RecompiledFuncs.ARM9.Funcs.OverlaySections);
/// LibRecomp.ARM7.Recomp.RegisterSections(RecompiledFuncs.ARM7.Funcs.Sections, RecompiledFuncs.ARM7.Funcs.OverlaySections);
///
/// // The first time around, check the player's ROM and keep a copy of it
/// if (!Game.IsROMValid("mygame"))
/// {
///     Game.SelectROM(romPath, "mygame");
/// }
///
/// Game.Start("mygame", Model.DSLite, settings, ShowError);
/// </code>
/// </example>
public static class Game
{
    private const int HeaderSize = 0x170;
    private const int TitleLength = 12;
    private const int LogoChecksumOffset = 0x15C;
    private const ushort LogoChecksum = 0xCF56;

    private static readonly Dictionary<string, GameEntry> Games = [];
    private static (string GameId, byte[] ROM)? _storedROM;

    private static string? _configPath;
    private static SaveFile? _saveFile;
    private static Thread? _gameThread;
    private static volatile bool _isQuitting;

    /// <summary>The folder where stored ROMs, saves and firmware are kept.</summary>
    public static string ConfigPath => _configPath ?? throw new InvalidOperationException("Call RegisterConfigPath first.");

    /// <summary>The game that <see cref="Start"/> is running, or null if nothing has been started yet.</summary>
    public static GameEntry? Current { get; private set; }

    /// <summary>Sets <see cref="ConfigPath"/>. Make sure you call this before anything else.</summary>
    public static void RegisterConfigPath(string path)
    {
        _configPath = path;
    }

    /// <summary>Adds a game that your project can run.</summary>
    public static void Register(GameEntry game)
    {
        if (!Games.TryAdd(game.GameId, game))
        {
            throw new ArgumentException($"A game with the id {game.GameId} is already registered.", nameof(game));
        }
    }

    /// <summary>
    /// Checks the ROM that the player picked for a game, and keeps a copy of it if it's the right one. The player only
    /// has to do this once, since <see cref="Start"/> uses the copy from then on.
    /// </summary>
    public static ROMValidationError SelectROM(string romPath, string gameId)
    {
        byte[] rom;
        try
        {
            rom = File.ReadAllBytes(romPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ROMValidationError.FailedToOpen;
        }

        var error = Validate(rom, Games[gameId]);
        if (error != ROMValidationError.Good)
        {
            return error;
        }

        try
        {
            BackedUpFile.Write(StoredROMPath(gameId), rom);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ROMValidationError.OtherError;
        }

        _storedROM = (gameId, rom);
        return ROMValidationError.Good;
    }

    /// <summary>Returns <c>true</c> if the right ROM for a game is already stored.</summary>
    public static bool IsROMValid(string gameId)
    {
        return LoadStoredROM(gameId) is not null;
    }

    /// <summary>
    /// Loads a game's stored ROM, save and firmware, and starts running it in the background. If something stops the
    /// game, the player gets told why through <paramref name="showError"/>. Keep in mind that it can be called from
    /// any thread.
    /// </summary>
    /// <param name="gameId">The game to run.</param>
    /// <param name="model">Which DS to run the game on.</param>
    /// <param name="settings">The settings the player entered in the DS's own menu.</param>
    /// <param name="showError">Tells the player that something went wrong.</param>
    /// <returns><c>false</c> if the game couldn't be started. The player has already been told why by then.</returns>
    public static bool Start(string gameId, Model model, UserSettings settings, Action<string> showError)
    {
        var game = Games[gameId];
        if (LoadStoredROM(gameId) is not { } rom)
        {
            showError("The stored ROM could not be read. Select the ROM again.");
            return false;
        }

        _storedROM = null;
        Current = game;
        var saveFile = new SaveFile(Path.Combine(ConfigPath, "saves", $"{gameId}.sav"), Path.Combine(ConfigPath, "saves", $"{gameId}.{model}.firmware"));
        _saveFile = saveFile;
        Video.FrameFinished += OnFrameFinished;

        _gameThread = new Thread(Run) { Name = "Game", IsBackground = true };
        _gameThread.Start();
        return true;

        void Run()
        {
            try
            {
                Boot.Run(rom, model, game.SaveType, settings, saveFile.Firmware, saveFile.Save, paced: true);
                saveFile.Flush();
            }
            catch (GameQuit)
            {
            }
            catch (Exception e)
            {
                saveFile.Flush();
                showError(e.ToString());
                Environment.Exit(1);
            }
        }
    }

    /// <summary>
    /// Stops the game at the end of its current frame and writes its save and firmware. Don't forget to call this
    /// before your program exits, otherwise the player can lose their progress.
    /// </summary>
    public static void Quit()
    {
        _isQuitting = true;
        if (_gameThread is { IsAlive: true } && _gameThread.Join(TimeSpan.FromSeconds(1)))
        {
            return;
        }

        _saveFile?.Flush();
    }

    private static void OnFrameFinished(VideoFrame frame)
    {
        _saveFile!.Update();
        if (_isQuitting)
        {
            _saveFile.Flush();
            throw new GameQuit();
        }
    }

    private static string StoredROMPath(string gameId) => Path.Combine(ConfigPath, $"{gameId}.nds");

    private static byte[]? LoadStoredROM(string gameId)
    {
        if (_storedROM is { } stored && stored.GameId == gameId)
        {
            return stored.ROM;
        }

        if (!BackedUpFile.TryRead(StoredROMPath(gameId), contents => contents, out var rom) || Validate(rom, Games[gameId]) != ROMValidationError.Good)
        {
            return null;
        }

        _storedROM = (gameId, rom);
        return rom;
    }

    private static ROMValidationError Validate(byte[] rom, GameEntry game)
    {
        if (rom.Length < HeaderSize || BinaryPrimitives.ReadUInt16LittleEndian(rom.AsSpan(LogoChecksumOffset)) != LogoChecksum)
        {
            return ROMValidationError.NotAROM;
        }

        if (Convert.ToHexString(SHA1.HashData(rom)).Equals(game.ROMHash, StringComparison.OrdinalIgnoreCase))
        {
            return ROMValidationError.Good;
        }

        string title = Encoding.ASCII.GetString(rom, 0, TitleLength).TrimEnd('\0');
        return title == game.InternalName ? ROMValidationError.IncorrectVersion : ROMValidationError.IncorrectROM;
    }

    private sealed class GameQuit : Exception;
}
