// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.Core.Data\DesignTimeDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.IO;

namespace OpenWhoop.Core.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // You can get the path from args, environment variables, or a config file
            // For simplicity, using a hardcoded path for design-time.
            // Ensure this path is appropriate for your development environment.
            // It's often placed in the output directory or a user-specific location.
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "openwhoop_design.db");
            // Console.WriteLine($"DesignTimeDbContextFactory: Using database at {dbPath}");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}