using System.Globalization;
using Quartz;
using Scheduler.Domain;

namespace Scheduler.Application;

public sealed record SchedulePreview(string CronExpression, string TimeZone,
    DateTimeOffset? NextRunUtc, IReadOnlyList<DateTimeOffset> UpcomingUtc);

public sealed class ScheduleValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class ScheduleCalculator(TimeProvider timeProvider)
{
    public const string DefaultTimeZone = "Asia/Taipei";

    public SchedulePreview Preview(ScheduleSettings settings, string? timeZone)
    {
        var zoneId = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim();
        if (zoneId.Length > 128 ||
            (zoneId != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneId, out _)))
            throw new ScheduleValidationException("timeZone", "請輸入有效的 IANA 時區，例如 Asia/Taipei。");

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ScheduleValidationException("timeZone", "這個時區在伺服器上無法使用。");
        }

        var cron = Build(settings);
        var expression = new CronExpression(cron) { TimeZone = zone };
        var upcoming = new List<DateTimeOffset>(5);
        var cursor = timeProvider.GetUtcNow();
        for (var index = 0; index < 5; index++)
        {
            if (expression.GetNextValidTimeAfter(cursor) is not { } next)
                break;
            cursor = next.ToUniversalTime();
            upcoming.Add(cursor);
        }

        return new(cron, zoneId, upcoming.Count > 0 ? upcoming[0] : null, upcoming);
    }

    private static string Build(ScheduleSettings settings) => settings.Type switch
    {
        ScheduleType.EveryMinutes => $"0 0/{Field(settings.IntervalMinutes, 1, 59, "intervalMinutes")} * * * ?",
        ScheduleType.Hourly => $"0 {Field(settings.Minute, 0, 59, "minute")} * * * ?",
        ScheduleType.Daily => $"0 {Field(settings.Minute, 0, 59, "minute")} {Field(settings.Hour, 0, 23, "hour")} * * ?",
        ScheduleType.Weekly => $"0 {Field(settings.Minute, 0, 59, "minute")} {Field(settings.Hour, 0, 23, "hour")} ? * {Weekday(settings.DayOfWeek)}",
        ScheduleType.Monthly => $"0 {Field(settings.Minute, 0, 59, "minute")} {Field(settings.Hour, 0, 23, "hour")} {Field(settings.DayOfMonth, 1, 31, "dayOfMonth")} * ?",
        _ => throw new ScheduleValidationException("type", "請選擇排程週期。")
    };

    private static string Field(int? value, int min, int max, string name)
    {
        if (!value.HasValue || value.Value < min || value.Value > max)
            throw new ScheduleValidationException(name, $"數值須介於 {min} 至 {max}。");
        return value.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Weekday(DayOfWeek? value)
    {
        if (!value.HasValue || !Enum.IsDefined(value.Value))
            throw new ScheduleValidationException("dayOfWeek", "請選擇有效的星期。");
        return value.Value.ToString()[..3].ToUpperInvariant();
    }
}
