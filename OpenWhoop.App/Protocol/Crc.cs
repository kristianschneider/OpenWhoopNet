using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenWhoop.App.Protocol
{
    public static class Crc
    {
        public static byte Crc8(byte[] data, int offset, int count)
        {
            byte crc = 0;
            if (data == null || count <= 0 || offset < 0 || offset + count > data.Length)
            {
                // Or throw an ArgumentException, depending on desired behavior
                return crc;
            }

            for (int j = 0; j < count; j++)
            {
                crc ^= data[offset + j];
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x80) != 0)
                    {
                        crc = (byte)((crc << 1) ^ 0x07);
                    }
                    else
                    {
                        crc = (byte)(crc << 1);
                    }
                }
            }
            return crc;
        }

        public static byte Crc8(byte[] data)
        {
            return Crc8(data, 0, data.Length);
        }

        public static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFF;
            if (data == null || count <= 0 || offset < 0 || offset + count > data.Length)
            {
                return ~crc; // Or throw, or return 0 based on desired error handling
            }

            for (int j = 0; j < count; j++)
            {
                crc ^= data[offset + j];
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc = (crc >> 1) ^ 0xEDB88320;
                    }
                    else
                    {
                        crc = crc >> 1;
                    }
                }
            }
            return ~crc;
        }

        public static uint Crc32(byte[] data)
        {
            if (data == null) return 0; // Or throw
            return Crc32(data, 0, data.Length);
        }
    }
}
