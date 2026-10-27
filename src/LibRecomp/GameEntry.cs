using NTRModern;

namespace LibRecomp;

/// <summary><c>GameEntry</c> is a game that your project can run. Register it with <see cref="Game.Register"/>.</summary>
/// <param name="GameId">The name you use for the game. Its stored ROM, save and firmware get named after it too.</param>
/// <param name="InternalName">
/// The title in the ROM header. If the player picks another version of the same game, this is how they get told
/// it's the wrong version instead of the wrong game.
/// </param>
/// <param name="ROMHash">The SHA-1 of the ROM that the game was recompiled from, in hex.</param>
/// <param name="SaveType">The save chip in the game's cartridge.</param>
public sealed record GameEntry(
    string GameId,
    string InternalName,
    string ROMHash,
    SaveType SaveType);

/// <summary>What <see cref="Game.SelectROM"/> found out about the ROM the player picked.</summary>
public enum ROMValidationError
{
    /// <summary>It's the right ROM, and a copy of it is stored now.</summary>
    Good,

    /// <summary>The file couldn't be read.</summary>
    FailedToOpen,

    /// <summary>The file isn't a DS ROM at all.</summary>
    NotAROM,

    /// <summary>It's a ROM of some other game.</summary>
    IncorrectROM,

    /// <summary>It's the right game, but not the version that your project was recompiled from.</summary>
    IncorrectVersion,

    /// <summary>The ROM itself was fine, but the copy couldn't be stored.</summary>
    OtherError,
}
