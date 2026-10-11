using System.Globalization;
using System.Text.RegularExpressions;

namespace Roma.Domain.Common;

public readonly record struct LocalParts(int Year, int Month, int Day, int Hour, int Minute)
{
    public string DayString => $"{Year:D4}-{Month:D2}-{Day:D2}";
    public string TimeString => $"{Hour:D2}:{Minute:D2}";
    public string IsoString => $"{DayString}T{TimeString}";

    public static LocalParts FromStrings(string day, string time)
    {
        var dParts = day.Split('-');
        var tParts = time.Split(':');
        return new LocalParts(
            int.Parse(dParts[0], CultureInfo.InvariantCulture),
            int.Parse(dParts[1], CultureInfo.InvariantCulture),
            int.Parse(dParts[2], CultureInfo.InvariantCulture),
            int.Parse(tParts[0], CultureInfo.InvariantCulture),
            int.Parse(tParts[1], CultureInfo.InvariantCulture)
        );
    }
}

public static class TimeUtils
{
    private static readonly Regex OffsetRegex = new(@"^(-?\d+)([dhm])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static LocalParts GetLocalParts(DateTimeOffset utcNow, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(utcNow, tz);
        return new LocalParts(local.Year, local.Month, local.Day, local.Hour, local.Minute);
    }

    public static DateTimeOffset DayTimeToUtc(string day, string time, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var parts = LocalParts.FromStrings(day, time);
        var dt = new DateTime(parts.Year, parts.Month, parts.Day, parts.Hour, parts.Minute, 0, DateTimeKind.Unspecified);

        if (tz.IsInvalidTime(dt))
        {
            // Fall forward past the DST gap
            var adjustment = tz.GetAdjustmentRules();
            dt = dt.AddHours(1);
        }

        var offset = tz.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset).ToUniversalTime();
    }

    public static string FormatDuration(int seconds)
    {
        if (seconds < 0) seconds = 0;
        var totalMinutes = seconds / 60;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        if (hours > 0)
        {
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        }

        return $"{minutes}m";
    }

    public static int[] ParseOffsets(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return Array.Empty<int>();
        }

        var items = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<int>(items.Length);
        var seen = new HashSet<int>();

        foreach (var item in items)
        {
            if (item == "0" || item == "+0" || item == "-0")
            {
                if (!seen.Add(0))
                    throw new ArgumentException($"Duplicate offset detected: {item}");
                result.Add(0);
                continue;
            }

            var match = OffsetRegex.Match(item);
            if (!match.Success)
            {
                throw new ArgumentException($"Invalid offset format: '{item}'. Expected format like '-7d', '-2h', '-30m', or '0'.");
            }

            var val = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = match.Groups[2].Value.ToLowerInvariant();

            var minutes = unit switch
            {
                "d" => val * 24 * 60,
                "h" => val * 60,
                "m" => val,
                _ => throw new ArgumentException($"Unsupported unit '{unit}' in offset '{item}'.")
            };

            if (!seen.Add(minutes))
            {
                throw new ArgumentException($"Duplicate offset detected: {item} ({minutes} minutes)");
            }

            result.Add(minutes);
        }

        return result.OrderBy(x => x).ToArray();
    }

    public static string CalcDueAt(LocalParts moment, int offsetMinutes)
    {
        var dt = new DateTime(moment.Year, moment.Month, moment.Day, moment.Hour, moment.Minute, 0, DateTimeKind.Unspecified);
        var target = dt.AddMinutes(offsetMinutes);
        return $"{target.Year:D4}-{target.Month:D2}-{target.Day:D2}T{target.Hour:D2}:{target.Minute:D2}";
    }
}
