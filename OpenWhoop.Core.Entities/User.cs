// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.Core.Entities\User.cs
using System;

namespace OpenWhoop.Core.Entities
{
    public class User
    {
        public int Id { get; set; } // Primary Key
        public long UserId { get; set; } // Whoop's User ID, should be unique
        public DateTimeOffset CreatedAt { get; set; } // Store as UTC
        public DateTimeOffset UpdatedAt { get; set; } // Store as UTC
    }
}