// filepath: c:\Projects\Open Source\openwhoop\OpenWhoop.App\AlarmTime.cs
using System;
using System.Globalization;

public enum AlarmTimeType
{
    SpecificDateTime, // Represents a fully specified date and time
    SpecificTime,     // Represents a time on the current or next day
    RelativeMinute,
    RelativeMinute5,
    RelativeMinute10,
    RelativeMinute15,
    RelativeMinute30,
    RelativeHour
}

public class ParsedAlarmTime
{
    public AlarmTimeType Type { get; }
    public DateTime? SpecificDateTimeValue { get; } // Used when Type is SpecificDateTime
    public TimeSpan? SpecificTimeValue { get; }     // Used when Type is SpecificTime

    private ParsedAlarmTime(AlarmTimeType type, DateTime? specificDateTime = null, TimeSpan? specificTime = null)
    {
        Type = type;
        SpecificDateTimeValue = specificDateTime;
        SpecificTimeValue = specificTime;
    }

    public static ParsedAlarmTime FromSpecificDateTime(DateTime dateTime) => new ParsedAlarmTime(AlarmTimeType.SpecificDateTime, specificDateTime: dateTime);
    public static ParsedAlarmTime FromSpecificTime(TimeSpan time) => new ParsedAlarmTime(AlarmTimeType.SpecificTime, specificTime: time);
    public static ParsedAlarmTime FromRelative(AlarmTimeType relativeType)
    {
        if (relativeType == AlarmTimeType.SpecificDateTime || relativeType == AlarmTimeType.SpecificTime)
            throw new ArgumentException("Relative type cannot be SpecificDateTime or SpecificTime.", nameof(relativeType));
        return new ParsedAlarmTime(relativeType);
    }
}

public static class AlarmTimeConverter
{
    public static ParsedAlarmTime Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Alarm time string cannot be empty.", nameof(input));
        }

        // Try parsing as a full DateTime (e.g., "yyyy-MM-dd HH:mm:ss" or "yyyy-MM-ddTHH:mm:ss")
        string[] dateTimeFormats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm" };
        if (DateTime.TryParseExact(input, dateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDt))
        {
            return ParsedAlarmTime.FromSpecificDateTime(parsedDt);
        }

        // Try parsing as a Time (e.g., "HH:mm:ss" or "HH:mm")
        string[] timeFormats = { "HH:mm:ss", "HH:mm" };
        if (TimeSpan.TryParseExact(input, timeFormats, CultureInfo.InvariantCulture, out TimeSpan parsedTime))
        {
            return ParsedAlarmTime.FromSpecificTime(parsedTime);
        }

        switch (input.ToLowerInvariant())
        {
            case "minute":
            case "1min":
            case "min":
                return ParsedAlarmTime.FromRelative(AlarmTimeType.RelativeMinute);
            case "5minute":
            case "5min":
                return ParsedAlarmTime.FromRelative(AlarmTimeType.RelativeMinute5);
            case "10minute":
            case "10min":
                return ParsedAlarmTime.FromRelative(AlarmTimeType.RelativeMinute10);
            case "15minute":
            case "15min":
                return ParsedAlarmTime.FromRelative(AlarmTimeType.RelativeMinute15);
            case "30minute":
            case "30min":
                return ParsedAlarmTime.FromRelative(AlarmTimeType.RelativeMinute30);
            case "hour":
            case "h":
                return ParsedAlarmTime.FromRelative(AlarmTimeType.RelativeHour);
            default:
                throw new FormatException($"Invalid alarm time string: '{input}'. Expected a specific date/time, time, or a relative offset (e.g., 'min', '5min', 'hour').");
        }
    }

    public static DateTimeOffset ToUtcDateTimeOffset(ParsedAlarmTime parsedAlarmTime)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset alarmTimeCandidate;

        switch (parsedAlarmTime.Type)
        {
            case AlarmTimeType.SpecificDateTime:
                // Assume the parsed DateTime is in local time if no kind is specified, then convert to UTC.
                // If it has Kind=Utc, it's used as is. If Kind=Local, it's converted.
                // For simplicity, let's assume it's intended as local if not specified.
                var dt = parsedAlarmTime.SpecificDateTimeValue.Value;
                DateTime localDt = DateTime.SpecifyKind(dt, dt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Local : dt.Kind);
                alarmTimeCandidate = new DateTimeOffset(localDt).ToUniversalTime();
                break;

            case AlarmTimeType.SpecificTime:
                TimeSpan time = parsedAlarmTime.SpecificTimeValue.Value;
                DateTime today = DateTime.Today; // Local date
                DateTime specificLocalTime = today.Add(time);

                // If the time has already passed today, set it for tomorrow
                if (specificLocalTime < DateTime.Now)
                {
                    specificLocalTime = specificLocalTime.AddDays(1);
                }
                alarmTimeCandidate = new DateTimeOffset(specificLocalTime).ToUniversalTime();
                break;

            case AlarmTimeType.RelativeMinute:
                alarmTimeCandidate = nowUtc.AddMinutes(1);
                break;
            case AlarmTimeType.RelativeMinute5:
                alarmTimeCandidate = nowUtc.AddMinutes(5);
                break;
            case AlarmTimeType.RelativeMinute10:
                alarmTimeCandidate = nowUtc.AddMinutes(10);
                break;
            case AlarmTimeType.RelativeMinute15:
                alarmTimeCandidate = nowUtc.AddMinutes(15);
                break;
            case AlarmTimeType.RelativeMinute30:
                alarmTimeCandidate = nowUtc.AddMinutes(30);
                break;
            case AlarmTimeType.RelativeHour:
                alarmTimeCandidate = nowUtc.AddHours(1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(parsedAlarmTime.Type), "Unknown alarm time type.");
        }
        return alarmTimeCandidate;
    }
}