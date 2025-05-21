using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenWhoop.App
{
    public struct HistoryMetadata(uint unix, uint data, MetadataType cmd)
    {
        /// <summary>Unix timestamp (seconds since epoch)</summary>
        public uint Unix { get; set; } = unix;

        /// <summary>Byte‐offset or count field from the packet</summary>
        public uint Data { get; set; } = data;

        /// <summary>The metadata command type</summary>
        public MetadataType Cmd { get; set; } = cmd;
    }
}
