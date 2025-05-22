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
                PacketType.Command, 
                0,
                (byte)CommandNumber.GetHelloHarvard,
                [0x00]
            );
        }

        public static byte[] GetClock()
        {
            return Build(
                PacketType.Command,
                0,
            (byte)CommandNumber.GetClock,
            [0x00]);
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


        public static byte[] SendHistoricalData()
        {
            byte[] payload = new byte[] { 0x00 };
            return CreateCommandPacket(CommandNumber.SendHistoricalData, payload);
        }

        public static byte[] SendHistoricalData(uint offset)
        {
            byte[] offsetBytes = BitConverter.GetBytes(offset);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(offsetBytes);
            }
            return CreateCommandPacket(CommandNumber.SendHistoricalData, offsetBytes);
        }

     
        public static byte[] SendHistoryEnd(UInt32 data)
        {
            // 1. Start with 0x01
            var packetData = new List<byte> { 0x01 };

            // 2. Add the 4 bytes of 'data' in little-endian order
            byte[] dataBytes = BitConverter.GetBytes(data);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(dataBytes);
            }
            packetData.AddRange(dataBytes);

            // 3. Add 4 bytes of zero padding
            packetData.AddRange(new byte[4]);

            // 4. Build the packet
            return Build(
                PacketType.Command,
                0,
                (byte)CommandNumber.HistoricalDataResult,
                packetData.ToArray()
            );
        }

        public static byte[] AbortHistoricalTransmits()
        {
            return CreateCommandPacket(CommandNumber.AbortHistoricalTransmits);
        }

        public static byte[] Reset()
        {
            return Build(
                PacketType.Command, // Assuming your C# PacketType enum
                0,
                (byte)CommandNumber.RebootStrap, // Assuming your C# CommandNumber enum
                [0x00]
            );
        }

        public static byte[] SetTime()
        {
            uint currentTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Create payload: timestamp (4 bytes) + padding (5 bytes)
            var data = new List<byte>();

            // Add the Unix timestamp in little-endian order
            byte[] timeBytes = BitConverter.GetBytes(currentTime);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(timeBytes);
            }
            data.AddRange(timeBytes);

            // Add 5 bytes of padding (zeros)
            data.AddRange(new byte[5]);

            // Build the packet with Command type, sequence 0, SetClock command number
            return Build(
                PacketType.Command,
                0,
                (byte)CommandNumber.SetClock,
                data.ToArray()
            );
        }

        public static byte[] GetName()
        {
            return Build(
                PacketType.Command,
                0,
                (byte)CommandNumber.GetAdvertisingNameHarvard,
                [0x00]);
        }
    }
}