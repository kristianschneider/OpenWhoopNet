using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenWhoop.Core.Entities; 

namespace OpenWhoop.Core.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Packet> Packets { get; set; }
        public DbSet<Activity> Activities { get; set; }
        public DbSet<HeartRateSample> HeartRateSamples { get; set; }
        public DbSet<SleepCycle> SleepCycles { get; set; }
        public DbSet<SleepEvent> SleepEvents { get; set; }
        public DbSet<StressSample> StressSamples { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<StoredDeviceSetting> StoredDeviceSettings { get; set; }

        private string? _databasePath; 

        public AppDbContext(string databasePath)
        {
            _databasePath = databasePath;
        }

     
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured && !string.IsNullOrEmpty(_databasePath))
            {
                optionsBuilder.UseSqlite($"Data Source={_databasePath}");
            }
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var rrIntervalsConverter = new ValueConverter<List<ushort>, string>(
                v => JsonSerializer.Serialize(v ?? new List<ushort>(), (JsonSerializerOptions)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<ushort>()
                    : JsonSerializer.Deserialize<List<ushort>>(v, (JsonSerializerOptions)null) ?? new List<ushort>()
            );

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Packet>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Uuid).IsUnique();
                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("datetime('now', 'utc')"); 
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.UserId).IsUnique(); 
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            // Activity Configuration
            modelBuilder.Entity<Activity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.ActivityId).IsUnique(); 
                entity.HasIndex(e => e.UserId); 
                entity.HasIndex(e => e.Start);
                entity.HasIndex(e => e.End);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            modelBuilder.Entity<SleepCycle>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.SleepId).IsUnique(); 
                entity.HasIndex(e => e.UserId); 
                entity.HasIndex(e => e.Start);
                entity.HasIndex(e => e.End);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            modelBuilder.Entity<HeartRateSample>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.TimestampUtc);
                entity.HasIndex(e => e.ActivityId); 
                entity.HasIndex(e => e.SleepCycleId); 


                entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("datetime('now', 'utc')");
            });

            modelBuilder.Entity<SleepEvent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.SleepId); 
                entity.HasIndex(e => e.Timestamp);
                entity.Property(e => e.EventType).HasMaxLength(50); 
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            modelBuilder.Entity<StressSample>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Timestamp);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            // Note on DateTimeOffset:
            // EF Core with SQLite stores DateTimeOffset as TEXT in ISO 8601 format by default,
            // which preserves the offset. This is generally good.
            // For `CreatedAt` and `UpdatedAt` with `datetime('now', 'utc')`, ensure this stores
            // as UTC. SQLite's `datetime('now')` is UTC by default.
            modelBuilder.Entity<StoredDeviceSetting>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.DeviceId).HasMaxLength(100); // Optional: set a reasonable max length
                entity.Property(e => e.DeviceName).HasMaxLength(200); // Optional: set a reasonable max length
                entity.HasIndex(e => e.DeviceId); // Optional: if you query by DeviceId
                entity.Property(e => e.LastConnectedUtc)
                    .HasDefaultValueSql("datetime('now', 'utc')"); // Optional: default to now
            });

            modelBuilder.Entity<HeartRateSample>()
                .Property(e => e.RrIntervals)
                .HasConversion(rrIntervalsConverter);

        }

    }
}