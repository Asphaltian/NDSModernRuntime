using NTRModern;

namespace LibRecomp;

internal sealed class SaveFile
{
    private const int QuietFramesBeforeWrite = 30;

    private readonly string _path;
    private readonly string _firmwarePath;
    private int _framesUntilWrite;
    private int _framesUntilFirmwareWrite;

    public SaveFile(string path, string firmwarePath)
    {
        _path = path;
        _firmwarePath = firmwarePath;
        Save = BackedUpFile.TryRead(_path, contents => contents, out var save) ? save : null;
        Firmware = BackedUpFile.TryRead(_firmwarePath, contents => contents, out var firmware) ? firmware : null;
    }

    public byte[]? Save { get; }

    public byte[]? Firmware { get; }

    public void Update()
    {
        if (SaveMemory.IsModified)
        {
            SaveMemory.IsModified = false;
            _framesUntilWrite = QuietFramesBeforeWrite;
        }
        else if (_framesUntilWrite > 0 && --_framesUntilWrite == 0)
        {
            WriteSave();
        }

        if (NTRModern.Firmware.IsModified)
        {
            NTRModern.Firmware.IsModified = false;
            _framesUntilFirmwareWrite = QuietFramesBeforeWrite;
        }
        else if (_framesUntilFirmwareWrite > 0 && --_framesUntilFirmwareWrite == 0)
        {
            WriteFirmware();
        }
    }

    public void Flush()
    {
        if (SaveMemory.IsModified || _framesUntilWrite > 0)
        {
            SaveMemory.IsModified = false;
            _framesUntilWrite = 0;
            WriteSave();
        }

        if (NTRModern.Firmware.IsModified || _framesUntilFirmwareWrite > 0)
        {
            NTRModern.Firmware.IsModified = false;
            _framesUntilFirmwareWrite = 0;
            WriteFirmware();
        }
    }

    private void WriteSave()
    {
        if (SaveMemory.Data.Length > 0)
        {
            BackedUpFile.Write(_path, SaveMemory.Data);
        }
    }

    private void WriteFirmware()
    {
        BackedUpFile.Write(_firmwarePath, NTRModern.Firmware.Contents);
    }
}
