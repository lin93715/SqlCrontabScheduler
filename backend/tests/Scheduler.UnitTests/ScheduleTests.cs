using Scheduler.Application;
using Scheduler.Domain;

namespace Scheduler.UnitTests;

public sealed class ScheduleTests
{
    private static ScheduleCalculator CalculatorAt(string instant) => new(new FixedTime(DateTimeOffset.Parse(instant)));

    [Theory]
    [InlineData(ScheduleType.EveryMinutes, "0 0/5 * * * ?")]
    [InlineData(ScheduleType.Hourly, "0 30 * * * ?")]
    [InlineData(ScheduleType.Daily, "0 30 8 * * ?")]
    [InlineData(ScheduleType.Weekly, "0 30 8 ? * MON")]
    [InlineData(ScheduleType.Monthly, "0 30 8 31 * ?")]
    public void Formats_quartz_cron_and_returns_five_ordered_instants(ScheduleType type, string cron)
    {
        var calculator = CalculatorAt("2026-11-01T00:02:00Z");
        var preview = calculator.Preview(new(type, 5, 30, 8, DayOfWeek.Monday, 31), null);

        Assert.Equal(cron, preview.CronExpression);
        Assert.Equal("Asia/Taipei", preview.TimeZone);
        Assert.Equal(5, preview.UpcomingUtc.Count);
        Assert.Equal(preview.UpcomingUtc[0], preview.NextRunUtc);
        Assert.All(preview.UpcomingUtc, item => Assert.Equal(TimeSpan.Zero, item.Offset));
        Assert.True(preview.UpcomingUtc.Zip(preview.UpcomingUtc.Skip(1)).All(pair => pair.First < pair.Second));
    }

    [Fact]
    public void Minute_steps_start_at_minute_zero()
    {
        var calculator = CalculatorAt("2026-11-01T00:55:00Z");
        var preview = calculator.Preview(new(ScheduleType.EveryMinutes, IntervalMinutes: 7), "UTC");
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T00:56:00Z"), preview.NextRunUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T01:00:00Z"), preview.UpcomingUtc[1]);
    }

    [Theory]
    [InlineData("2027-01-29T00:01:00Z", "2027-03-29T00:00:00Z")]
    [InlineData("2028-01-29T00:01:00Z", "2028-02-29T00:00:00Z")]
    public void Monthly_skips_absent_dates_and_observes_leap_days(string from, string expected)
    {
        var preview = CalculatorAt(from).Preview(new(ScheduleType.Monthly, Minute: 0, Hour: 0, DayOfMonth: 29), "UTC");
        Assert.Equal(DateTimeOffset.Parse(expected), preview.NextRunUtc);
    }

    [Fact]
    public void Local_hour_is_preserved_across_daylight_saving_change()
    {
        var preview = CalculatorAt("2026-03-07T02:00:00Z")
            .Preview(new(ScheduleType.Daily, Minute: 0, Hour: 9), "America/New_York");
        Assert.Equal(DateTimeOffset.Parse("2026-03-07T14:00:00Z"), preview.UpcomingUtc[0]);
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T13:00:00Z"), preview.UpcomingUtc[1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(60)]
    public void Validates_minute_steps(int? interval)
    {
        var ex = Assert.Throws<ScheduleValidationException>(() =>
            CalculatorAt("2026-11-01T00:00:00Z").Preview(new(ScheduleType.EveryMinutes, IntervalMinutes: interval), null));
        Assert.Equal("intervalMinutes", ex.Field);
    }

    [Fact]
    public void Rejects_invalid_time_zone()
    {
        var ex = Assert.Throws<ScheduleValidationException>(() =>
            CalculatorAt("2026-11-01T00:00:00Z").Preview(new(ScheduleType.Hourly, Minute: 0), "Invalid/Zone"));
        Assert.Equal("timeZone", ex.Field);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
