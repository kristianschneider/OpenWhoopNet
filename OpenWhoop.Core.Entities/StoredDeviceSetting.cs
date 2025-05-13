using System;
using System.ComponentModel.DataAnnotations;

namespace OpenWhoop.Core.Entities
{
    public class StoredDeviceSetting
    {
        [Key]
        public int Id { get; set; } // Primary Key

        public string? DeviceId { get; set; } // Changed from StoredDeviceId and made nullable

        public string? DeviceName { get; set; } // Was StoredDeviceName, made nullable for consistency

        public DateTime LastConnectedUtc { get; set; } // Added
    }
}
