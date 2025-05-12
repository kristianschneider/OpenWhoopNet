using OpenWhoop.App;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenWhoop.App
{
    public static class WhoopPacketBuilder
    {
        private static byte _sequenceNumber = 0;

        private static byte GetNextSequenceNumber()
        {
            return _sequenceNumber++;
        }

        private static byte CalculateCrc(byte[] data)
        {
            byte crc = 0;
            foreach (byte b in data)
            {
                crc ^= b;
            }
            return crc;
        }

        public static byte[] CreateCommandPacket(CommandNumber command, byte[] payload = null)
        {
            payload = payload ?? new byte[0];
            List<byte> packet = new List<byte>();

            packet.Add((byte)PacketType.Command);       // Packet Type
            packet.Add(GetNextSequenceNumber());        // Sequence Number
            packet.Add((byte)(payload.Length + 1));     // Length (CommandNumber byte + payload length)
            packet.Add((byte)command);                  // Command Number
            packet.AddRange(payload);                   // Payload

            byte crc = CalculateCrc(packet.ToArray());
            packet.Add(crc);                            // CRC

            return packet.ToArray();
        }

        public static byte[] GetBatteryLevel()
        {
            return CreateCommandPacket(CommandNumber.GetBatteryLevel);
        }

        public static byte[] ToggleRealtimeHr(bool enable)
        {
            byte[] payload = new byte[] { enable ? (byte)0x01 : (byte)0x00 };
            return CreateCommandPacket(CommandNumber.ToggleRealtimeHr, payload);
        }

        public static byte[] GetVersionInfo()
        {
            return CreateCommandPacket(CommandNumber.ReportVersionInfo);
        }

        public static byte[] SetClock(DateTimeOffset dateTime)
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
    }
}