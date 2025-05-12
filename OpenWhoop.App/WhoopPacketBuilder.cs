using OpenWhoop.App;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenWhoop.App
{
    public static class WhoopPacketBuilder
    {
        private static byte _sequenceNumber = 0; // Simple incrementing sequence number

        private static byte GetNextSequenceNumber()
        {
            // Sequence number wraps around 0-255
            _sequenceNumber = (byte)((_sequenceNumber + 1) % 256);
            return _sequenceNumber;
        }

        private static byte CalculateCrc(byte[] dataWithoutCrc)
        {
            byte crc = 0;
            foreach (byte b in dataWithoutCrc)
            {
                crc ^= b;
            }
            return crc;
        }

        /// <summary>
        /// Creates a command packet with no additional payload.
        /// </summary>
        /// <param name="commandNumber">The command to send.</param>
        /// <returns>The byte array representing the packet.</returns>
        public static byte[] CreateCommandPacket(CommandNumber commandNumber)
        {
            return CreateCommandPacket(commandNumber, Array.Empty<byte>());
        }

        /// <summary>
        /// Creates a command packet with a payload.
        /// </summary>
        /// <param name="commandNumber">The command to send.</param>
        /// <param name="payload">The payload for the command.</param>
        /// <returns>The byte array representing the packet.</returns>
        public static byte[] CreateCommandPacket(CommandNumber commandNumber, byte[] payload)
        {
            if (payload == null) payload = Array.Empty<byte>();

            // Packet structure: [PacketType (1), Sequence (1), Length (1), CommandNumber (1), Payload (N), CRC (1)]
            // The 'Length' in the header is the length of (CommandNumber + Payload).
            byte actualPayloadLength = (byte)(1 + payload.Length); // CommandNumber byte + payload bytes

            List<byte> packet = new List<byte>();
            packet.Add((byte)PacketType.Command);       // Packet Type
            packet.Add(GetNextSequenceNumber());        // Sequence Number
            packet.Add(actualPayloadLength);            // Length of (CommandNumber + Payload)
            packet.Add((byte)commandNumber);            // Command Number itself

            if (payload.Length > 0)
            {
                packet.AddRange(payload);               // Actual payload for the command
            }

            byte crc = CalculateCrc(packet.ToArray());
            packet.Add(crc);                            // CRC

            return packet.ToArray();
        }

        // --- Specific Command Builders ---

        public static byte[] ExitHighFreqSync()
        {
            // Corresponds to Rust: WhoopPacket::exit_high_freq_sync()
            // This sends a Command packet with CommandNumber::ExitHighFreqSync and no additional payload.
            return CreateCommandPacket(CommandNumber.ExitHighFreqSync);
        }

        public static byte[] EnterHighFreqSync()
        {
            return CreateCommandPacket(CommandNumber.EnterHighFreqSync);
        }

        public static byte[] GetBatteryLevel()
        {
            return CreateCommandPacket(CommandNumber.GetBatteryLevel);
        }

        public static byte[] SetClock(DateTimeOffset dateTime)
        {
            // Payload for SetClock is typically a 4-byte Unix timestamp (seconds since epoch)
            uint timestamp = (uint)dateTime.ToUnixTimeSeconds();
            byte[] payload = BitConverter.GetBytes(timestamp);
            if (BitConverter.IsLittleEndian) // Whoop likely expects little-endian for multi-byte values
            {
                // Array.Reverse(payload); // Reverse if device expects big-endian, but usually BLE is little-endian
            }
            return CreateCommandPacket(CommandNumber.SetClock, payload);
        }

        public static byte[] SetAlarmTime(DateTimeOffset alarmTime)
        {
            // Payload for SetAlarmTime is a 4-byte Unix timestamp
            uint timestamp = (uint)alarmTime.ToUnixTimeSeconds();
            byte[] payload = BitConverter.GetBytes(timestamp);
            // Assuming little-endian is correct for Whoop
            return CreateCommandPacket(CommandNumber.SetAlarmTime, payload);
        }

        public static byte[] DisableAlarm()
        {
            return CreateCommandPacket(CommandNumber.DisableAlarm);
        }

        public static byte[] GetVersionInfo()
        {
            return CreateCommandPacket(CommandNumber.ReportVersionInfo);
        }

        public static byte[] ToggleRealtimeHr(bool enable)
        {
            // Payload: 1 byte (0x01 for enable, 0x00 for disable)
            byte[] payload = new byte[] { enable ? (byte)0x01 : (byte)0x00 };
            return CreateCommandPacket(CommandNumber.ToggleRealtimeHr, payload);
        }

        public static byte[] GetExtendedBatteryInfo()
        {
            return CreateCommandPacket(CommandNumber.GetExtendedBatteryInfo);
        }
    }
}