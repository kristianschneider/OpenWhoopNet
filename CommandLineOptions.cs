using CommandLine;

namespace OpenWhoopNet;

public class CommandLineOptions
{

    [Verb("scan", HelpText = "Scan for Whoop devices.")]
    public class ScanOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }
    }

    [Verb("download-history", HelpText = "Download history data from whoop devices.")]
    public class DownloadHistoryOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }

        [Option("whoop", Required = true, HelpText = "Whoop device ID (MAC address on Linux, part of the name on macOS).")]
        public string WhoopDeviceId { get; set; }
    }

    [Verb("rerun", HelpText = "Reruns the packet processing on stored packets.")]
    public class ReRunOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }
    }

    [Verb("detect-events", HelpText = "Detects sleeps and exercises.")]
    public class DetectEventsOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }
    }

    [Verb("sleep-stats", HelpText = "Print sleep statistics for all time and last week.")]
    public class SleepStatsOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }
    }

    [Verb("exercise-stats", HelpText = "Print activity statistics for all time and last week.")]
    public class ExerciseStatsOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }
    }

    [Verb("calculate-stress", HelpText = "Calculate stress for historical data.")]
    public class CalculateStressOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }
    }

    [Verb("set-alarm", HelpText = "Set alarm.")]
    public class SetAlarmOptions
    {
        [Option("database-url", Required = true, HelpText = "Database connection string.")]
        public string DatabaseUrl { get; set; }

        [Option("whoop", Required = true, HelpText = "Whoop device ID.")]
        public string WhoopDeviceId { get; set; }

        [Option("alarm-time", Required = true, HelpText = "Alarm time (e.g., 'HH:mm', 'YYYY-MM-DD HH:mm:ss', 'minute', '5min', 'hour').")]
        public string AlarmTime { get; set; }
    }

    [Verb("merge", HelpText = "Copy packets from one database into another.")]
    public class MergeOptions
    {
        [Option("database-url", Required = true, HelpText = "Target database connection string.")] // This is the target DB for merge
        public string DatabaseUrl { get; set; }

        [Option("from", Required = true, HelpText = "Source database connection string.")]
        public string FromDatabaseUrl { get; set; }
    }
}