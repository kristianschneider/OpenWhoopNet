using CommandLine;
using Microsoft.EntityFrameworkCore; // Add this
using OpenWhoop.Core.Data; // Add this
using System;
using System.IO; // Add this
using System.Threading.Tasks;
using static OpenWhoopNet.CommandLineOptions;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("OpenWhoop C# Port");

        // Temporary: Parse args just to get DatabaseUrl for context creation
        // This is a bit simplistic; a more robust DI setup would be better for larger apps.
        string dbPath = "default_openwhoop.db"; // Default if not parsed or provided
        Parser.Default.ParseArguments<BaseOptionsForDbPathOnly>(args)
            .WithParsed<BaseOptionsForDbPathOnly>(opts =>
            {
                dbPath = opts.DatabaseUrl;
            });

        // Ensure the directory for the database exists
        var dbDirectory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }
        Console.WriteLine($"Using database at: {Path.GetFullPath(dbPath)}");

        // Setup DbContextOptions
        var dbContextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        // Create DbContext instance
        await using var dbContext = new AppDbContext(dbContextOptions);

        try
        {
            // Automatically apply pending migrations
            // In a production app, you might want more control over this step
            Console.WriteLine("Applying database migrations...");
            await dbContext.Database.MigrateAsync();
            Console.WriteLine("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error applying database migrations: {ex.Message}");
            Console.ResetColor();
            return -1; // Indicate migration failure
        }


        return await Parser.Default.ParseArguments<
            ScanOptions,
            DownloadHistoryOptions,
            ReRunOptions,
            DetectEventsOptions,
            SleepStatsOptions,
            ExerciseStatsOptions,
            CalculateStressOptions,
            SetAlarmOptions,
            MergeOptions
            >(args)
            .MapResult(
                async (ScanOptions opts) => await RunScanCommand(opts, dbContext),
                async (DownloadHistoryOptions opts) => await RunDownloadHistoryCommand(opts, dbContext),
                async (ReRunOptions opts) => await RunReRunCommand(opts, dbContext),
                async (DetectEventsOptions opts) => await RunDetectEventsCommand(opts, dbContext),
                async (SleepStatsOptions opts) => await RunSleepStatsCommand(opts, dbContext),
                async (ExerciseStatsOptions opts) => await RunExerciseStatsCommand(opts, dbContext),
                async (CalculateStressOptions opts) => await RunCalculateStressCommand(opts, dbContext),
                async (SetAlarmOptions opts) => await RunSetAlarmCommand(opts, dbContext),
                async (MergeOptions opts) => await RunMergeCommand(opts, dbContext),
                errs => Task.FromResult(1) // Error handling
            );
    }

    // A helper class to parse just the DatabaseUrl for initial DbContext setup
    // This is a workaround because CommandLineParser processes verbs and options together.
    // A more elegant solution involves a proper DI container or a two-pass parse.
    private class BaseOptionsForDbPathOnly
    {
        [Option("database-url", Required = false, HelpText = "Database connection string (path for SQLite).")]
        public string DatabaseUrl { get; set; } = "openwhoop.db"; // Default value
    }

    // Modify command handlers to accept AppDbContext
    private static async Task<int> RunScanCommand(ScanOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"Scan command. Database URL from opts: {opts.DatabaseUrl}");
        Console.WriteLine($"DbContext is available: {dbContext != null}");
        // TODO: Implement scan logic using dbContext
        return 0;
    }

    private static async Task<int> RunDownloadHistoryCommand(DownloadHistoryOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"DownloadHistory: WhoopDeviceId = {opts.WhoopDeviceId}, DB: {opts.DatabaseUrl}");
        // TODO: Implement download history logic using dbContext
        return 0;
    }

    private static async Task<int> RunReRunCommand(ReRunOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"ReRun command. Database URL: {opts.DatabaseUrl}");
        // Example: Accessing packets
        // var packets = await dbContext.Packets.OrderBy(p => p.Id).Take(10).ToListAsync();
        // Console.WriteLine($"Found {packets.Count} packets to start.");
        // TODO: Implement rerun logic
        return 0;
    }

    private static async Task<int> RunDetectEventsCommand(DetectEventsOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"DetectEvents command. Database URL: {opts.DatabaseUrl}");
        // TODO: Implement detect events logic
        return 0;
    }

    private static async Task<int> RunSleepStatsCommand(SleepStatsOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"SleepStats command. Database URL: {opts.DatabaseUrl}");
        // TODO: Implement sleep stats logic
        return 0;
    }

    private static async Task<int> RunExerciseStatsCommand(ExerciseStatsOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"ExerciseStats command. Database URL: {opts.DatabaseUrl}");
        // TODO: Implement exercise stats logic
        return 0;
    }

    private static async Task<int> RunCalculateStressCommand(CalculateStressOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"CalculateStress command. Database URL: {opts.DatabaseUrl}");
        // TODO: Implement calculate stress logic
        return 0;
    }

    private static async Task<int> RunSetAlarmCommand(SetAlarmOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"SetAlarm: WhoopDeviceId = {opts.WhoopDeviceId}, AlarmTime String = {opts.AlarmTime}, DB: {opts.DatabaseUrl}");
        try
        {
            ParsedAlarmTime parsedAlarm = AlarmTimeConverter.Parse(opts.AlarmTime);
            DateTimeOffset alarmDateTimeUtc = AlarmTimeConverter.ToUtcDateTimeOffset(parsedAlarm);
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

            if (alarmDateTimeUtc <= nowUtc)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Calculated alarm time {alarmDateTimeUtc:yyyy-MM-dd HH:mm:ss'Z'} is in the past or is now. Current time: {nowUtc:yyyy-MM-dd HH:mm:ss'Z'}");
                Console.ResetColor();
                return 1;
            }
            Console.WriteLine($"Calculated UTC alarm time: {alarmDateTimeUtc:yyyy-MM-dd HH:mm:ss'Z'}");
            // TODO: Implement actual alarm setting logic
        }
        catch (FormatException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error parsing alarm time: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error with alarm time argument: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        return 0;
    }

    private static async Task<int> RunMergeCommand(MergeOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"Merge: FromDatabaseUrl = {opts.FromDatabaseUrl}, Target DB: {opts.DatabaseUrl}");
        // TODO: Implement merge logic
        return 0;
    }
}