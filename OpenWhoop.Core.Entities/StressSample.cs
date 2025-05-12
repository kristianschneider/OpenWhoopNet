// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.Core.Entities\StressSample.cs
using System;

namespace OpenWhoop.Core.Entities
{
    public class StressSample
    {
        public int Id { get; set; }
        public DateTimeOffset Timestamp { get; set; } // Store as UTC
        public double Score { get; set; }
        public DateTimeOffset CreatedAt { get; set; } // Store as UTC
    }
}