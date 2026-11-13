namespace Nudge.Core.Tests;

public class RecurrenceTests
{
    private static Recurrence Fixed(
        string rule,
        DateOnly anchor)
    {
        return new Recurrence(
            RecurrenceRule.Parse(rule),
            RecurrenceMode.FixedSchedule,
            anchor);
    }

    private static Recurrence AfterCompletion(string rule)
    {
        return new Recurrence(
            RecurrenceRule.Parse(rule),
            RecurrenceMode.AfterCompletion,
            new DateOnly(2026, 1, 1));
    }

    private static DateOnly Day(
        int year,
        int month,
        int day)
    {
        return new DateOnly(year, month, day);
    }

    // Named regression test (MILESTONES.md M1). Do not weaken.
    [Fact]
    public void CompleteOnce_AdvancesOnce()
    {
        var daily = Fixed("FREQ=DAILY", Day(2026, 11, 1));
        var weekly = Fixed("FREQ=WEEKLY;BYDAY=FR", Day(2026, 11, 6));
        var sixMonthly = AfterCompletion("FREQ=MONTHLY;INTERVAL=6");

        Assert.Equal(
            Day(2026, 11, 13),
            daily.NextAfterCompletion(Day(2026, 11, 12), Day(2026, 11, 12)));
        Assert.Equal(
            Day(2026, 11, 20),
            weekly.NextAfterCompletion(Day(2026, 11, 13), Day(2026, 11, 13)));
        Assert.Equal(
            Day(2027, 5, 12),
            sixMonthly.NextAfterCompletion(
                Day(2026, 11, 1),
                Day(2026, 11, 12)));
    }

    [Fact]
    public void FixedSchedule_StaysOnFridaysWhenCompletedLate()
    {
        var biweekly = Fixed(
            "FREQ=WEEKLY;INTERVAL=2;BYDAY=FR",
            Day(2026, 11, 6));

        // Due Friday the 6th, completed the following Wednesday.
        var next = biweekly.NextAfterCompletion(
            Day(2026, 11, 6),
            Day(2026, 11, 11));

        Assert.Equal(Day(2026, 11, 20), next);
    }

    [Fact]
    public void FixedSchedule_CompletedEarly_DoesNotRepeatOccurrence()
    {
        var weekly = Fixed("FREQ=WEEKLY;BYDAY=FR", Day(2026, 11, 6));

        // Friday the 20th was moved to and completed on Wednesday.
        var next = weekly.NextAfterCompletion(
            Day(2026, 11, 20),
            Day(2026, 11, 18));

        Assert.Equal(Day(2026, 11, 27), next);
    }

    [Fact]
    public void FixedSchedule_LongOverdue_SkipsMissedOccurrences()
    {
        var daily = Fixed("FREQ=DAILY", Day(2026, 11, 1));

        var next = daily.NextAfterCompletion(
            Day(2026, 11, 2),
            Day(2026, 11, 12));

        Assert.Equal(Day(2026, 11, 13), next);
    }

    [Fact]
    public void AfterCompletion_CountsFromCompletionDay()
    {
        var sixMonthly = AfterCompletion("FREQ=MONTHLY;INTERVAL=6");

        // Due in March, actually done in mid May.
        var next = sixMonthly.NextAfterCompletion(
            Day(2026, 3, 1),
            Day(2026, 5, 17));

        Assert.Equal(Day(2026, 11, 17), next);
    }

    [Fact]
    public void AfterCompletion_WithWeekday_WaitsFullIntervalFirst()
    {
        var rule = AfterCompletion("FREQ=WEEKLY;INTERVAL=2;BYDAY=SA");

        // Completed Thursday the 12th; two weeks later is Thursday
        // the 26th, and the first Saturday from there is the 28th.
        var next = rule.NextAfterCompletion(
            Day(2026, 11, 7),
            Day(2026, 11, 12));

        Assert.Equal(Day(2026, 11, 28), next);
    }

    [Fact]
    public void MonthlyOnThe31st_FallsOnLastDayOfShortMonths()
    {
        var rule = RecurrenceRule.Parse("FREQ=MONTHLY");
        var anchor = Day(2027, 1, 31);

        var feb = rule.NextAfter(anchor, anchor);
        var mar = rule.NextAfter(anchor, feb!.Value);
        var apr = rule.NextAfter(anchor, mar!.Value);

        Assert.Equal(Day(2027, 2, 28), feb);
        Assert.Equal(Day(2027, 3, 31), mar);
        Assert.Equal(Day(2027, 4, 30), apr);
    }

    [Fact]
    public void YearlyOnLeapDay_FallsOnFeb28InOrdinaryYears()
    {
        var rule = RecurrenceRule.Parse("FREQ=YEARLY");
        var anchor = Day(2028, 2, 29);

        Assert.Equal(Day(2029, 2, 28), rule.NextAfter(anchor, anchor));
        Assert.Equal(
            Day(2032, 2, 29),
            rule.NextAfter(anchor, Day(2031, 12, 31)));
    }

    [Fact]
    public void Monthly_LastFridayAndSecondTuesday()
    {
        var anchor = Day(2026, 11, 1);
        var lastFriday = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR");
        var secondTuesday = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=2TU");

        Assert.Equal(
            Day(2026, 11, 27),
            lastFriday.NextOnOrAfter(anchor, anchor));
        Assert.Equal(
            Day(2026, 12, 25),
            lastFriday.NextAfter(anchor, Day(2026, 11, 27)));
        Assert.Equal(
            Day(2026, 11, 10),
            secondTuesday.NextOnOrAfter(anchor, anchor));
    }

    [Fact]
    public void Monthly_LastDayOfMonth()
    {
        var rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-1");
        var anchor = Day(2028, 1, 15);

        Assert.Equal(Day(2028, 1, 31), rule.NextAfter(anchor, anchor));
        Assert.Equal(
            Day(2028, 2, 29),
            rule.NextAfter(anchor, Day(2028, 1, 31)));
    }

    [Fact]
    public void Weekly_IntervalCountsWeeksFromWeekStart()
    {
        // Anchor is a Wednesday; with weeks starting on Monday the
        // Monday of an "on" week is two days before the anchor's
        // week ends, so the next Monday is 12 days later.
        var anchor = Day(2026, 11, 4);
        var mondayStart = RecurrenceRule.Parse(
            "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE");
        var sundayStart = RecurrenceRule.Parse(
            "FREQ=WEEKLY;INTERVAL=2;BYDAY=SU,WE;WKST=SU");

        Assert.Equal(
            Day(2026, 11, 16),
            mondayStart.NextAfter(anchor, anchor));
        Assert.Equal(
            Day(2026, 11, 15),
            sundayStart.NextAfter(anchor, anchor));
    }

    [Fact]
    public void Until_IsInclusiveAndEndsTheSeries()
    {
        var daily = Fixed("FREQ=DAILY;UNTIL=20261114", Day(2026, 11, 1));

        Assert.Equal(
            Day(2026, 11, 14),
            daily.NextAfterCompletion(Day(2026, 11, 13), Day(2026, 11, 13)));
        Assert.Null(
            daily.NextAfterCompletion(Day(2026, 11, 14), Day(2026, 11, 14)));
    }

    [Fact]
    public void NextOnOrAfter_BeforeAnchor_StartsAtAnchor()
    {
        var rule = RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=3");
        var anchor = Day(2026, 11, 10);

        Assert.Equal(anchor, rule.NextOnOrAfter(anchor, Day(2026, 1, 1)));
    }
}
