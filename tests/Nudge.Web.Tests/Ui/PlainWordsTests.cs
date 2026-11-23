using System.Globalization;
using Nudge.Core;
using Nudge.Web.Components;

namespace Nudge.Web.Tests.Ui;

/// <summary>Dates, repeat rules and reminders in words, and the
/// presets' mapping to what is written.</summary>
public sealed class PlainWordsTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Theory]
    [InlineData("2026-11-13", "Today")]
    [InlineData("2026-11-14", "Tomorrow")]
    [InlineData("2026-11-12", "Yesterday")]
    [InlineData("2026-11-27", "Fri 27 Nov")]
    [InlineData("2026-01-02", "Fri 2 Jan")]
    [InlineData("2027-01-01", "Fri 1 Jan 2027")]
    [InlineData("2025-12-31", "Wed 31 Dec 2025")]
    public void Day_IsInWords(
        string date,
        string expected)
    {
        Assert.Equal(expected, Plain.Day(DateOnly.Parse(date), Today));
    }

    [Fact]
    public void Day_NextToANewYear_IsStillTomorrowAndYesterday()
    {
        var eve = new DateOnly(2026, 12, 31);
        var day = new DateOnly(2027, 1, 1);

        Assert.Equal("Tomorrow", Plain.Day(day, eve));
        Assert.Equal("Yesterday", Plain.Day(eve, day));
    }

    [Fact]
    public void Day_IsEnglishWhateverTheCulture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Assert.Equal(
                "Fri 27 Nov",
                Plain.Day(new DateOnly(2026, 11, 27), Today));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void DayTime_AddsTheTimeWhenThereIsOne()
    {
        Assert.Equal(
            "Today 14:30",
            Plain.DayTime(Today, new TimeOnly(14, 30), Today));
        Assert.Equal("Today", Plain.DayTime(Today, null, Today));
    }

    // 04:30 UTC on the 14th is 23:30 on the 13th in New York.
    [Fact]
    public void Instant_IsOnTheOwnersDay()
    {
        var instant = new DateTimeOffset(2026, 11, 14, 4, 30, 0, TimeSpan.Zero);

        Assert.Equal("Today 23:30", Plain.Instant(instant, NewYork, Today));
        Assert.Equal(
            "Yesterday 23:30",
            Plain.Instant(instant, NewYork, Today.AddDays(1)));
    }

    [Fact]
    public void Instant_AcrossTheNewYearInTheOwnersZone_KeepsTheOldYear()
    {
        var instant = new DateTimeOffset(2027, 1, 1, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            "Thu 31 Dec 22:00",
            Plain.Instant(instant, NewYork, new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Words_ForFormValues_StayIso()
    {
        var date = new DateOnly(2026, 11, 27);

        Assert.Equal("2026-11-27", Words.Date(date));
        Assert.Equal("09:05", Words.Time(new TimeOnly(9, 5)));
        Assert.Equal("09:05:07", Words.TimeValue(new TimeOnly(9, 5, 7)));
    }

    [Theory]
    [InlineData("FREQ=DAILY", "Every day")]
    [InlineData("FREQ=DAILY;INTERVAL=3", "Every 3 days")]
    [InlineData("FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR", "Every weekday")]
    [InlineData("FREQ=WEEKLY", "Every week")]
    [InlineData("FREQ=WEEKLY;BYDAY=FR", "Every week on Friday")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=FR", "Every 2 weeks on Friday")]
    [InlineData("FREQ=WEEKLY;BYDAY=FR,MO", "Every week on Monday and Friday")]
    [InlineData(
        "FREQ=WEEKLY;BYDAY=SU,WE,MO",
        "Every week on Monday, Wednesday and Sunday")]
    [InlineData("FREQ=MONTHLY", "Every month")]
    [InlineData("FREQ=MONTHLY;INTERVAL=6", "Every 6 months")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15", "Every month on the 15th")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=22", "Every month on the 22nd")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1", "Every month on the last day")]
    [InlineData(
        "FREQ=MONTHLY;BYMONTHDAY=31",
        "Every month on the 31st (or the last day of a shorter month)")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO", "Every month on the first Monday")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", "Every month on the last Friday")]
    [InlineData("FREQ=YEARLY", "Every year")]
    [InlineData(
        "FREQ=YEARLY;BYMONTH=12;BYMONTHDAY=25",
        "Every year on 25 December")]
    public void RuleWords_SayWhatTheRuleDoes(
        string rule,
        string expected)
    {
        Assert.Equal(expected, Plain.RuleWords(RecurrenceRule.Parse(rule)));
    }

    // Each of these would be misdescribed by the common phrases.
    [Theory]
    [InlineData("FREQ=DAILY;BYDAY=MO,WE")]
    [InlineData("FREQ=DAILY;INTERVAL=2;BYDAY=MO,TU,WE,TH,FR")]
    [InlineData("FREQ=DAILY;BYMONTH=6")]
    [InlineData("FREQ=DAILY;BYMONTHDAY=1")]
    [InlineData("FREQ=WEEKLY;BYMONTH=6;BYDAY=FR")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=SU;WKST=SU")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,15")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-2")]
    [InlineData("FREQ=MONTHLY;BYMONTH=3;BYMONTHDAY=1")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=5FR")]
    [InlineData("FREQ=MONTHLY;BYDAY=-2FR")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO,3MO")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=13;BYDAY=FR")]
    [InlineData("FREQ=YEARLY;BYMONTH=3")]
    [InlineData("FREQ=YEARLY;BYMONTH=3,9;BYMONTHDAY=1")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29")]
    [InlineData("FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=31")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1")]
    [InlineData("FREQ=YEARLY;BYMONTH=11;BYDAY=4TH")]
    public void Repeat_ThatCannotBeSaidExactly_ShowsTheRawRule(string text)
    {
        var rule = RecurrenceRule.Parse(text);

        Assert.Null(Plain.RuleWords(rule));
        Assert.Equal(
            $"{rule} (on a fixed schedule)",
            Plain.Repeat(rule, RecurrenceMode.FixedSchedule));
        Assert.Equal(
            $"{rule} (after completion)",
            Plain.Repeat(rule, RecurrenceMode.AfterCompletion));
    }

    [Theory]
    [InlineData(
        "FREQ=WEEKLY;BYDAY=FR",
        RecurrenceMode.FixedSchedule,
        "Every week on Friday, on a fixed schedule")]
    [InlineData(
        "FREQ=WEEKLY",
        RecurrenceMode.AfterCompletion,
        "Every week, counted from completion")]
    [InlineData(
        "FREQ=WEEKLY;BYDAY=FR",
        RecurrenceMode.AfterCompletion,
        "Every week on Friday, at least a week after completion")]
    [InlineData(
        "FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=1",
        RecurrenceMode.AfterCompletion,
        "Every 3 months on the 1st, at least 3 months after completion")]
    [InlineData(
        "FREQ=DAILY;UNTIL=20271231",
        RecurrenceMode.FixedSchedule,
        "Every day through 31 Dec 2027, on a fixed schedule")]
    public void Repeat_SaysTheMode(
        string rule,
        RecurrenceMode mode,
        string expected)
    {
        Assert.Equal(expected, Plain.Repeat(RecurrenceRule.Parse(rule), mode));
    }

    // Completed on a Wednesday, "Friday at least a week later" is the
    // Friday of the week after, as the words say.
    [Fact]
    public void AfterCompletionWords_MatchWhatTheRuleDoes()
    {
        var rule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=FR");

        Assert.Equal(
            new DateOnly(2026, 11, 20),
            rule.NextCountedFrom(new DateOnly(2026, 11, 11)));
    }

    // Friday 13 November 2026.
    [Theory]
    [InlineData("daily", "FREQ=DAILY", "Every day")]
    [InlineData("weekdays", "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR", "Every weekday")]
    [InlineData("weekly-FR", "FREQ=WEEKLY;BYDAY=FR", "Every week on Friday")]
    [InlineData(
        "2-weeks-FR",
        "FREQ=WEEKLY;INTERVAL=2;BYDAY=FR",
        "Every 2 weeks on Friday")]
    [InlineData("monthly", "FREQ=MONTHLY", "Every month")]
    [InlineData("3-months", "FREQ=MONTHLY;INTERVAL=3", "Every 3 months")]
    [InlineData("6-months", "FREQ=MONTHLY;INTERVAL=6", "Every 6 months")]
    [InlineData("yearly", "FREQ=YEARLY", "Every year")]
    public void RepeatPreset_IsAValidCanonicalRule_UnderItsLabel(
        string key,
        string rule,
        string label)
    {
        Assert.Equal(rule, RepeatPresets.Rule(key));
        Assert.Equal(rule, RecurrenceRule.Parse(rule).ToString());
        Assert.Contains(
            new Choice(key, label),
            RepeatPresets.For(Today));
        Assert.Equal(
            key,
            RepeatPresets.Selected(RecurrenceRule.Parse(rule), Today));
    }

    [Theory]
    [InlineData("FREQ=WEEKLY;BYDAY=MO")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1")]
    [InlineData("FREQ=DAILY;UNTIL=20271231")]
    [InlineData("FREQ=WEEKLY;BYDAY=FR;WKST=SU")]
    public void Selected_ForARuleThatIsNoPreset_IsCustom(string rule)
    {
        Assert.Equal(
            RepeatPresets.Custom,
            RepeatPresets.Selected(RecurrenceRule.Parse(rule), Today));
    }

    [Fact]
    public void Selected_WithNoRule_IsNothing()
    {
        Assert.Equal("", RepeatPresets.Selected(null, Today));
    }

    [Fact]
    public void RepeatResolve_UsesTheTypedRuleOnlyUnderCustom()
    {
        Assert.Equal(
            "FREQ=DAILY",
            RepeatPresets.Resolve("daily", "FREQ=YEARLY", "FREQ=YEARLY").Value);
        Assert.Equal(
            "FREQ=YEARLY",
            RepeatPresets.Resolve("custom", " FREQ=YEARLY ", "").Value);
    }

    [Theory]
    [InlineData("daily", "FREQ=YEARLY", "")]
    [InlineData("custom", "  ", "")]
    [InlineData("", "FREQ=DAILY", "FREQ=DAILY")]
    [InlineData(null, "FREQ=DAILY", "FREQ=DAILY")]
    [InlineData("weekly-XX", "", "")]
    public void RepeatResolve_Refuses(
        string? preset,
        string typed,
        string shown)
    {
        var chosen = RepeatPresets.Resolve(preset, typed, shown);

        Assert.Null(chosen.Value);
        Assert.NotNull(chosen.Error);
    }

    [Theory]
    [InlineData("day-0900", null, 540)]
    [InlineData("day-before-1800", null, -360)]
    [InlineData("day-before-0900", null, -900)]
    [InlineData("week-before-0900", null, -9540)]
    [InlineData("day-0900", "14:00", -300)]
    [InlineData("day-0900", "08:00", 60)]
    [InlineData("day-before-1800", "14:00", -1200)]
    [InlineData("day-before-0900", "14:00", -1740)]
    [InlineData("at-due", "14:00", 0)]
    [InlineData("15-before", "14:00", -15)]
    [InlineData("60-before", "14:00", -60)]
    [InlineData("1-day-before", "14:00", -1440)]
    public void ReminderPreset_MapsToTheOffset(
        string key,
        string? dueTime,
        int expected)
    {
        var due = dueTime is null ? (TimeOnly?)null : TimeOnly.Parse(dueTime);

        Assert.Equal(expected, ReminderPresets.Offset(key, due));
    }

    // What matters is when it fires: the core works that out from the
    // offset, the same way for both kinds of item.
    [Theory]
    [InlineData("day-0900", null, 20, 9, 0)]
    [InlineData("day-before-1800", null, 19, 18, 0)]
    [InlineData("day-0900", "14:00", 20, 9, 0)]
    [InlineData("day-before-1800", "14:00", 19, 18, 0)]
    [InlineData("day-0900", "07:45", 20, 9, 0)]
    public void ReminderPreset_FiresAtTheTimeItsLabelSays(
        string key,
        string? dueTime,
        int day,
        int hour,
        int minute)
    {
        var due = dueTime is null ? (TimeOnly?)null : TimeOnly.Parse(dueTime);
        var item = new Item("Invented item");
        item.SetDue(new DateOnly(2026, 11, 20), due);
        var offset = ReminderPresets.Offset(key, due)!.Value;

        var fires = Reminder.FromDue(TimeSpan.FromMinutes(offset))
            .FireTime(item, NewYork)!.Value;

        var local = TimeZoneInfo.ConvertTime(fires, NewYork);
        Assert.Equal(
            (2026, 11, day, hour, minute),
            (local.Year, local.Month, local.Day, local.Hour, local.Minute));
    }

    [Fact]
    public void ReminderPresets_FitTheDueTime()
    {
        var untimed = ReminderPresets.For(null).Select(c => c.Key).ToList();
        var timed = ReminderPresets.For(new TimeOnly(14, 0))
            .Select(c => c.Key)
            .ToList();
        var seconds = ReminderPresets.For(new TimeOnly(14, 0, 30))
            .Select(c => c.Key)
            .ToList();

        Assert.DoesNotContain("at-due", untimed);
        Assert.Contains("day-0900", untimed);
        Assert.Contains("at-due", timed);
        Assert.Contains("day-0900", timed);
        Assert.Contains("at-due", seconds);
        Assert.DoesNotContain("day-0900", seconds);
    }

    [Fact]
    public void ReminderOffset_OfAPresetThatDoesNotFit_IsNone()
    {
        Assert.Null(ReminderPresets.Offset("15-before", null));
        Assert.Null(
            ReminderPresets.Offset("day-0900", new TimeOnly(14, 0, 30)));
        Assert.Null(ReminderPresets.Offset("no-such", null));
    }

    [Theory]
    [InlineData("custom", "-30", null, -30)]
    [InlineData("day-0900", "", "14:00", -300)]
    [InlineData("day-0900", null, null, 540)]
    public void ReminderResolve_GivesTheOffset(
        string preset,
        string? typed,
        string? dueTime,
        int expected)
    {
        var due = dueTime is null ? (TimeOnly?)null : TimeOnly.Parse(dueTime);

        Assert.Equal(
            expected,
            ReminderPresets.Resolve(preset, typed, due).Value);
    }

    [Theory]
    [InlineData("custom", "")]
    [InlineData("custom", "soon")]
    [InlineData("custom", "1.5")]
    [InlineData("day-0900", "120")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ReminderResolve_Refuses(
        string? preset,
        string typed)
    {
        var chosen = ReminderPresets.Resolve(preset, typed, null);

        Assert.Null(chosen.Value);
        Assert.NotNull(chosen.Error);
    }

    [Theory]
    [InlineData(540, "09:00 on the due day")]
    [InlineData(0, "00:00 on the due day")]
    [InlineData(-360, "18:00 the day before")]
    [InlineData(-1, "23:59 the day before")]
    [InlineData(-1440, "00:00 the day before")]
    [InlineData(-1441, "23:59 2 days before")]
    [InlineData(-9540, "09:00 7 days before")]
    [InlineData(1440, "00:00 the day after")]
    [InlineData(3000, "02:00 2 days after")]
    public void ReminderOffset_WithNoDueTime_IsByTheClock(
        int minutes,
        string expected)
    {
        Assert.Equal(
            expected,
            Plain.ReminderOffset(TimeSpan.FromMinutes(minutes), null));
    }

    [Theory]
    [InlineData(0, "At the due time")]
    [InlineData(-30, "30 minutes before the due time")]
    [InlineData(-60, "1 hour before the due time")]
    [InlineData(-90, "1 hour 30 minutes before the due time")]
    [InlineData(-1440, "1 day before the due time")]
    [InlineData(-2945, "2 days 1 hour 5 minutes before the due time")]
    [InlineData(1, "1 minute after the due time")]
    public void ReminderOffset_WithADueTime_IsFromIt(
        int minutes,
        string expected)
    {
        Assert.Equal(
            expected,
            Plain.ReminderOffset(
                TimeSpan.FromMinutes(minutes),
                new TimeOnly(14, 0)));
    }
}
