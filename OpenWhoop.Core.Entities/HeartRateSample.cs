// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.Core.Entities\HeartRateSample.cs
using System;

namespace OpenWhoop.Core.Entities
{
    public class HeartRateSample
    {
        public int Id { get; set; }
        public int? ActivityId { get; set; } // Foreign key to Activity
        public int? SleepCycleId { get; set; } // Foreign key to SleepCycle
        public DateTime TimestampUtc { get; set; } // Store as UTC
        public int Value { get; set; } // Heart rate value
        public DateTime CreatedAtUtc { get; set; } // Store as UTC
        public List<ushort> RrIntervals { get; set; }
    }
}