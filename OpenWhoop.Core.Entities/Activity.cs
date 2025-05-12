using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenWhoop.Core.Entities
{
    public class Activity
    {
        public int Id { get; set; } // Corresponds to primary_key
        public long ActivityId { get; set; } // Whoop's ID for the activity
        public int UserId { get; set; } // Foreign key to a User entity (if you create one)
        public int SportId { get; set; }
        public DateTimeOffset Start { get; set; } // Store as UTC
        public DateTimeOffset End { get; set; }   // Store as UTC
        public string TimezoneOffset { get; set; } // e.g., "-05:00" or "UTC"
        public double? Score { get; set; }
        public double? Strain { get; set; }
        public int? AverageHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public double? Kilojoules { get; set; }
        public double? Distance { get; set; } // Assuming distance might be in meters or km
        public int? Zone0Duration { get; set; } // Duration in seconds
        public int? Zone1Duration { get; set; }
        public int? Zone2Duration { get; set; }
        public int? Zone3Duration { get; set; }
        public int? Zone4Duration { get; set; }
        public int? Zone5Duration { get; set; }
        public string Source { get; set; } // e.g., "whoop", "manual"
        public DateTimeOffset CreatedAt { get; set; } // Store as UTC
        public DateTimeOffset UpdatedAt { get; set; } // Store as UTC
    }
}
