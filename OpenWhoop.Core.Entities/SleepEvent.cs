// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.Core.Entities\SleepEvent.cs
using System;

namespace OpenWhoop.Core.Entities
{
    // Consider defining an enum for EventType if the values are fixed
    // public enum SleepEventType { Awake, Light, Sws, Rem, Unknown }

    public class SleepEvent
    {
        public int Id { get; set; }
        public long SleepId { get; set; } // Corresponds to the SleepCycle.SleepId (Whoop's ID)
        // Or, if you prefer a direct FK to SleepCycle.Id:
        // public int SleepCycleId { get; set; }
        public DateTimeOffset Timestamp { get; set; } // Store as UTC
        public string EventType { get; set; } // e.g., "AWAKE", "LIGHT", "SWS", "REM". Consider an enum.
        public DateTimeOffset CreatedAt { get; set; } // Store as UTC
    }
}