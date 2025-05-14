using OpenWhoop.App;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenWhoop.App.Protocol;

namespace OpenWhoop.App
{
    public class WhoopPacketBuilder
    {
        private static byte _sequenceNumber = 0;
        private const byte SOF = 0xAA;

        private static byte GetNextSequenceNumber()
        {
            return _sequenceNumber++;
        }

        public static byte[] Build(PacketType packetType, byte sequenceNumber, byte commandNumber, byte[] payload)
        {
            // 1) Build the raw packet
            var pkt = CreatePacket(packetType, sequenceNumber, commandNumber, payload);

            // 2) Length = payload‐packet length + 4 (same as Rust: pkt.len() as u16 + 4)
            ushort length = (ushort)(pkt.Length + 4);
            byte[] lengthBytes = BitConverter.GetBytes(length);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            // 3) CRC8 over the two length bytes
            byte crc8 = ComputeCrc8(lengthBytes);

            // 4) CRC32 over the packet
            uint crc32 = ComputeCrc32(pkt);
            byte[] crc32Bytes = BitConverter.GetBytes(crc32);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(crc32Bytes);

            // 5) Assemble final frame
            var frame = new List<byte>(1 + 2 + 1 + pkt.Length + 4);
            frame.Add(SOF);
            frame.AddRange(lengthBytes);
            frame.Add(crc8);
            frame.AddRange(pkt);
            frame.AddRange(crc32Bytes);

            return frame.ToArray();
        }

        private static byte[] CreatePacket(PacketType packetType, byte seq, byte cmd, byte[] payload)
        {
            var lst = new List<byte> { (byte)packetType, seq, cmd };
            if (payload != null && payload.Length > 0)
                lst.AddRange(payload);
            return lst.ToArray();
        }

        // Simple CRC-8 (poly=0x07). Adjust poly/initial if your Rust version differs.
        private static byte ComputeCrc8(byte[] data)
        {
            byte crc = 0x00;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
            }
            return crc;
        }

        // Standard CRC-32 (IEEE 802.3)
        private static readonly uint[] Crc32Table = GenerateCrc32Table();

        private static uint ComputeCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = (crc >> 8) ^ Crc32Table[(crc ^ b) & 0xFF];
            return ~crc;
        }

        private static uint[] GenerateCrc32Table()
        {
            const uint poly = 0xEDB88320;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? (c >> 1) ^ poly : c >> 1;
                table[i] = c;
            }
            return table;
        }



        public static byte[] CreateCommandPacket(CommandNumber command, byte[] payload = null)
        {
            return Build(PacketType.Command, 0, (byte)command, payload);
        }

        public static byte[] EnterHighFreqSync()
        {
            return Build(
                PacketType.Command, // Assuming your C# PacketType enum
                0,
                (byte)CommandNumber.EnterHighFreqSync, // Assuming your C# CommandNumber enum
                new byte[0] // No payload
            );
        }

        public static byte[] ExitHighFreqSync()
        {
            return Build(
                PacketType.Command, // Assuming your C# PacketType enum
                0,
                (byte)CommandNumber.ExitHighFreqSync, // Assuming your C# CommandNumber enum
                new byte[0] // No payload
            );
        }

        public static byte[] GetBatteryLevel()
        {
            return Build(
                PacketType.Command, // Assuming your C# PacketType enum
                0,
                (byte)CommandNumber.GetBatteryLevel, // Assuming your C# CommandNumber enum
                [0x00]
            );
        }

        public static byte[] GetHelloHarvard()
        {
            return Build(
                PacketType.Command, // Assuming your C# PacketType enum
                0,
                (byte)CommandNumber.GetHelloHarvard, // Assuming your C# CommandNumber enum
                [0x00]
            );
        }

        public static byte[] ToggleRealtimeHr(bool enable)
        {
            byte[] payload = new byte[] { enable ? (byte)0x01 : (byte)0x00 };
            return CreateCommandPacket(CommandNumber.ToggleRealtimeHr, payload);
        }

        public byte[] GetVersionInfo()
        {
            return CreateCommandPacket(CommandNumber.ReportVersionInfo);
        }

        public byte[] SetClock(DateTimeOffset dateTime)
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

        public static byte[] SetReadPointer(uint pointerTimestamp)
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

        public static byte[] SendHistoricalData(bool start)
        {
            // Assuming a simple 1-byte payload: 0x01 to start, 0x00 to stop (though Abort might be better for stop)
            // This is a guess; the actual payload might be more complex (e.g., specifying range, type of data)
            byte[] payload = new byte[] { start ? (byte)0x01 : (byte)0x00 };
            return CreateCommandPacket(CommandNumber.SendHistoricalData, payload);
        }

        public byte[] AbortHistoricalTransmits()
        {
            return CreateCommandPacket(CommandNumber.AbortHistoricalTransmits);
        }
    }
}