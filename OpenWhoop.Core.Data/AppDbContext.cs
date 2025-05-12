// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.Core.Data\AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using OpenWhoop.Core.Entities; // Make sure this using directive is correct

namespace OpenWhoop.Core.Data
{
    public class AppDbContext : DbContext
    {
        // DbSet properties for each entity
        public DbSet<Packet> Packets { get; set; }
        public DbSet<Activity> Activities { get; set; }
        public DbSet<HeartRateSample> HeartRateSamples { get; set; }
        public DbSet<SleepCycle> SleepCycles { get; set; }
        public DbSet<SleepEvent> SleepEvents { get; set; }
        public DbSet<StressSample> StressSamples { get; set; }
        public DbSet<User> Users { get; set; }

        private readonly string _databasePath;

        // Constructor that accepts a database path (connection string for SQLite)
        // This is useful if the path is determined at runtime (e.g., from command line args)
        public AppDbContext(string databasePath)
        {
            _databasePath = databasePath;
        }

        // Parameterless constructor for EF Core tools (like migrations)
        // It might use a default path or expect configuration elsewhere.
        // For simplicity, tools can also use the constructor that takes DbContextOptions.
        // public AppDbContext()
        // {
        //     // A default path for design-time tools, or handle this via AddDbContext in a startup project.
        //     // For library projects, it's often better to rely on the consuming application (OpenWhoop.App)
        //     // to provide the connection string.
        //     _databasePath = "openwhoop_default.db";
        // }

        // Constructor for use with ASP.NET Core DI or when options are configured externally
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
            // If using this constructor, _databasePath might not be set unless you handle it.
            // Typically, the connection string is part of the options.
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                if (!string.IsNullOrEmpty(_databasePath))
                {
                    optionsBuilder.UseSqlite($"Data Source={_databasePath}");
                }
                else
                {
                    // Fallback or throw if path isn't provided and options aren't configured.
                    // This is important for migrations if the parameterless constructor isn't sufficient
                    // or if you're not using a design-time DbContext factory.
                    // For now, let's assume _databasePath will be set by the application
                    // or options will be passed in.
                    // If running migrations from this project directly, you might need a
                    // design-time factory (IDesignTimeDbContextFactory).
                    optionsBuilder.UseSqlite("Data Source=openwhoop_design_time.db"); // Default for tools
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Packet Configuration
            modelBuilder.Entity<Packet>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Uuid).IsUnique();
                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("datetime('now', 'utc')"); // SQLite specific for UTC
            });

            // User Configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.UserId).IsUnique(); // Whoop's User ID
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            // Activity Configuration
            modelBuilder.Entity<Activity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.ActivityId).IsUnique(); // Whoop's Activity ID
                entity.HasIndex(e => e.UserId); // Foreign key, assuming User.Id
                entity.HasIndex(e => e.Start);
                entity.HasIndex(e => e.End);
                // Consider FK to User entity:
                // entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId); (If User entity exists and is related)
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            // SleepCycle Configuration
            modelBuilder.Entity<SleepCycle>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.SleepId).IsUnique(); // Whoop's Sleep ID
                entity.HasIndex(e => e.UserId); // Foreign key
                entity.HasIndex(e => e.Start);
                entity.HasIndex(e => e.End);
                // Consider FK to User entity:
                // entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            // HeartRateSample Configuration
            modelBuilder.Entity<HeartRateSample>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.ActivityId); // For querying HR by activity
                entity.HasIndex(e => e.SleepCycleId); // For querying HR by sleep

                // Foreign Key to Activity (if ActivityId is FK to Activity.Id)
                // entity.HasOne<Activity>()
                //       .WithMany() // Or .WithMany(a => a.HeartRateSamples) if you add navigation property
                //       .HasForeignKey(hrs => hrs.ActivityId)
                //       .OnDelete(DeleteBehavior.Cascade); // Or SetNull, Restrict

                // Foreign Key to SleepCycle (if SleepCycleId is FK to SleepCycle.Id)
                // entity.HasOne<SleepCycle>()
                //       .WithMany() // Or .WithMany(sc => sc.HeartRateSamples)
                //       .HasForeignKey(hrs => hrs.SleepCycleId)
                //       .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            // SleepEvent Configuration
            modelBuilder.Entity<SleepEvent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.SleepId); // Index on Whoop's Sleep ID
                entity.HasIndex(e => e.Timestamp);
                // If SleepId refers to SleepCycle.SleepId (the long value from Whoop)
                // and not SleepCycle.Id (the int PK), then direct FK relationship setup needs care.
                // If you add a SleepCycleId (int FK to SleepCycle.Id) to SleepEvent, then:
                // entity.HasOne<SleepCycle>()
                //       .WithMany() // Or .WithMany(sc => sc.SleepEvents)
                //       .HasForeignKey(se => se.SleepCycleId);
                entity.Property(e => e.EventType).HasMaxLength(50); // Example length
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now', 'utc')");
            });

            // StressSample Configuration
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
        }
    }
}