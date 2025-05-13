using System;
using System.Runtime.Intrinsics.Arm;
using System.Text; // For BitConverter in error messages if needed

namespace OpenWhoop.App.Protocol
{
    // Assuming PacketType, CommandNumber, EventNumber enums are defined in OpenWhoop.App namespace
    // e.g., public enum PacketType { Unknown = 0, RealtimeData = 46, HistoricalData = 47, Event = 48, CommandResponse = 49, ... }

    public enum PacketParseError
    {
        None,
        TooShortForHeader,
        InvalidSOF,
        DataLengthMismatch,
        HeaderCrcMismatch,
        DeclaredLengthTooSmall, // DeclaredLengthByte < 4 for non-minimal packets
        PayloadCrcMismatch
    }

    public class ParsedWhoopPacket
    {
        public const byte ExpectedSOF = 0xAA;

        public byte[] RawData { get; private set; }
        public bool IsValid { get; private set; }
        public PacketParseError Error { get; private set; }
        public string ErrorMessage { get; private set; }

        public byte SOF { get; private set; }
        public byte DeclaredLengthByte { get; private set; }
        public byte HeaderCRC { get; private set; }
        public byte CalculatedHeaderCRC { get; private set; }
        public bool IsHeaderCrcValid => HeaderCRC == CalculatedHeaderCRC;

        public byte Sequence { get; private set; }
        public PacketType PacketType { get; private set; }
        public byte CommandOrEventNumber { get; private set; }
        public ArraySegment<byte> Payload { get; private set; }
        public byte PayloadCRC { get; private set; }
        public byte CalculatedPayloadCRC { get; private set; }
        public bool IsPayloadCrcValid => PayloadCRC == CalculatedPayloadCRC;

        
        private ParsedWhoopPacket()
        {
            RawData = Array.Empty<byte>();
            Payload = new ArraySegment<byte>();
        }


        public static bool TryParse(byte[] rawData, out ParsedWhoopPacket packet)
        {
            packet = new ParsedWhoopPacket { RawData = rawData, IsValid = false, Error = PacketParseError.None };

            if (rawData == null || rawData.Length < 8)
            {
                packet.Error = PacketParseError.TooShortForHeader;
                return false;
            }

            int offset = 0;
            packet.SOF = rawData[offset++];
            if (packet.SOF != ExpectedSOF)
            {
                packet.Error = PacketParseError.InvalidSOF;
                return false;
            }

            ushort length = BitConverter.ToUInt16(rawData, offset); // 2 bytes, little-endian
            offset += 2;

            packet.HeaderCRC = rawData[offset++];
            packet.CalculatedHeaderCRC = Crc.Crc8(rawData, 1, 2); // CRC8 over the 2 length bytes
            if (packet.HeaderCRC != packet.CalculatedHeaderCRC)
            {
                packet.Error = PacketParseError.HeaderCrcMismatch;
                return false;
            }

            if (length > rawData.Length - offset || length < 8)
            {
                packet.Error = PacketParseError.DataLengthMismatch;
                return false;
            }

            int payloadLength = length - 8;
            if (payloadLength < 3) // At least packet_type, seq, cmd
            {
                packet.Error = PacketParseError.DataLengthMismatch;
                return false;
            }

            var payload = new byte[payloadLength];
            Array.Copy(rawData, offset, payload, 0, payloadLength);
            offset += payloadLength;

            //uint expectedCrc32 = BitConverter.ToUInt32(rawData, offset);
            //uint calculatedCrc32 = Crc32.Compute(payload, 0, payload.Length); // <-- Use CRC32 here!
            //if (expectedCrc32 != calculatedCrc32)
            //{
            //    packet.Error = PacketParseError.PayloadCrcMismatch;
            //    return false;
            //}

            // Now extract fields from payload
            int payloadOffset = 0;
            packet.PacketType = (PacketType)payload[payloadOffset++];
            packet.Sequence = payload[payloadOffset++];
            packet.CommandOrEventNumber = payload[payloadOffset++];
            packet.Payload = new ArraySegment<byte>(payload, payloadOffset, payload.Length - payloadOffset);

            packet.IsValid = true;
            return true;

        }
    }
}