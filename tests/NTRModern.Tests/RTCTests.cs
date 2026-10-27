namespace NTRModern.Tests;

public sealed class RTCTests : IDisposable
{
    private const uint RCNT = 0x04000134;
    private const uint RTCRegister = 0x04000138;
    private const byte Status1 = 0x06;
    private const byte Status2 = 0x46;
    private const byte DateTime = 0x26;
    private const byte Time = 0x66;
    private const byte Read = 0x80;

    public RTCTests()
    {
        Hardware.Reset(Model.DS);
        RTC.Host = new FixedTime(new DateTimeOffset(2026, 9, 29, 14, 5, 9, TimeSpan.Zero));
    }

    public void Dispose() => RTC.Host = TimeProvider.System;

    private static byte[] Transfer(byte command, params byte[] parameters)
    {
        ARM7.IO.Write16(RTCRegister, 0x72);
        ARM7.IO.Write16(RTCRegister, 0x76);
        Send(command);
        foreach (byte parameter in parameters)
        {
            Send(parameter);
        }

        byte[] received = (command & Read) == 0 ? [] : new byte[command switch
        {
            DateTime | Read => 7,
            Time | Read => 3,
            _ => 1,
        }];

        for (int index = 0; index < received.Length; index++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                ARM7.IO.Write16(RTCRegister, 0x64);
                received[index] |= (byte)((ARM7.IO.Read16(RTCRegister) & 1) << bit);
                ARM7.IO.Write16(RTCRegister, 0x66);
            }
        }

        ARM7.IO.Write16(RTCRegister, (ushort)(ARM7.IO.Read16(RTCRegister) & ~4));
        return received;
    }

    private static void Send(byte value)
    {
        for (int bit = 0; bit < 8; bit++)
        {
            ARM7.IO.Write16(RTCRegister, (ushort)(0x74 | ((value >> bit) & 1)));
            ARM7.IO.Write16(RTCRegister, (ushort)(0x76 | ((value >> bit) & 1)));
        }
    }

    [Fact]
    public void TheDateAndTimeComeFromTheHostClockInBCD()
    {
        Assert.Equal([0x26, 0x09, 0x29, 2, 0x14 | 0x40, 0x05, 0x09], Transfer(DateTime | Read));
    }

    [Fact]
    public void SettingTheTimeMovesTheClock()
    {
        Transfer(Time, 0x08, 0x30, 0x00);

        Assert.Equal([0x08, 0x30, 0x00], Transfer(Time | Read));
        Assert.Equal([0x26, 0x09, 0x29, 2, 0x08, 0x30, 0x00], Transfer(DateTime | Read));
    }

    [Fact]
    public void TheClockStartsIn24HourModeWithNothingToReport()
    {
        Assert.Equal([0x02], Transfer(Status1 | Read));
    }

    [Fact]
    public void RTCInterruptsAreNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => Transfer(Status2, 0x04));
    }

    [Fact]
    public void RCNTInputsReadHighAndOutputsReadWhatWasWritten()
    {
        ARM7.IO.Write16(RCNT, 0x8000);
        Assert.Equal(0x800F, ARM7.IO.Read16(RCNT));

        ARM7.IO.Write16(RCNT, 0x8010);
        Assert.Equal(0x801E, ARM7.IO.Read16(RCNT));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
