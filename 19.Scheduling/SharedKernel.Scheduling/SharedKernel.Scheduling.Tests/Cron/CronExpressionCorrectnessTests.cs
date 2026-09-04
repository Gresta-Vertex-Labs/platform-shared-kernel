using FluentAssertions;
using Quartz;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Cron;

/// <summary>
/// T-04 — proves next-fire-time computation goes through Quartz's standalone
/// <see cref="CronExpression"/> rather than a hand-rolled parser, by exercising exactly the cases a
/// naive implementation gets wrong: a DST spring-forward gap, a DST fall-back duplicated hour, and the
/// <c>W</c>/<c>#</c> specifiers.
/// </summary>
/// <remarks>
/// This package (<c>ScheduledJobRegistry.ParseCron</c>) always anchors <c>CronExpression.TimeZone</c>
/// to <see cref="TimeZoneInfo.Utc"/> — UTC has no DST transitions, so this domain's own runtime
/// behavior is deliberately DST-invariant. These tests instead exercise <see cref="CronExpression"/>
/// directly with a real DST-observing zone, proving the underlying library dependency choice (Domain
/// Invariant 1) is doing genuine, correct work — not that this package's own output varies by DST.
/// </remarks>
public sealed class CronExpressionCorrectnessTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void GetTimeAfter_SpringForwardGap_SkipsToNextValidLocalTime()
    {
        // "0 30 2 * * ?" = 02:30 every day. On 2025-03-09 in America/New_York, clocks jump from
        // 02:00 EST straight to 03:00 EDT — 02:30 local time never occurs that day. A hand-rolled
        // parser that naively adds "one day" in UTC, or that doesn't re-validate the computed local
        // time against the zone's transition table, would either throw or silently return a
        // nonexistent instant.
        var cron = new CronExpression("0 30 2 * * ?") { TimeZone = Eastern };
        var beforeTransition = new DateTimeOffset(2025, 3, 8, 12, 0, 0, TimeSpan.FromHours(-5));

        DateTimeOffset? next = cron.GetTimeAfter(beforeTransition);

        next.Should().NotBeNull();
        DateTimeOffset localNext = TimeZoneInfo.ConvertTime(next!.Value, Eastern);
        localNext.Date.Should().Be(new DateTime(2025, 3, 9));
        // The nonexistent 02:30 is skipped forward to the first valid local time at/after it — 03:30 EDT.
        localNext.TimeOfDay.Should().Be(TimeSpan.FromHours(3.5));
        localNext.Offset.Should().Be(TimeSpan.FromHours(-4)); // EDT, confirming DST was actually crossed
    }

    [Fact]
    public void GetTimeAfter_FallBackDuplicatedHour_ReturnsFirstOccurrence()
    {
        // "0 30 1 * * ?" = 01:30 every day. On 2025-11-02 in America/New_York, 01:00-02:00 local
        // time occurs TWICE (EDT then EST). A hand-rolled parser working in naive local time could
        // either miss the occurrence entirely or double-count it; CronExpression must return exactly
        // one well-defined instant.
        var cron = new CronExpression("0 30 1 * * ?") { TimeZone = Eastern };
        var beforeTransition = new DateTimeOffset(2025, 11, 1, 12, 0, 0, TimeSpan.FromHours(-4));

        DateTimeOffset? next = cron.GetTimeAfter(beforeTransition);

        next.Should().NotBeNull();
        DateTimeOffset localNext = TimeZoneInfo.ConvertTime(next!.Value, Eastern);
        localNext.Date.Should().Be(new DateTime(2025, 11, 2));
        localNext.TimeOfDay.Should().Be(TimeSpan.FromHours(1.5));

        // A second call starting just after the first result must land on the NEXT calendar day's
        // 01:30 — never re-fire for the duplicated hour a second time.
        DateTimeOffset? afterFirst = cron.GetTimeAfter(next.Value);
        DateTimeOffset localAfterFirst = TimeZoneInfo.ConvertTime(afterFirst!.Value, Eastern);
        localAfterFirst.Date.Should().Be(new DateTime(2025, 11, 3));
    }

    [Fact]
    public void GetTimeAfter_WSpecifier_ResolvesToNearestWeekday()
    {
        // "0 0 12 15W * ?" = the nearest weekday to the 15th of each month, at 12:00.
        // 2026-08-15 is a Saturday, so the nearest weekday is Friday 2026-08-14.
        var cron = new CronExpression("0 0 12 15W * ?") { TimeZone = TimeZoneInfo.Utc };
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        DateTimeOffset? next = cron.GetTimeAfter(start);

        next.Should().NotBeNull();
        next!.Value.Date.Should().Be(new DateTime(2026, 8, 14));
        next.Value.DayOfWeek.Should().Be(DayOfWeek.Friday);
    }

    [Fact]
    public void GetTimeAfter_HashSpecifier_ResolvesToNthWeekdayOfMonth()
    {
        // "0 0 9 ? * 6#3" = the 3rd Friday of each month, at 09:00 (day-of-week 6 = Friday in
        // Quartz's 1-based Sunday=1 convention).
        var cron = new CronExpression("0 0 9 ? * 6#3") { TimeZone = TimeZoneInfo.Utc };
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        DateTimeOffset? next = cron.GetTimeAfter(start);

        next.Should().NotBeNull();
        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Friday);
        // Third Friday of September 2026 is the 18th.
        next.Value.Date.Should().Be(new DateTime(2026, 9, 18));
    }

    [Fact]
    public void GetTimeAfter_LSpecifier_ResolvesToLastDayOfMonth()
    {
        // "0 0 23 L * ?" = 23:00 on the last day of each month. February 2026 is not a leap year (28 days).
        var cron = new CronExpression("0 0 23 L * ?") { TimeZone = TimeZoneInfo.Utc };
        var start = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        DateTimeOffset? next = cron.GetTimeAfter(start);

        next.Should().NotBeNull();
        next!.Value.Date.Should().Be(new DateTime(2026, 2, 28));
    }
}
