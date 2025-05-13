using OpenWhoop.App;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenWhoop.App.Protocol;

namespace OpenWhoop.App
{
    public  class WhoopPacketBuilder
    {
        private static byte _sequenceNumber = 0;
        private const byte SOF = 0xAA;

        private static byte GetNextSequenceNumber()
        {
            return _sequenceNumber++;
        }

        public static byte[] Build(PacketType packetType, byte sequenceNumber, byte commandNumber, byte[] payload)
        {
            if (payload == null)
            {
                payload = [];
            }

            // 1. Create the inner packet (pkt in Rust: Type, Seq, Cmd, Data)
            List<byte> innerPacketList = new List<byte>();
            innerPacketList.Add((byte)packetType);
            innerPacketList.Add(sequenceNumber);
            innerPacketList.Add(commandNumber);
            innerPacketList.AddRange(payload);
            byte[] innerPacketBytes = innerPacketList.ToArray();

            // 2. Calculate length: innerPacket.Length + 4 (for DataCRC32)
            ushort length = (ushort)(innerPacketBytes.Length + 1);

            // 3. Convert length to 2-byte little-endian array (length_buffer)
            byte[] lengthBuffer = BitConverter.GetBytes(length);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBuffer); // Ensure little-endian
            }

            // 4. Calculate CRC8 over length_buffer
            byte headerCrc8 = Crc.Crc8(lengthBuffer);

            // 5. Calculate CRC32 over innerPacketBytes
            uint dataCrc32 = Crc.Crc32(innerPacketBytes);

            // 6. Convert dataCrc32 to 4-byte little-endian array
            byte[] dataCrc32Buffer = BitConverter.GetBytes(dataCrc32);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(dataCrc32Buffer); // Ensure little-endian
            }

            // 7. Assemble the final framed packet
            List<byte> framedPacketList = new List<byte>();
            framedPacketList.Add(SOF);
            framedPacketList.AddRange(lengthBuffer);    // 2 bytes
            framedPacketList.Add(headerCrc8);           // 1 byte
            framedPacketList.AddRange(innerPacketBytes); // Variable length
            framedPacketList.AddRange(dataCrc32Buffer); // 4 bytes

            return framedPacketList.ToArray();
        }



        public static byte[] CreateCommandPacket(CommandNumber command, byte[] payload = null)
        {
            return Build(PacketType.Command, 0, (byte)command, payload);
        }

public  byte[] EnterHighFreqSync()
        {
            return Build(
                PacketType.Command, // Assuming your C# PacketType enum
                GetNextSequenceNumber(),
                (byte)CommandNumber.EnterHighFreqSync, // Assuming your C# CommandNumber enum
                new byte[0] // No payload
            );
        }

        public static byte[] GetBatteryLevel()
        {
            return Build(
                PacketType.Command, // Assuming your C# PacketType enum
                GetNextSequenceNumber(),
                (byte)CommandNumber.GetBatteryLevel, // Assuming your C# CommandNumber enum
                new byte[0] // No payload
            );
        }

        public static byte[] ToggleRealtimeHr(bool enable)
        {
            byte[] payload = new byte[] { enable ? (byte)0x01 : (byte)0x00 };
            return CreateCommandPacket(CommandNumber.ToggleRealtimeHr, payload);
        }

        public  byte[] GetVersionInfo()
        {
            return CreateCommandPacket(CommandNumber.ReportVersionInfo);
        }

        public  byte[] SetClock(DateTimeOffset dateTime)
        {
            uint timestamp = (uint)dateTime.ToUnixTimeSeconds();
            byte[] payload = BitConverter.GetBytes(timestamp);
            // Whoop likely expects little-endian for multi-byte values.
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(payload); // Ensure little-endian if system is big-endian
            }
            return CreateCommandPacket(CommandNumber.SetClock, payload);
        }

        public  byte[] SetReadPointer(uint pointerTimestamp)
        {
            // Assuming pointerTimestamp is a Unix timestamp (seconds since epoch)
            // Payload is typically 4 bytes, little-endian
            byte[] payload = BitConverter.GetBytes(pointerTimestamp);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(payload); // Ensure little-endian
            }
            return CreateCommandPacket(CommandNumber.SetReadPointer, payload);
        }

        public  byte[] SendHistoricalData(bool start)
        {
            // Assuming a simple 1-byte payload: 0x01 to start, 0x00 to stop (though Abort might be better for stop)
            // This is a guess; the actual payload might be more complex (e.g., specifying range, type of data)
            byte[] payload = new byte[] { start ? (byte)0x01 : (byte)0x00 };
            return CreateCommandPacket(CommandNumber.SendHistoricalData, payload);
        }

        public  byte[] AbortHistoricalTransmits()
        {
            return CreateCommandPacket(CommandNumber.AbortHistoricalTransmits);
        }
    }
}