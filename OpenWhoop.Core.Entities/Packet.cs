using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenWhoop.Core.Entities
{
    public class Packet
    {
        public int Id { get; set; } // Corresponds to primary_key
        public Guid Uuid { get; set; }
        public byte[] Bytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; } // Store as UTC
    }
}
