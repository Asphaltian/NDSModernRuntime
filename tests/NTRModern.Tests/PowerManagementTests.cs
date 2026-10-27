namespace NTRModern.Tests;

public class PowerManagementTests
{
    private const uint SPICNT = 0x040001C0;
    private const uint SPIDATA = 0x040001C2;
    private const ushort Enable = 1 << 15;
    private const ushort Hold = 1 << 11;
    private const byte Read = 1 << 7;

    private static byte Transfer(byte value, bool hold)
    {
        ARM7.IO.Write16(SPICNT, (ushort)(Enable | (hold ? Hold : 0)));
        ARM7.IO.Write16(SPIDATA, value);
        return (byte)ARM7.IO.Read16(SPIDATA);
    }

    private static byte ReadRegister(int index)
    {
        Transfer((byte)(Read | index), hold: true);
        return Transfer(0, hold: false);
    }

    private static void WriteRegister(int index, byte value)
    {
        Transfer((byte)index, hold: true);
        Transfer(value, hold: false);
    }

    [Fact]
    public void TheDSiReadsGBATEKsValuesInNDSMode()
    {
        Hardware.Reset(Model.DSi);

        Assert.Equal([0x0F, 0x00, 0x00, 0x01, 0x41, 0x0F, 0x00], [ReadRegister(0), ReadRegister(1), ReadRegister(2), ReadRegister(3), ReadRegister(4), ReadRegister(0x10), ReadRegister(5)]);
    }

    [Fact]
    public void BothBacklightsAndTheSoundAmplifierAreOnAtBoot()
    {
        Hardware.Reset(Model.DS);

        Assert.Equal(0x0D, ReadRegister(0));
        Assert.Equal(0x00, ReadRegister(2));
    }

    [Fact]
    public void TheOriginalDSMirrorsItsFourRegisters()
    {
        Hardware.Reset(Model.DS);

        WriteRegister(4, 0x0F);

        Assert.Equal(0x0F, ReadRegister(0));
        Assert.Equal(0x0F, ReadRegister(0x7C));
    }

    [Fact]
    public void TheDSLiteMirrorsRegisterFourAndThenTheFirstEight()
    {
        Hardware.Reset(Model.DSLite);

        WriteRegister(4, 0x02);

        Assert.Equal(0x42, ReadRegister(4));
        Assert.Equal(0x42, ReadRegister(7));
        Assert.Equal(0x42, ReadRegister(0x0C));
        Assert.Equal(0x0D, ReadRegister(8));
    }

    [Fact]
    public void TheDSLiteHasNoMuteBit()
    {
        Hardware.Reset(Model.DSLite);

        WriteRegister(0, 0x0F);

        Assert.Equal(0x0D, ReadRegister(0));
    }

    [Fact]
    public void TheBatteryStatusCantBeWritten()
    {
        Hardware.Reset(Model.DS);

        WriteRegister(1, 0x01);

        Assert.Equal(0x00, ReadRegister(1));
    }

    [Fact]
    public void TheMicrophoneGainIsUnknownUntilItsSet()
    {
        Hardware.Reset(Model.DSLite);
        Assert.Throws<NotSupportedException>(() => ReadRegister(3));

        Hardware.Reset(Model.DSLite);
        WriteRegister(3, 0xFE);
        Assert.Equal(0x02, ReadRegister(3));
    }

    [Fact]
    public void SwitchingTheConsoleOffIsntSupported()
    {
        Hardware.Reset(Model.DS);

        Transfer(0, hold: true);

        Assert.Throws<NotSupportedException>(() => Transfer(0x4D, hold: false));
    }
}
