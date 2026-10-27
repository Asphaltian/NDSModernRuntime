namespace NTRModern.Tests;

public class IPCTests
{
    private const uint IPCSYNC = 0x04000180;
    private const uint IPCFIFOCNT = 0x04000184;
    private const uint IPCFIFOSEND = 0x04000188;
    private const uint IPCFIFORECV = 0x04100000;
    private const uint IF = 0x04000214;

    private const uint SendEmpty = 1 << 0;
    private const uint SendFull = 1 << 1;
    private const uint SendEmptyIRQ = 1 << 2;
    private const uint SendClear = 1 << 3;
    private const uint ReceiveEmpty = 1 << 8;
    private const uint ReceiveNotEmptyIRQ = 1 << 10;
    private const uint Error = 1 << 14;
    private const uint Enable = 1 << 15;

    public IPCTests() => Hardware.Reset(Model.DS);

    [Fact]
    public void SyncOutputIsTheOtherCPUsInput()
    {
        ARM9.IO.Write16(IPCSYNC, 0x0500);

        Assert.Equal(0x0005, ARM7.IO.Read16(IPCSYNC) & 0x0F);
        Assert.Equal(0x0500, ARM9.IO.Read16(IPCSYNC));
    }

    [Fact]
    public void SyncIRQOnlyReachesACPUThatEnabledIt()
    {
        ARM9.IO.Write16(IPCSYNC, 1 << 13);
        Assert.Equal(0u, ARM7.IO.Read32(IF));

        ARM7.IO.Write16(IPCSYNC, 1 << 14);
        ARM9.IO.Write16(IPCSYNC, 1 << 13);
        Assert.Equal((uint)Interrupt.IPCSync, ARM7.IO.Read32(IF));
    }

    [Fact]
    public void WordsArriveInTheOrderTheyWereSent()
    {
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM7.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM9.IO.Write32(IPCFIFOSEND, 1);
        ARM9.IO.Write32(IPCFIFOSEND, 2);

        Assert.Equal(0u, ARM9.IO.Read16(IPCFIFOCNT) & SendEmpty);
        Assert.Equal(1u, ARM7.IO.Read32(IPCFIFORECV));
        Assert.Equal(2u, ARM7.IO.Read32(IPCFIFORECV));
        Assert.Equal(ReceiveEmpty, ARM7.IO.Read16(IPCFIFOCNT) & ReceiveEmpty);
    }

    [Fact]
    public void SendingWhileDisabledIsIgnoredWithoutAnError()
    {
        ARM9.IO.Write32(IPCFIFOSEND, 1);

        Assert.Equal(SendEmpty, ARM9.IO.Read16(IPCFIFOCNT) & (SendEmpty | Error));
    }

    [Fact]
    public void ReceivingWhileDisabledLeavesTheWordInTheFIFO()
    {
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM9.IO.Write32(IPCFIFOSEND, 7);

        Assert.Equal(7u, ARM7.IO.Read32(IPCFIFORECV));
        Assert.Equal(7u, ARM7.IO.Read32(IPCFIFORECV));
        Assert.Equal(0u, ARM7.IO.Read16(IPCFIFOCNT) & ReceiveEmpty);
    }

    [Fact]
    public void SendingToAFullFIFOSetsTheErrorUntilItIsAcknowledged()
    {
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        for (uint word = 0; word <= IPC.FIFOSize; word++)
        {
            ARM9.IO.Write32(IPCFIFOSEND, word);
        }

        Assert.Equal(SendFull | Error, ARM9.IO.Read16(IPCFIFOCNT) & (SendFull | Error));

        ARM9.IO.Write16(IPCFIFOCNT, (ushort)(Enable | Error));
        Assert.Equal(0u, ARM9.IO.Read16(IPCFIFOCNT) & Error);
    }

    [Fact]
    public void ReceivingFromAnEmptyFIFOGivesTheLastWordAndSetsTheError()
    {
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM7.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM9.IO.Write32(IPCFIFOSEND, 9);
        ARM7.IO.Read32(IPCFIFORECV);

        Assert.Equal(9u, ARM7.IO.Read32(IPCFIFORECV));
        Assert.Equal(Error, ARM7.IO.Read16(IPCFIFOCNT) & Error);
    }

    [Fact]
    public void ClearingTheSendFIFOMakesTheOtherSideReceiveZero()
    {
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM7.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM9.IO.Write32(IPCFIFOSEND, 9);
        ARM9.IO.Write32(IPCFIFOSEND, 10);
        ARM7.IO.Read32(IPCFIFORECV);
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)(Enable | SendClear));

        Assert.Equal(0u, ARM7.IO.Read32(IPCFIFORECV));
        Assert.Equal(SendEmpty, ARM9.IO.Read16(IPCFIFOCNT) & SendEmpty);
    }

    [Fact]
    public void ReceiveNotEmptyIRQOnlyFiresWhenTheFIFOStopsBeingEmpty()
    {
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM7.IO.Write16(IPCFIFOCNT, (ushort)(Enable | ReceiveNotEmptyIRQ));
        ARM9.IO.Write32(IPCFIFOSEND, 1);
        Assert.Equal((uint)Interrupt.IPCReceiveFIFONotEmpty, ARM7.IO.Read32(IF));

        ARM7.IO.Write32(IF, (uint)Interrupt.IPCReceiveFIFONotEmpty);
        ARM9.IO.Write32(IPCFIFOSEND, 2);
        Assert.Equal(0u, ARM7.IO.Read32(IF));
    }

    [Fact]
    public void SendEmptyIRQFiresWhenEnabledOnAnEmptyFIFOAndWhenItEmpties()
    {
        ARM7.IO.Write16(IPCFIFOCNT, (ushort)Enable);
        ARM9.IO.Write16(IPCFIFOCNT, (ushort)(Enable | SendEmptyIRQ));
        Assert.Equal((uint)Interrupt.IPCSendFIFOEmpty, ARM9.IO.Read32(IF));

        ARM9.IO.Write32(IF, (uint)Interrupt.IPCSendFIFOEmpty);
        ARM9.IO.Write32(IPCFIFOSEND, 1);
        Assert.Equal(0u, ARM9.IO.Read32(IF));

        ARM7.IO.Read32(IPCFIFORECV);
        Assert.Equal((uint)Interrupt.IPCSendFIFOEmpty, ARM9.IO.Read32(IF));
    }
}
