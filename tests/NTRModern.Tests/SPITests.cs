using System.Buffers.Binary;

namespace NTRModern.Tests;

public class SPITests
{
    private const uint SPICNT = 0x040001C0;
    private const uint SPIDATA = 0x040001C2;
    private const uint IF = 0x04000214;
    private const ushort Enable = 1 << 15;
    private const ushort IRQEnable = 1 << 14;
    private const ushort Hold = 1 << 11;
    private const ushort Firmware = 1 << 8;
    private const ushort Busy = 1 << 7;
    private const byte PP = 0x02;
    private const byte RDSR = 0x05;
    private const byte WREN = 0x06;
    private const byte PW = 0x0A;
    private const byte DP = 0xB9;
    private const byte RDP = 0xAB;
    private const byte SE = 0xD8;
    private const byte PE = 0xDB;

    private static readonly UserSettings Settings = new("Chimecho", "Hello", Language.German, FavoriteColor: 3, BirthdayMonth: 7, BirthdayDay: 21);

    public SPITests()
    {
        Hardware.Reset(Model.DSLite);
        NTRModern.Firmware.Build(Settings, []);
    }

    private static byte Transfer(byte value, bool hold = true)
    {
        ARM7.IO.Write16(SPICNT, (ushort)(Enable | Firmware | (hold ? Hold : 0)));
        ARM7.IO.Write16(SPIDATA, value);
        return (byte)ARM7.IO.Read16(SPIDATA);
    }

    private static byte[] ReadFirmware(uint address, int length)
    {
        Transfer(0x03);
        Transfer((byte)(address >> 16));
        Transfer((byte)(address >> 8));
        Transfer((byte)address);
        return [.. Enumerable.Range(0, length).Select(i => Transfer(0, hold: i < length - 1))];
    }

    [Fact]
    public void TheHeaderPointsAtTheUserSettings()
    {
        Assert.Equal([0xC0, 0x7F], ReadFirmware(0x20, 2));
        Assert.Equal([0x20], ReadFirmware(0x1D, 1));
    }

    [Fact]
    public void BothUserSettingsAreasAreValidAndTheSecondIsNewer()
    {
        byte[] first = ReadFirmware(0x3FE00, 0x100);
        byte[] second = ReadFirmware(0x3FF00, 0x100);

        foreach (byte[] area in (byte[][])[first, second])
        {
            Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(area.AsSpan(0x72)), CRC16.Compute(0xFFFF, area.AsSpan(0, 0x70)));
            Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(area));
        }

        Assert.Equal((0, 1), (BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(0x70)), BinaryPrimitives.ReadUInt16LittleEndian(second.AsSpan(0x70))));
        Assert.Equal(second.AsSpan(0, 0x70).ToArray(), NTRModern.Firmware.CurrentUserSettings.ToArray());
    }

    [Fact]
    public void UserSettingsHoldWhatThePlayerChose()
    {
        byte[] area = ReadFirmware(0x3FE00, 0x100);

        Assert.Equal((3, 7, 21), (area[0x02], area[0x03], area[0x04]));
        Assert.Equal("Chimecho", System.Text.Encoding.Unicode.GetString(area, 0x06, 2 * area[0x1A]));
        Assert.Equal("Hello", System.Text.Encoding.Unicode.GetString(area, 0x1C, 2 * area[0x50]));
        Assert.Equal((int)Language.German, area[0x64] & 7);
        Assert.Equal(0xEC00, BinaryPrimitives.ReadUInt16LittleEndian(area.AsSpan(0x64)) & 0xFE00);
    }

    [Fact]
    public void TheCalibrationTurnsADCValuesBackIntoPixels()
    {
        byte[] area = ReadFirmware(0x3FE00, 0x100);
        int adcX1 = BinaryPrimitives.ReadUInt16LittleEndian(area.AsSpan(0x58));
        int adcX2 = BinaryPrimitives.ReadUInt16LittleEndian(area.AsSpan(0x5E));
        int screenX1 = area[0x5C];
        int screenX2 = area[0x62];

        int ToPixel(int adc) => ((adc - adcX1) * (screenX2 - screenX1) / (adcX2 - adcX1)) + (screenX1 - 1);

        Assert.Equal((0, 128, 254), (ToPixel(0), ToPixel(128 * 16), ToPixel(254 * 16)));
    }

    [Fact]
    public void TheFlashIdentifiesAsTheDSLitesM35PE20()
    {
        Transfer(0x9F);

        Assert.Equal([0x20, 0x50, 0x12], new[] { Transfer(0), Transfer(0), Transfer(0, hold: false) });
    }

    [Fact]
    public void WriteEnableShowsInTheStatusRegister()
    {
        Transfer(0x06, hold: false);
        Transfer(0x05);
        Assert.Equal(0x02, Transfer(0, hold: false));

        Transfer(0x04, hold: false);
        Transfer(0x05);
        Assert.Equal(0x00, Transfer(0, hold: false));
    }

    [Fact]
    public void ReleasingTheChipselectEndsTheInstruction()
    {
        ReadFirmware(0x20, 1);

        Assert.Throws<NotSupportedException>(() => Transfer(0x00));
    }

    [Theory]
    [InlineData(0, 8 * 67027964 / 4_000_000)]
    [InlineData(3, 8 * 67027964 / 512_000)]
    public void ATransferIsBusyForEightBitsAtItsBaudRate(int baudRate, long cycles)
    {
        ARM7.IO.Write16(SPICNT, (ushort)(Enable | IRQEnable | Firmware | Hold | baudRate));
        ARM7.IO.Write16(SPIDATA, 0x05);

        ARM7.Scheduler.Cycles = ARM9.Scheduler.Cycles = cycles - 1;
        SystemClock.Synchronize(Processor.ARM7);
        Assert.Equal(Busy, ARM7.IO.Read16(SPICNT) & Busy);
        Assert.Equal(0u, ARM7.IO.Read32(IF));

        ARM7.Scheduler.Cycles = ARM9.Scheduler.Cycles = cycles;
        SystemClock.Synchronize(Processor.ARM7);
        Assert.Equal(0, ARM7.IO.Read16(SPICNT) & Busy);
        Assert.Equal((uint)Interrupt.SPI, ARM7.IO.Read32(IF));
    }

    [Fact]
    public void SettingsOutsideTheirRangesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => NTRModern.Firmware.Build(Settings with { Nickname = "Twelve chars" }, []));
        Assert.Throws<ArgumentException>(() => NTRModern.Firmware.Build(Settings with { BirthdayMonth = 13 }, []));
    }

    private static byte[] DSDump(byte rfChipType = 2)
    {
        byte[] dump = new byte[0x40000];
        Array.Fill(dump, (byte)0xFF);
        BinaryPrimitives.WriteUInt16LittleEndian(dump.AsSpan(0x2C), 0x0138);
        dump[0x2F] = 3;
        (dump[0x40], dump[0x41], dump[0x42]) = (rfChipType, 0x18, 0x0C);
        BinaryPrimitives.WriteUInt16LittleEndian(dump.AsSpan(0x2A), CRC16.Compute(0, dump.AsSpan(0x2C, 0x138)));
        return dump;
    }

    [Theory]
    [InlineData(Model.DS, 0xFF, 0x3FE00, 0x40)]
    [InlineData(Model.DSLite, 0x20, 0x3FE00, 0x50)]
    [InlineData(Model.DSi, 0x57, 0x1FE00, 0x40)]
    public void EachModelHasItsOwnConsoleTypeFlashChipAndUserSettingsPlace(Model model, byte consoleType, uint userSettings, byte deviceType)
    {
        Hardware.Reset(model);
        NTRModern.Firmware.Build(Settings, model == Model.DS ? DSDump() : []);

        Assert.Equal(consoleType, ReadFirmware(0x1D, 1)[0]);
        Assert.Equal(userSettings / 8, BinaryPrimitives.ReadUInt16LittleEndian(ReadFirmware(0x20, 2)));
        byte[] area = ReadFirmware(userSettings, 0x74);
        Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(area.AsSpan(0x72)), CRC16.Compute(0xFFFF, area.AsSpan(0, 0x70)));

        Transfer(0x9F);
        Assert.Equal(deviceType, new[] { Transfer(0), Transfer(0), Transfer(0, hold: false) }[1]);
    }

    [Fact]
    public void AnOriginalDSNeedsAFirmwareDumpForItsWifiCalibration()
    {
        Hardware.Reset(Model.DS);

        Assert.Throws<ArgumentException>(() => NTRModern.Firmware.Build(Settings, []));
    }

    [Fact]
    public void TheWifiCalibrationComesFromTheDump()
    {
        Hardware.Reset(Model.DS);
        byte[] dump = DSDump();

        NTRModern.Firmware.Build(Settings, dump);

        Assert.Equal(dump.AsSpan(0x2A, 0x200 - 0x2A).ToArray(), ReadFirmware(0x2A, 0x200 - 0x2A));
    }

    [Fact]
    public void ADumpFromAnotherModelOrWithDamagedCalibrationIsRejected()
    {
        Hardware.Reset(Model.DSLite);
        Assert.Throws<ArgumentException>(() => NTRModern.Firmware.Build(Settings, DSDump()));

        Hardware.Reset(Model.DS);
        byte[] damaged = DSDump();
        damaged[0x40] ^= 1;
        Assert.Throws<ArgumentException>(() => NTRModern.Firmware.Build(Settings, damaged));
        Assert.Throws<ArgumentException>(() => NTRModern.Firmware.Build(Settings, DSDump(rfChipType: 3)));
    }

    [Theory]
    [InlineData(Model.DSLite, 5)]
    [InlineData(Model.DSi, 15)]
    public void TheBuiltInWifiCalibrationIsValid(Model model, byte version)
    {
        Hardware.Reset(model);
        NTRModern.Firmware.Build(Settings, []);

        byte[] header = ReadFirmware(0, 0x200);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x2C));
        Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x2A)), CRC16.Compute(0, header.AsSpan(0x2C, length)));
        Assert.Equal((0x138, version, (byte)3), (length, header[0x2F], header[0x40]));
    }

    [Fact]
    public void TheDSiFirmwareHasItsOwnHeaderAndExtendedUserSettings()
    {
        Hardware.Reset(Model.DSi);
        NTRModern.Firmware.Build(Settings, []);

        byte[] header = ReadFirmware(0, 0x300);
        Assert.All(header.AsSpan(0, 0x1D).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal([0x01, 0x20, 0xFF], header.AsSpan(0x1FD, 3).ToArray());
        Assert.Equal(0x80, header[0x2FF]);

        byte[] area = ReadFirmware(0x1FE00, 0x100);
        Assert.Equal((1, (byte)Language.German), (area[0x74], area[0x75]));
        Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(area.AsSpan(0xFE)), CRC16.Compute(0xFFFF, area.AsSpan(0x74, 0x8A)));
    }

    [Fact]
    public void DevicesThatArentSupportedThrow()
    {
        ARM7.IO.Write16(SPICNT, Enable | (3 << 8));

        Assert.Throws<NotSupportedException>(() => ARM7.IO.Write16(SPIDATA, 0x80));
    }

    private static void Send(params byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            Transfer(bytes[i], hold: i < bytes.Length - 1);
        }
    }

    private static void WaitForWrite() => ARM7.Scheduler.Cycles += SystemClock.CyclesPerSecond * 2;

    [Fact]
    public void PageWriteReplacesBytesAfterWREN()
    {
        Send(WREN);
        Send(PW, 0x03, 0xFA, 0x10, 0x12, 0x34);
        WaitForWrite();

        Assert.Equal([0x12, 0x34, 0xFF], ReadFirmware(0x3FA10, 3));
        Assert.True(NTRModern.Firmware.IsModified);
        Assert.Equal(0, Status());
    }

    [Fact]
    public void AWriteKeepsTheChipBusyForGBATEKsTypicalTime()
    {
        Send(WREN);
        Send(PW, 0x03, 0xFA, 0x00, 0x00);
        long elevenMilliseconds = SystemClock.CyclesPerSecond * 11 / 1000;

        ARM7.Scheduler.Cycles += elevenMilliseconds - 1;
        Assert.Equal(1, Status() & 1);
        Assert.Throws<NotSupportedException>(() => ReadFirmware(0x3FA00, 1));

        ARM7.Scheduler.Cycles += 1;
        Assert.Equal(0, Status() & 1);
    }

    [Fact]
    public void PageProgramOnlyClearsBits()
    {
        Send(WREN);
        Send(PW, 0x03, 0xFA, 0x00, 0xF0);
        WaitForWrite();
        Send(WREN);
        Send(PP, 0x03, 0xFA, 0x00, 0x3C);
        WaitForWrite();

        Assert.Equal([0x30], ReadFirmware(0x3FA00, 1));
    }

    [Fact]
    public void ErasesFillWithFF()
    {
        Send(WREN);
        Send(PW, 0x03, 0xFA, 0x00, 0x00);
        WaitForWrite();
        Send(WREN);
        Send(PE, 0x03, 0xFA, 0x00);
        WaitForWrite();
        Assert.Equal([0xFF], ReadFirmware(0x3FA00, 1));

        Send(WREN);
        Send(SE, 0x03, 0x00, 0x00);
        WaitForWrite();
        Assert.Equal([0xFF, 0xFF], ReadFirmware(0x3FE00, 2));
    }

    [Fact]
    public void WritesNeedWRENAndStayInTheirPage()
    {
        Assert.Throws<NotSupportedException>(() => Send(PW, 0x03, 0xFA, 0x00, 0x00));

        NTRModern.Firmware.Deselect();
        Send(WREN);
        Assert.Throws<NotSupportedException>(() => Send(PW, 0x03, 0xFA, 0xFF, 0x00, 0x00));
    }

    [Fact]
    public void TheFirstPagesAreProtected()
    {
        Send(WREN);

        Assert.Throws<NotSupportedException>(() => Send(PW, 0x00, 0x80, 0x00, 0x00));
    }

    [Fact]
    public void DeepPowerDownIgnoresWholeInstructions()
    {
        Send(DP);
        Send(0x03, 0xAB, 0xAB, 0xAB);
        Send(RDP);

        Assert.Equal([0x20], ReadFirmware(0x1D, 1));
    }

    [Fact]
    public void FirmwareFromAnEarlierRunKeepsTheGamesWrites()
    {
        Send(WREN);
        Send(PW, 0x03, 0xFA, 0x00, 0x5A);
        WaitForWrite();
        byte[] earlier = NTRModern.Firmware.Contents.ToArray();

        Hardware.Reset(Model.DSLite);
        NTRModern.Firmware.Build(Settings with { Nickname = "Kecleon" }, earlier);

        Assert.Equal([0x5A], ReadFirmware(0x3FA00, 1));
        Assert.Equal("Kecleon", System.Text.Encoding.Unicode.GetString(ReadFirmware(0x3FF06, 14)));
        Assert.False(NTRModern.Firmware.IsModified);
    }

    private static byte Status()
    {
        Transfer(RDSR);
        return Transfer(0, hold: false);
    }
}
