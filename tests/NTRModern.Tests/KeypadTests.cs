namespace NTRModern.Tests;

public sealed class KeypadTests : IDisposable
{
    private const uint KEYINPUT = 0x04000130;
    private const uint KEYCNT = 0x04000132;
    private const uint EXTKEYIN = 0x04000136;
    private const uint IF = 0x04000214;
    private const ushort IRQEnable = 1 << 14;
    private const ushort AllSelected = 1 << 15;

    public KeypadTests() => Hardware.Reset(Model.DS);

    public void Dispose() => Keypad.Pressed = Buttons.None;

    [Fact]
    public void BothCPUsSeeTheButtonsBeingHeld()
    {
        Keypad.Pressed = Buttons.A | Buttons.Start;

        Assert.Equal(0x3F6, ARM9.IO.Read16(KEYINPUT));
        Assert.Equal(0x3F6, ARM7.IO.Read16(KEYINPUT));
    }

    [Fact]
    public void TheARM7SeesXAndYWithThePenUpAndTheLidOpen()
    {
        Assert.Equal(0x7F, ARM7.IO.Read16(EXTKEYIN));

        Keypad.Pressed = Buttons.X;
        Assert.Equal(0x7E, ARM7.IO.Read16(EXTKEYIN));
    }

    [Fact]
    public void AnyOfTheSelectedButtonsRaisesTheIRQ()
    {
        Keypad.Pressed = Buttons.B;

        ARM9.IO.Write16(KEYCNT, (ushort)(IRQEnable | (ushort)(Buttons.A | Buttons.B)));

        Assert.Equal((uint)Interrupt.Keypad, ARM9.IO.Read32(IF));
        Assert.Equal(0u, ARM7.IO.Read32(IF));
    }

    [Fact]
    public void AllSelectedButtonsAreNeededInAndMode()
    {
        Keypad.Pressed = Buttons.B;
        ARM7.IO.Write16(KEYCNT, (ushort)(IRQEnable | AllSelected | (ushort)(Buttons.A | Buttons.B)));
        Assert.Equal(0u, ARM7.IO.Read32(IF));

        Keypad.Pressed = Buttons.A | Buttons.B;
        Keypad.CheckInterrupts();
        Assert.Equal((uint)Interrupt.Keypad, ARM7.IO.Read32(IF));
    }

    [Fact]
    public void XAndYCantRaiseTheIRQ()
    {
        Keypad.Pressed = Buttons.X | Buttons.Y;

        ARM9.IO.Write16(KEYCNT, 0xFFFF);

        Assert.Equal(0u, ARM9.IO.Read32(IF));
    }
}
