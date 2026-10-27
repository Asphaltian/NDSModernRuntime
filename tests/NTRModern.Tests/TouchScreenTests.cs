namespace NTRModern.Tests;

public sealed class TouchScreenTests : IDisposable
{
    private const uint SPICNT = 0x040001C0;
    private const uint SPIDATA = 0x040001C2;
    private const uint EXTKEYIN = 0x04000136;
    private const ushort Enable = 1 << 15;
    private const ushort Hold = 1 << 11;
    private const ushort TouchScreenDevice = 2 << 8;
    private const ushort PenUp = 1 << 6;
    private const byte X = 0xD0;
    private const byte Y = 0x90;
    private const byte Temperature0 = 0x84;
    private const byte Battery = 0xA4;
    private const byte AUX = 0xE4;
    private const byte SingleEnded = 1 << 2;
    private const byte EightBit = 1 << 3;

    public TouchScreenTests() => Hardware.Reset(Model.DS);

    public void Dispose() => TouchScreen.Release();

    private static byte Transfer(byte value, bool hold = true)
    {
        ARM7.IO.Write16(SPICNT, (ushort)(Enable | TouchScreenDevice | (hold ? Hold : 0)));
        ARM7.IO.Write16(SPIDATA, value);
        return (byte)ARM7.IO.Read16(SPIDATA);
    }

    private static int Read12(byte command)
    {
        Transfer(command);
        int reply = (Transfer(0) << 8) | Transfer(0, hold: false);
        return reply >> 3;
    }

    private static int Read8(byte command)
    {
        Transfer((byte)(command | EightBit));
        int reply = (Transfer(0) << 8) | Transfer(0, hold: false);
        return reply >> 7;
    }

    [Fact]
    public void ThePositionComesBackAsTheTouchedPixelAfterCalibration()
    {
        TouchScreen.Touch(40, 100);

        Assert.Equal(40 << 4, Read12(X));
        Assert.Equal(100 << 4, Read12(Y));
    }

    [Fact]
    public void AReleasedScreenReadsXAsZeroAndYAsFFF()
    {
        Assert.Equal(0x000, Read12(X));
        Assert.Equal(0xFFF, Read12(Y));
    }

    [Fact]
    public void EightBitModeSendsTheTopEightBits()
    {
        TouchScreen.Touch(255, 191);

        Assert.Equal(255, Read8(X));
        Assert.Equal(191, Read8(Y));
    }

    [Fact]
    public void ANewCommandCanStartWhileTheLastReplyFinishes()
    {
        TouchScreen.Touch(1, 2);

        Transfer(X);
        int x = (Transfer(0) << 8) | Transfer(Y);
        int y = (Transfer(0) << 8) | Transfer(0, hold: false);

        Assert.Equal((1 << 4, 2 << 4), (x >> 3, y >> 3));
    }

    [Fact]
    public void ANewCommandCantStartBeforeReplyBitSixIsIn()
    {
        Transfer(X);

        Assert.Throws<NotSupportedException>(() => Transfer(Y));
    }

    [Fact]
    public void DeselectingDropsTheRestOfTheReply()
    {
        Transfer(Y, hold: false);

        Assert.Equal(0, Transfer(0, hold: false));
    }

    [Fact]
    public void ThePenBitFollowsTheTSC2046PowerDownModes()
    {
        TouchScreen.Touch(10, 10);
        Assert.Equal(0, ARM7.IO.Read16(EXTKEYIN) & PenUp);

        Read12(Temperature0 | 1);
        Assert.Equal(PenUp, ARM7.IO.Read16(EXTKEYIN) & PenUp);

        Read12(Temperature0 | 2);
        Assert.Equal(0, ARM7.IO.Read16(EXTKEYIN) & PenUp);

        TouchScreen.Release();
        Assert.Equal(PenUp, ARM7.IO.Read16(EXTKEYIN) & PenUp);
    }

    [Fact]
    public void TheDSLitesChipKeepsThePenIRQInModesZeroToTwo()
    {
        Hardware.Reset(Model.DSLite);
        TouchScreen.Touch(10, 10);

        Read12(Temperature0 | 1);
        Assert.Equal(0, ARM7.IO.Read16(EXTKEYIN) & PenUp);

        Read12(Temperature0 | 3);
        Assert.Equal(PenUp, ARM7.IO.Read16(EXTKEYIN) & PenUp);
    }

    [Fact]
    public void TheOriginalDSsBatteryChannelIsGrounded()
    {
        Assert.Equal(0x000, Read12(Battery));
    }

    [Fact]
    public void TheDSLitesSecondAUXInputIsTiedToVREF()
    {
        Hardware.Reset(Model.DSLite);

        Assert.Equal(0xFFF, Read12(AUX & ~SingleEnded));
    }

    [Fact]
    public void OnlyPositionChannelsWorkInDifferentialMode()
    {
        Assert.Throws<NotSupportedException>(() => Transfer(Temperature0 & ~SingleEnded));
    }

    [Fact]
    public void TheDSiOnlyGivesPositionsAndFFFForEverythingElse()
    {
        Hardware.Reset(Model.DSi);

        Assert.Equal(0xFFF, Read12(Battery));
        Assert.Equal(0xFFF, Read12(Temperature0 & ~SingleEnded));
        Assert.Equal(0xFF, Read8(Battery));
    }

    [Fact]
    public void TouchingOffTheScreenIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TouchScreen.Touch(256, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TouchScreen.Touch(0, 192));
    }
}
