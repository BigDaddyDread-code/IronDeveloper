using System;
using System.Globalization;

namespace IronDev.Core.Time;

public static class DateTimeDisplay
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.InvariantCulture;

    public static string ToRelativeDisplay(DateTime utc)
        => ToRelativeDisplay(ToUtcOffset(utc));

    public static string ToRelativeDisplay(DateTimeOffset utc)
    {
        var value = utc.ToUniversalTime();
        var elapsed = DateTimeOffset.UtcNow - value;

        if (elapsed.TotalSeconds < 0)
            elapsed = TimeSpan.Zero;

        if (elapsed.TotalMinutes < 1)
            return "just now";

        if (elapsed.TotalHours < 1)
            return $"{Math.Floor(elapsed.TotalMinutes)}m ago";

        if (elapsed.TotalDays < 1)
            return $"{Math.Floor(elapsed.TotalHours)}h ago";

        if (elapsed.TotalDays < 30)
            return $"{Math.Floor(elapsed.TotalDays)}d ago";

        return ToLocalDisplay(value);
    }

    public static string ToLocalDisplay(DateTime utc)
        => ToLocalDisplay(ToUtcOffset(utc));

    public static string ToLocalDisplay(DateTimeOffset utc)
        => utc
            .ToUniversalTime()
            .ToLocalTime()
            .ToString("d MMM yyyy, HH:mm", DisplayCulture);

    public static string ToUtcMetadata(DateTime utc)
        => ToUtcMetadata(ToUtcOffset(utc));

    public static string ToUtcMetadata(DateTimeOffset utc)
        => utc
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm 'UTC'", DisplayCulture);

    public static string ToUtcTooltip(DateTime utc)
        => ToUtcTooltip(ToUtcOffset(utc));

    public static string ToUtcTooltip(DateTimeOffset utc)
        => utc
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z' 'UTC'", DisplayCulture);

    public static string ToCompactMetadata(DateTime utc, string label)
        => ToCompactMetadata(ToUtcOffset(utc), label);

    public static string ToCompactMetadata(DateTimeOffset utc, string label)
        => $"{label} {ToRelativeDisplay(utc)} - {ToUtcMetadata(utc)}";

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utc);
    }
}
