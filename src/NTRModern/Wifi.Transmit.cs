using System.Buffers.Binary;

namespace NTRModern;

internal static partial class Wifi
{
    private const int W_TXSTATCNT = 0x008;
    private const int W_TXBUSY = 0x0B6;
    private const int W_TXSTAT = 0x0B8;
    private const int W_TX_HDR_CNT = 0x194;
    private const int W_TX_SEQNO = 0x210;
    private const int W_RXTX_ADDR = 0x268;

    private const int TransmitCommand = 1;
    private const int BeaconRequest = 4;
    private const ushort BeaconTransmitStatus = 1 << 15;
    private const ushort BeaconSent = 0x0301;
    private const ushort LocationAddress = 0x0FFF;
    private const ushort MarkTransmitStatus = 1 << 12;
    private const ushort SequenceFromRAM = 1 << 13;
    private const ushort ManualSequence = 1 << 2;
    private const ushort ShortPreamble = 1 << 2;
    private const ushort SequenceNumberMask = 0x0FFF;
    private const int StatusOffset = 0x00;
    private const int Unknown04Offset = 0x04;
    private const int Unknown05Offset = 0x05;
    private const int RateOffset = 0x08;
    private const int LengthOffset = 0x0A;
    private const int HeaderSize = 0x0C;
    private const int DestinationOffset = HeaderSize + 4;
    private const int SequenceControlOffset = HeaderSize + 22;
    private const ushort LengthMask = 0x3FFF;
    private const byte TwoMegabits = 0x14;
    private const int LongPreambleMicroseconds = 192;
    private const int ShortPreambleMicroseconds = 96;
    private const ushort TransmitOkay = 0x0001;
    private const ushort MarkedTransmitStatus = 0x0700;
    private const int TransmitStatusSlotShift = 12;
    private const ushort RFTransmit = 3;
    private const ushort PreamblePins = 0x0044;
    private const ushort DataPins = 0x0046;
    private const int TransmitDoneIRQ = 1;
    private const int TransmitStartIRQ = 7;

    private static readonly (int Request, int Port, int StatusSlot)[] Locations =
        [(BeaconRequest, W_TXBUF_BEACON, 0), (3, W_TXBUF_LOC3, 2), (2, W_TXBUF_LOC2, 1), (0, W_TXBUF_LOC1, 0)];

    private static readonly SystemClock.Event TransmitStep = SystemClock.CreateEvent(AdvanceTransmit);

    private static (int Request, int Port, int StatusSlot)? _transmitting;
    private static bool _isSendingData;
    private static long _dataStart;
    private static bool _isBeaconDue;

    private static bool IsPoweredOn => (Ports[W_POWERSTATE / 2] & PowerDisabled) == 0;

    private static void ResetTransmit()
    {
        (_transmitting, _isSendingData, _dataStart, _isBeaconDue) = (null, false, 0, false);
        TransmitStep.Cancel();
    }

    private static ushort ReadTransmitAddress()
    {
        if (_transmitting is not { } location || !_isSendingData)
        {
            return Ports[W_RXTX_ADDR / 2];
        }

        var frame = Frame(location);
        long microseconds = (SystemClock.CyclesOf(Processor.ARM7) - _dataStart) * MicrosecondsPerSecond / SystemClock.CyclesPerSecond;
        long halfwords = microseconds / (2 * MicrosecondsPerByte(frame));
        return (ushort)((Ports[location.Port / 2] + (HeaderSize / 2) + halfwords) & LocationAddress);
    }

    private static int MicrosecondsPerByte(Span<byte> frame) => frame[RateOffset] == TwoMegabits ? 4 : 8;

    private static void UpdateTransmit(long now)
    {
        ushort requests = Ports[W_TXREQ_READ / 2];
        if ((requests & (1 << TransmitCommand)) != 0 && (Ports[W_TXBUF_CMD / 2] & LocationEnable) != 0)
        {
            throw new NotSupportedException("The ARM7 asked the wifi hardware to send a multiplay command, which isn't supported.");
        }

        ushort busy = 0;
        foreach (var location in Locations)
        {
            if (IsRequested(location))
            {
                busy |= (ushort)(1 << location.Request);
            }
        }

        if (_transmitting is { } current)
        {
            busy |= (ushort)(1 << current.Request);
        }

        Ports[W_TXBUSY / 2] = busy;
        if (_transmitting is null && IsPoweredOn)
        {
            foreach (var location in Locations)
            {
                if (IsRequested(location))
                {
                    StartTransmit(location, now);
                    return;
                }
            }
        }
    }

    private static bool IsRequested((int Request, int Port, int StatusSlot) location)
    {
        bool isRequested = location.Request == BeaconRequest ? _isBeaconDue : (Ports[W_TXREQ_READ / 2] & (1 << location.Request)) != 0;
        return (Ports[W_MODE_RST / 2] & TransmitEnable) != 0 && isRequested && (Ports[location.Port / 2] & LocationEnable) != 0;
    }

    private static Span<byte> Frame((int Request, int Port, int StatusSlot) location)
    {
        return RAM.AsSpan((Ports[location.Port / 2] & LocationAddress) * 2);
    }

    private static void StartTransmit((int Request, int Port, int StatusSlot) location, long now)
    {
        var frame = Frame(location);
        if (frame[Unknown04Offset] > 2)
        {
            throw new NotSupportedException($"The ARM7 sent a wifi frame with 0x{frame[Unknown04Offset]:X2} in its header, which isn't supported.");
        }

        if ((frame[DestinationOffset] & 1) == 0)
        {
            throw new NotSupportedException("The ARM7 sent a wifi frame to a single console or access point, which isn't supported.");
        }

        _transmitting = location;
        _isSendingData = false;
        (Ports[W_RF_STATUS / 2], Ports[W_RF_PINS / 2]) = (RFTransmit, PreamblePins);
        bool isShort = frame[RateOffset] == TwoMegabits && (Ports[W_PREAMBLE / 2] & ShortPreamble) != 0;
        TransmitStep.ScheduleAt(now + CyclesFor(isShort ? ShortPreambleMicroseconds : LongPreambleMicroseconds));
    }

    private static int Length(Span<byte> frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame[LengthOffset..]) & LengthMask;

    private static void AdvanceTransmit(long when)
    {
        var location = _transmitting!.Value;
        var frame = Frame(location);
        bool isBeacon = location.Request == BeaconRequest;
        if (!_isSendingData)
        {
            (_isSendingData, _dataStart) = (true, when);
            Ports[W_RF_PINS / 2] = DataPins;
            bool usesSequenceNumber = isBeacon || (Ports[location.Port / 2] & SequenceFromRAM) == 0;
            if (usesSequenceNumber && frame[Unknown04Offset] == 0 && (Ports[W_TX_HDR_CNT / 2] & ManualSequence) == 0)
            {
                ushort sequence = Ports[W_TX_SEQNO / 2];
                BinaryPrimitives.WriteUInt16LittleEndian(frame[SequenceControlOffset..], (ushort)(sequence << 4));
                Ports[W_TX_SEQNO / 2] = (ushort)((sequence + 1) & SequenceNumberMask);
            }

            RequestInterrupt(TransmitStartIRQ);
            TransmitStep.ScheduleAt(_dataStart + CyclesFor(Length(frame) * MicrosecondsPerByte(frame)));
            return;
        }

        Ports[W_RXTX_ADDR / 2] = (ushort)((Ports[location.Port / 2] + ((HeaderSize + Length(frame)) / 2)) & LocationAddress);
        BinaryPrimitives.WriteUInt16LittleEndian(frame[StatusOffset..], TransmitOkay);
        frame[Unknown05Offset] = 0;
        (Ports[W_RF_STATUS / 2], Ports[W_RF_PINS / 2]) = IsPoweredOn ? (RFReceive, ReceivePins) : (RFIdle, IdlePins);
        _transmitting = null;
        if (isBeacon)
        {
            _isBeaconDue = false;
            if ((Ports[W_TXSTATCNT / 2] & BeaconTransmitStatus) != 0)
            {
                Ports[W_TXSTAT / 2] = BeaconSent;
                RequestInterrupt(TransmitDoneIRQ);
            }
        }
        else
        {
            bool isMarked = (Ports[location.Port / 2] & MarkTransmitStatus) != 0;
            Ports[W_TXSTAT / 2] = (ushort)(TransmitOkay | (isMarked ? MarkedTransmitStatus : 0) | (location.StatusSlot << TransmitStatusSlotShift));
            Ports[location.Port / 2] &= unchecked((ushort)~LocationEnable);
            RequestInterrupt(TransmitDoneIRQ);
        }

        UpdateTransmit(when);
    }
}
