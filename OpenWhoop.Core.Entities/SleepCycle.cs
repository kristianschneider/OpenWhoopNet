// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.Core.Entities\SleepCycle.cs
using System;

namespace OpenWhoop.Core.Entities
{
    public class SleepCycle
    {
        public int Id { get; set; } // Primary Key
        public long SleepId { get; set; } // Whoop's ID for the sleep
        public int UserId { get; set; } // Foreign key to User
        public DateTimeOffset Start { get; set; } // Store as UTC
        public DateTimeOffset End { get; set; }   // Store as UTC
        public string TimezoneOffset { get; set; } // e.g., "-05:00"
        public bool Nap { get; set; }
        public int? Score { get; set; }
        public int? RecoveryScore { get; set; } // Renamed from "quality_score" for clarity if it represents recovery
        public double? HrvRmssd { get; set; } // Heart Rate Variability (Root Mean Square of Successive Differences)
        public int? RestingHeartRate { get; set; }
        public double? Kilojoules { get; set; }
        public int? AverageHeartRate { get; set; }
        public int? SleepNeedSeconds { get; set; } // Duration in seconds
        public double? RespiratoryRate { get; set; }
        public int? SleepDebtSeconds { get; set; } // Duration in seconds
        public double? SleepEfficiency { get; set; } // Percentage (e.g., 0.0 to 1.0 or 0 to 100)
        public int? SleepConsistency { get; set; } // Percentage (e.g., 0 to 100)
        public int? CyclesCount { get; set; }
        public int? Disturbances { get; set; }
        public int? TimeInBedSeconds { get; set; } // Duration in seconds
        public int? LatencySeconds { get; set; } // Duration in seconds
        public int? LightSleepDurationSeconds { get; set; } // Duration in seconds
        public int? SlowWaveSleepDurationSeconds { get; set; } // Duration in seconds
        public int? RemSleepDurationSeconds { get; set; } // Duration in seconds
        public int? AwakeDurationSeconds { get; set; } // Duration in seconds
        public int? ArousalTimeSeconds { get; set; } // Duration in seconds - check if this is different from Disturbances or AwakeDuration
        public string Source { get; set; } // e.g., "whoop_auto", "manual"
        public DateTimeOffset CreatedAt { get; set; } // Store as UTC
        public DateTimeOffset UpdatedAt { get; set; } // Store as UTC
    }
}   