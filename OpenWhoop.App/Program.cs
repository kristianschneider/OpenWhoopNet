using CommandLine;
using Microsoft.EntityFrameworkCore;
using OpenWhoop.App;
using OpenWhoop.Core.Data;
using OpenWhoopNet;
using System;
using System.IO;
using System.Linq; // Required for errs.ToList() if you use it
using System.Threading.Tasks;

public class Program
{
    private static async Task<int> ProcessVerb<T>(T options, Func<T, AppDbContext, Task<int>> commandRunner)
        where T : IVerbOptions // Ensure T implements IVerbOptions to access DatabaseUrl
    {
        string dbPath = options.DatabaseUrl;

        if (string.IsNullOrWhiteSpace(dbPath))
        {
            // This should ideally be caught by CommandLineParser due to Required=true on DatabaseUrl
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Database URL/path is required and was not provided or parsed correctly.");
            Console.ResetColor();
            return 1; // Error code
        }

        string fullDbPath = Path.GetFullPath(dbPath);
        var dbDirectory = Path.GetDirectoryName(fullDbPath);

        if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
        {
            try
            {
                Directory.CreateDirectory(dbDirectory);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error creating database directory '{dbDirectory}': {ex.Message}");
                Console.ResetColor();
                return 1; // Error code
            }
        }
        Console.WriteLine($"Using database at: {fullDbPath}");

        var dbContextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={fullDbPath}") // Use fullDbPath
            .Options;

        await using var dbContext = new AppDbContext(dbContextOptions);

        try
        {
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

        // Execute the specific command logic
        return await commandRunner(options, dbContext);
    }

    public static async Task<int> Main(string[] args)
    {
        var parserResult = Parser.Default.ParseArguments<
            ScanOptions,
            DownloadHistoryOptions,
            ReRunOptions,
            DetectEventsOptions,
            SleepStatsOptions,
            ExerciseStatsOptions,
            CalculateStressOptions,
            SetAlarmOptions,
            MergeOptions
            >(args);

        return await parserResult.MapResult(
            (ScanOptions opts) => ProcessVerb(opts, RunScanCommand),
            (DownloadHistoryOptions opts) => ProcessVerb(opts, RunDownloadHistoryCommand),
            (ReRunOptions opts) => ProcessVerb(opts, RunReRunCommand),
            (DetectEventsOptions opts) => ProcessVerb(opts, RunDetectEventsCommand),
            (SleepStatsOptions opts) => ProcessVerb(opts, RunSleepStatsCommand),
            (ExerciseStatsOptions opts) => ProcessVerb(opts, RunExerciseStatsCommand),
            (CalculateStressOptions opts) => ProcessVerb(opts, RunCalculateStressCommand),
            (SetAlarmOptions opts) => ProcessVerb(opts, RunSetAlarmCommand),
            (MergeOptions opts) => ProcessVerb(opts, RunMergeCommand),
            errs =>
            {
                // CommandLineParser automatically prints errors to the console.
                // You can add additional logging here if necessary.
                return Task.FromResult(1); // Return error code for parsing failure
            });
    }

    // Ensure all command handlers match the signature expected by ProcessVerb:
    // async Task<int> CommandHandlerName(SpecificOptionsType opts, AppDbContext dbContext)

    private static async Task<int> RunScanCommand(ScanOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"Scan command. Database URL from opts: {opts.DatabaseUrl}");
        var scanner = new WhoopDeviceScanner();
        await scanner.DisplayNearbyWhoopDevicesAsync(TimeSpan.FromSeconds(15)); // Scan for 15 seconds
        return 0;
    }

    private static async Task<int> RunDownloadHistoryCommand(DownloadHistoryOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"DownloadHistory: WhoopDeviceId = {opts.WhoopDeviceId}, DB: {opts.DatabaseUrl}");
        var scanner = new WhoopDeviceScanner();
        BluetoothLEDevice peripheral = await scanner.FindWhoopPeripheralAsync(opts.WhoopDeviceId, TimeSpan.FromSeconds(30));

        if (peripheral == null)
        {
            Console.WriteLine("Failed to find the specified Whoop device.");
            return 1;
        }

        Console.WriteLine($"Found device: {peripheral.Name}, ID: {peripheral.DeviceId}");
        // Next steps:
        // var whoopDevice = new WhoopDevice(peripheral, dbContext); // C# equivalent
        // await whoopDevice.ConnectAsync();
        // await whoopDevice.InitializeAsync();
        // await whoopDevice.SyncHistoryAsync();
        // ... ensure disconnection ...
        peripheral.Dispose(); // Dispose the device object when done
        return 0;
    }

    // ... other command handlers ...

    private static async Task<int> RunSetAlarmCommand(SetAlarmOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"SetAlarm: WhoopDeviceId = {opts.WhoopDeviceId}, AlarmTime String = {opts.AlarmTime}, DB: {opts.DatabaseUrl}");

        BluetoothLEDevice peripheral = null;
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

            var scanner = new WhoopDeviceScanner();
            peripheral = await scanner.FindWhoopPeripheralAsync(opts.WhoopDeviceId, TimeSpan.FromSeconds(30));

            if (peripheral == null)
            {
                Console.WriteLine("Failed to find the specified Whoop device for setting alarm.");
                return 1;
            }

            Console.WriteLine($"Found device for alarm: {peripheral.Name}, ID: {peripheral.DeviceId}");
            // TODO: Implement actual alarm setting logic using the peripheral and WhoopPacket C# equivalent
            // var whoopDevice = new WhoopDevice(peripheral, dbContext);
            // await whoopDevice.ConnectAsync();
            // var packet = WhoopPacketGenerator.SetAlarm((uint)alarmDateTimeUtc.ToUnixTimeSeconds());
            // await whoopDevice.SendCommandAsync(packet);
            // Console.WriteLine($"Alarm time set for: {alarmDateTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

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
        catch (Exception ex) // Catch other potential errors (e.g., from BLE)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        finally
        {
            peripheral?.Dispose(); // Ensure device is disposed
        }
        return 0;
    }

    private static async Task<int> RunReRunCommand(ReRunOptions opts, AppDbContext dbContext)
    {
        Console.WriteLine($"ReRun command. Database URL: {opts.DatabaseUrl}");
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


    private static async Task<int> RunMergeCommand(MergeOptions opts, AppDbContext dbContext)
    {
        // dbContext here is for the *target* database, as per opts.DatabaseUrl
        Console.WriteLine($"Merge: FromDatabaseUrl = {opts.FromDatabaseUrl}, Target DB: {opts.DatabaseUrl}");
        // TODO: Implement merge logic. This will involve creating another DbContext for opts.FromDatabaseUrl.
        return 0;
    }
}