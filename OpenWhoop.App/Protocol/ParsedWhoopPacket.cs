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
        public byte HeaderCRC { get; private set; }
        public byte CalculatedHeaderCRC { get; private set; }
        public bool IsHeaderCrcValid => HeaderCRC == CalculatedHeaderCRC;

        public byte Sequence { get; private set; }
        public PacketType PacketType { get; private set; }
        public byte CommandOrEventNumber { get; private set; }
        public byte[] Payload { get; private set; }

        
        private ParsedWhoopPacket()
        {
            RawData = Array.Empty<byte>();
            Payload = Array.Empty<byte>();
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



            if (length < 8 || length > rawData.Length - offset)
            {
                packet.Error = PacketParseError.DataLengthMismatch;
                return false;
            }

            int pktLen = length - 4;        // drop the trailing CRC32
            if (pktLen < 3)
            {               // need at least packet_type, seq, cmd
                packet.Error = PacketParseError.DataLengthMismatch;
                return false;
            }

            // verify CRC32 over those pktLen bytes
            int crc32Offset = offset + pktLen;
            uint expectedCrc32 = BitConverter.ToUInt32(rawData, crc32Offset);
            uint calculatedCrc32 = Crc.Crc32(rawData, offset, pktLen);
            if (expectedCrc32 != calculatedCrc32)
            {
                packet.Error = PacketParseError.PayloadCrcMismatch;
                return false;
            }

            // now split out the fields
            packet.PacketType = (PacketType)rawData[offset];
            packet.Sequence = rawData[offset + 1];
            packet.CommandOrEventNumber = rawData[offset + 2];

            // and the actual payload is the remainder:
            int payloadLength = pktLen - 3;   // pktLen minus the 3 header bytes
            //copy rawdata into payload from offset + 3 to payloadLength
           
                packet.Payload = new byte[payloadLength];
                Array.Copy(rawData, offset + 3, packet.Payload, 0, payloadLength);
           
            //packet.Payload = new ArraySegment<byte>(rawData,
            //    offset + 3,
            //    payloadLength);

            packet.IsValid = true;
            return true;

        }
    }
}