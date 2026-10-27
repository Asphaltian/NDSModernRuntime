namespace NTRModern;

/// <summary>Save chips that a DS cartridge can have. Make sure you pick the one that the game's cartridge has.</summary>
public enum SaveType
{
    /// <summary>No save chip.</summary>
    None,

    /// <summary>A 512-byte EEPROM.</summary>
    EEPROM512,

    /// <summary>An 8 KB EEPROM.</summary>
    EEPROM8K,

    /// <summary>A 64 KB EEPROM.</summary>
    EEPROM64K,

    /// <summary>A 128 KB EEPROM.</summary>
    EEPROM128K,
}

internal static class SaveMemory
{
    public const byte Undriven = 0xFF;

    private const byte Erased = 0xFF;

    private static byte[] _data = [];

    public static SaveType Type { get; private set; }

    public static bool IsModified { get; set; }

    internal static Span<byte> Data => _data;

    public static void Initialize(SaveType type)
    {
        Type = type;
        _data = new byte[SizeOf(type)];
        _data.AsSpan().Fill(Erased);
        IsModified = false;
        EEPROM.Reset();
    }

    public static void Load(ReadOnlySpan<byte> contents)
    {
        _data.AsSpan().Fill(Erased);
        contents[..Math.Min(contents.Length, _data.Length)].CopyTo(_data);
    }

    public static byte Transfer(byte value) => Type == SaveType.None ? Undriven : EEPROM.Transfer(value);

    public static void Deselect() => EEPROM.Deselect();

    private static int SizeOf(SaveType type) => type switch
    {
        SaveType.EEPROM512 => 0x200,
        SaveType.EEPROM8K => 0x2000,
        SaveType.EEPROM64K => 0x10000,
        SaveType.EEPROM128K => 0x20000,
        _ => 0,
    };
}
