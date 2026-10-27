namespace NTRModern;

/// <summary>Languages the player can pick in the DS's own settings menu.</summary>
public enum Language
{
    Japanese,
    English,
    French,
    German,
    Italian,
    Spanish,
}

/// <summary>
/// <c>UserSettings</c> holds what the player would normally enter in the DS's own settings menu, like their nickname
/// and birthday. Keep in mind that games can read all of it.
/// </summary>
/// <example>
/// <code>
/// var settings = new UserSettings("Kecleon", "Welcome!", Language.English, FavoriteColor: 4, BirthdayMonth: 9, BirthdayDay: 12);
/// Boot.Run(rom, Model.DSLite, SaveType.EEPROM64K, settings);
/// </code>
/// </example>
/// <param name="Nickname">Up to 10 characters.</param>
/// <param name="Message">Up to 26 characters.</param>
/// <param name="Language">The language that games show their text in, as long as they have it.</param>
/// <param name="FavoriteColor">From 0 to 15, where 0 is gray and 1 is brown.</param>
/// <param name="BirthdayMonth">From 1 to 12.</param>
/// <param name="BirthdayDay">From 1 to 31.</param>
public sealed record UserSettings(string Nickname, string Message, Language Language, int FavoriteColor, int BirthdayMonth, int BirthdayDay);
