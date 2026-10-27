namespace NTRModern;

internal static class IPC
{
    public const int FIFOSize = 16;
    public const uint WritableControl = SendEmptyIRQ | ReceiveNotEmptyIRQ | Enable;

    private const uint SyncOutput = 0xF << 8;
    private const uint SyncSendIRQ = 1 << 13;
    private const uint SyncIRQEnable = 1 << 14;
    private const uint SendEmpty = 1 << 0;
    private const uint SendFull = 1 << 1;
    private const uint SendEmptyIRQ = 1 << 2;
    private const uint SendClear = 1 << 3;
    private const uint ReceiveEmpty = 1 << 8;
    private const uint ReceiveFull = 1 << 9;
    private const uint ReceiveNotEmptyIRQ = 1 << 10;
    private const uint Error = 1 << 14;
    private const uint Enable = 1 << 15;

    private static readonly End[] Ends = [new(), new()];

    public static void Reset()
    {
        Ends[0] = new End();
        Ends[1] = new End();
    }

    public static uint ReadIPCSYNC(Processor self)
    {
        SystemClock.CatchUp(self);
        var end = Ends[(int)self];
        return ((OtherEnd(self).Sync & SyncOutput) >> 8) | end.Sync;
    }

    public static void WriteIPCSYNC(Processor self, uint value)
    {
        SystemClock.CatchUp(self);
        Ends[(int)self].Sync = value & (SyncOutput | SyncIRQEnable);
        if ((value & SyncSendIRQ) != 0 && (OtherEnd(self).Sync & SyncIRQEnable) != 0)
        {
            Hardware.InterruptsOf(SystemClock.Other(self)).Raise(Interrupt.IPCSync);
        }
    }

    public static uint ReadIPCFIFOCNT(Processor self)
    {
        SystemClock.CatchUp(self);
        var end = Ends[(int)self];
        var receive = OtherEnd(self).Sent;
        return end.Control
            | (end.Sent.Count == 0 ? SendEmpty : 0)
            | (end.Sent.Count == FIFOSize ? SendFull : 0)
            | (receive.Count == 0 ? ReceiveEmpty : 0)
            | (receive.Count == FIFOSize ? ReceiveFull : 0);
    }

    public static void WriteIPCFIFOCNT(Processor self, uint value)
    {
        SystemClock.CatchUp(self);
        var end = Ends[(int)self];
        uint error = (value & Error) != 0 ? 0 : end.Control & Error;
        end.Control = (value & WritableControl) | error;
        if ((value & SendClear) != 0)
        {
            end.Sent.Clear();
            OtherEnd(self).LastReceived = 0;
        }

        UpdateIRQs();
    }

    public static void Send(Processor self, uint value)
    {
        SystemClock.CatchUp(self);
        var end = Ends[(int)self];
        if ((end.Control & Enable) == 0)
        {
            return;
        }

        if (end.Sent.Count == FIFOSize)
        {
            end.Control |= Error;
            return;
        }

        end.Sent.Enqueue(value);
        UpdateIRQs();
    }

    public static uint Receive(Processor self)
    {
        SystemClock.CatchUp(self);
        var end = Ends[(int)self];
        var receive = OtherEnd(self).Sent;
        if (receive.Count == 0)
        {
            end.Control |= Error;
            return end.LastReceived;
        }

        if ((end.Control & Enable) == 0)
        {
            return receive.Peek();
        }

        end.LastReceived = receive.Dequeue();
        UpdateIRQs();
        return end.LastReceived;
    }

    private static void UpdateIRQs()
    {
        for (var processor = Processor.ARM9; processor <= Processor.ARM7; processor++)
        {
            var end = Ends[(int)processor];
            bool sendEmpty = (end.Control & SendEmptyIRQ) != 0 && end.Sent.Count == 0;
            bool receiveNotEmpty = (end.Control & ReceiveNotEmptyIRQ) != 0 && OtherEnd(processor).Sent.Count != 0;
            if (sendEmpty && !end.SendEmptyCondition)
            {
                Hardware.InterruptsOf(processor).Raise(Interrupt.IPCSendFIFOEmpty);
            }

            if (receiveNotEmpty && !end.ReceiveNotEmptyCondition)
            {
                Hardware.InterruptsOf(processor).Raise(Interrupt.IPCReceiveFIFONotEmpty);
            }

            (end.SendEmptyCondition, end.ReceiveNotEmptyCondition) = (sendEmpty, receiveNotEmpty);
        }
    }

    private static End OtherEnd(Processor self) => Ends[(int)SystemClock.Other(self)];

    private sealed class End
    {
        public readonly Queue<uint> Sent = new(FIFOSize);

        public uint Sync;
        public uint Control;
        public uint LastReceived;
        public bool SendEmptyCondition;
        public bool ReceiveNotEmptyCondition;
    }
}
