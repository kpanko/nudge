using CsCheck;

namespace Nudge.Core.Tests;

/// <summary>
/// Property-based tests. Anchors span 2019 to 2033, which covers
/// three leap days, every month length and many clock changes.
/// Expected values come from arithmetic written independently of
/// the engine's day-by-day search.
/// </summary>
public class RecurrencePropertyTests
{
    private static readonly Gen<DateOnly> GenDate =
        Gen.Int[
            new DateOnly(2019, 1, 1).DayNumber,
            new DateOnly(2033, 12, 31).DayNumber]
        .Select(DateOnly.FromDayNumber);

    private static readonly Gen<int> GenInterval = Gen.Int[1, 14];

    private static readonly Gen<int> GenDelay = Gen.Int[0, 800];

    private static readonly Gen<Frequency> GenFrequency =
        Gen.Enum<Frequency>();

    private static readonly Gen<string> GenRuleText = Gen.OneOf(
        Gen.Select(GenFrequency, GenInterval)
            .Select(t => Simple(t.Item1, t.Item2)),
        Gen.Select(Gen.Int[1, 4], Gen.Int[1, 127])
            .Select(t => $"FREQ=WEEKLY;INTERVAL={t.Item1};BYDAY="
                + Weekdays(t.Item2)),
        Gen.Select(Gen.Int[1, 6], Gen.Int[-31, 31].Where(d => d != 0))
            .Select(t => $"FREQ=MONTHLY;INTERVAL={t.Item1};BYMONTHDAY="
                + t.Item2),
        Gen.Select(
                Gen.Int[1, 6],
                Gen.Int[-4, 4].Where(n => n != 0),
                Gen.Int[0, 6])
            .Select(t => $"FREQ=MONTHLY;INTERVAL={t.Item1};BYDAY="
                + t.Item2 + Code(t.Item3)),
        Gen.Select(Gen.Int[1, 3], Gen.Int[1, 12], Gen.Int[1, 31])
            .Select(t => $"FREQ=YEARLY;INTERVAL={t.Item1};BYMONTH="
                + t.Item2 + ";BYMONTHDAY=" + t.Item3),
        Gen.Select(Gen.Int[1, 5], Gen.Int[1, 127])
            .Select(t => $"FREQ=DAILY;INTERVAL={t.Item1};BYDAY="
                + Weekdays(t.Item2)));

    private static readonly string[] ZoneIds =
    [
        "America/New_York",
        "Europe/London",
        "Australia/Lord_Howe",
        "America/Sao_Paulo",
        "Pacific/Auckland",
    ];

    private static string Simple(
        Frequency frequency,
        int interval)
    {
        return $"FREQ={frequency.ToString().ToUpperInvariant()}"
            + $";INTERVAL={interval}";
    }

    private static string Code(int day)
    {
        return new[] { "SU", "MO", "TU", "WE", "TH", "FR", "SA" }[day];
    }

    private static string Weekdays(int mask)
    {
        return string.Join(
            ',',
            Enumerable.Range(0, 7)
                .Where(d => (mask & (1 << d)) != 0)
                .Select(Code));
    }

    /// <summary>The nth date of a rule with no BY parts, computed
    /// directly from the anchor.</summary>
    private static DateOnly Nth(
        Frequency frequency,
        int interval,
        DateOnly anchor,
        int n)
    {
        return frequency switch
        {
            Frequency.Daily => anchor.AddDays(n * interval),
            Frequency.Weekly => anchor.AddDays(7 * n * interval),
            Frequency.Monthly => anchor.AddMonths(n * interval),
            _ => anchor.AddYears(n * interval),
        };
    }

    [Fact]
    public void SimpleRules_ProduceAnchorPlusWholeIntervals()
    {
        Gen.Select(GenFrequency, GenInterval, GenDate)
            .Sample(t =>
            {
                var (frequency, interval, anchor) = t;
                var rule = RecurrenceRule.Parse(
                    Simple(frequency, interval));

                var current = anchor;
                for (var n = 1; n <= 12; n++)
                {
                    var next = rule.NextAfter(anchor, current);
                    Assert.Equal(
                        Nth(frequency, interval, anchor, n),
                        next);
                    current = next!.Value;
                }
            });
    }

    [Fact]
    public void NextAfter_IsTheFirstLaterOccurrence()
    {
        Gen.Select(GenRuleText, GenDate, GenDelay)
            .Sample(t =>
            {
                var (text, anchor, delay) = t;
                var rule = RecurrenceRule.Parse(text);
                var after = anchor.AddDays(delay - 30);

                var next = rule.NextAfter(anchor, after);

                Assert.NotNull(next);
                Assert.True(next > after);
                Assert.True(next >= anchor);
                Assert.True(rule.IsOccurrence(anchor, next.Value));
                for (var d = after.AddDays(1); d < next; d = d.AddDays(1))
                {
                    Assert.False(rule.IsOccurrence(anchor, d));
                }
            });
    }

    [Fact]
    public void FixedSchedule_NeverDriftsWhateverTheCompletionDays()
    {
        Gen.Select(GenRuleText, GenDate, Gen.Int[-3, 40].Array[1, 8])
            .Sample(t =>
            {
                var (text, anchor, delays) = t;
                var rule = RecurrenceRule.Parse(text);
                var recurrence = new Recurrence(
                    rule,
                    RecurrenceMode.FixedSchedule,
                    anchor);
                var occurrence = rule.NextOnOrAfter(anchor, anchor);
                Assert.NotNull(occurrence);

                foreach (var delay in delays)
                {
                    var completedOn = occurrence.Value.AddDays(delay);
                    var next = recurrence.NextAfterCompletion(
                        occurrence.Value,
                        completedOn);

                    // Still on the original series, moved forward,
                    // and by no more than one occurrence beyond the
                    // completion day.
                    Assert.NotNull(next);
                    Assert.True(rule.IsOccurrence(anchor, next.Value));
                    Assert.True(next > occurrence);
                    Assert.True(next > completedOn);
                    var floor = completedOn > occurrence
                        ? completedOn
                        : occurrence.Value;
                    Assert.Equal(rule.NextAfter(anchor, floor), next);
                    occurrence = next;
                }
            });
    }

    [Fact]
    public void AfterCompletion_IsExactlyOneIntervalLater()
    {
        Gen.Select(GenFrequency, GenInterval, GenDate, GenDate)
            .Sample(t =>
            {
                var (frequency, interval, occurrence, completedOn) = t;
                var recurrence = new Recurrence(
                    RecurrenceRule.Parse(Simple(frequency, interval)),
                    RecurrenceMode.AfterCompletion,
                    new DateOnly(2019, 1, 1));

                var next = recurrence.NextAfterCompletion(
                    occurrence,
                    completedOn);

                Assert.Equal(
                    Nth(frequency, interval, completedOn, 1),
                    next);
                Assert.True(next > completedOn);
            });
    }

    [Fact]
    public void AfterCompletion_WithByParts_WaitsAtLeastOneInterval()
    {
        Gen.Select(GenRuleText, GenDate)
            .Sample(t =>
            {
                var (text, completedOn) = t;
                var rule = RecurrenceRule.Parse(text);

                var next = rule.NextCountedFrom(completedOn);

                var earliest = Nth(
                    rule.Frequency,
                    rule.Interval,
                    completedOn,
                    1);
                Assert.NotNull(next);
                Assert.True(next >= earliest);
                Assert.True(
                    rule.ByDay.Count == 0
                    || rule.ByDay.Any(d => d.Matches(next.Value)));
            });
    }

    [Fact]
    public void MonthlyAndYearly_ClampToMonthEndAndNeverSkipAPeriod()
    {
        Gen.Select(Gen.Bool, Gen.Int[1, 3], GenDate)
            .Sample(t =>
            {
                var (yearly, interval, anchor) = t;
                var frequency = yearly
                    ? Frequency.Yearly
                    : Frequency.Monthly;
                var rule = RecurrenceRule.Parse(
                    Simple(frequency, interval));

                var current = anchor;
                for (var n = 0; n < 30; n++)
                {
                    var next = rule.NextAfter(anchor, current)!.Value;
                    var daysInMonth = DateTime.DaysInMonth(
                        next.Year,
                        next.Month);
                    Assert.Equal(
                        Math.Min(anchor.Day, daysInMonth),
                        next.Day);
                    var months =
                        ((next.Year - current.Year) * 12)
                        + next.Month - current.Month;
                    Assert.Equal(
                        yearly ? 12 * interval : interval,
                        months);
                    current = next;
                }
            });
    }

    [Fact]
    public void DailyReminderTime_SurvivesClockChanges()
    {
        var genTime = Gen.Int[0, (24 * 60) - 1]
            .Select(m => new TimeOnly(m / 60, m % 60));

        var genZoneId = Gen.Int[0, ZoneIds.Length - 1]
            .Select(i => ZoneIds[i]);

        Gen.Select(genZoneId, GenDate, genTime)
            .Sample(t =>
            {
                var (zoneId, start, time) = t;
                var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                var rule = RecurrenceRule.Parse("FREQ=DAILY");

                var date = start;
                var previous = LocalTimeResolver.ToInstant(
                    date,
                    time,
                    zone);
                for (var n = 0; n < 400; n++)
                {
                    date = rule.NextAfter(start, date)!.Value;
                    var instant = LocalTimeResolver.ToInstant(
                        date,
                        time,
                        zone);
                    var local = date.ToDateTime(time);

                    // Always on the right local day, a real local
                    // time, and later than the day before.
                    Assert.Equal(date, DateOnly.FromDateTime(
                        instant.DateTime));
                    Assert.Equal(
                        zone.GetUtcOffset(instant.UtcDateTime),
                        instant.Offset);
                    Assert.True(instant > previous);
                    var gap = instant - previous;
                    Assert.InRange(gap.TotalHours, 22.9, 25.1);

                    // The wall-clock time is kept unless the clocks
                    // skipped it, in which case it moves later.
                    if (zone.IsInvalidTime(local))
                    {
                        Assert.True(instant.DateTime > local);
                    }
                    else
                    {
                        Assert.Equal(local, instant.DateTime);
                    }

                    previous = instant;
                }
            });
    }
}
