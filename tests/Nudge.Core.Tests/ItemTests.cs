namespace Nudge.Core.Tests;

public class ItemTests
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateOnly Day(
        int year,
        int month,
        int day)
    {
        return new DateOnly(year, month, day);
    }

    /// <summary>An instant in November 2026, New York time.</summary>
    private static DateTimeOffset November(
        int day,
        int hour)
    {
        return new DateTimeOffset(
            2026,
            11,
            day,
            hour,
            0,
            0,
            TimeSpan.FromHours(-5));
    }

    private static Item Repeating(
        string rule,
        RecurrenceMode mode,
        DateOnly start)
    {
        var item = new Item("Water the plants");
        item.Repeat(RecurrenceRule.Parse(rule), mode, start);
        return item;
    }

    // Named regression test (MILESTONES.md M1). Do not weaken.
    [Fact]
    public void CompleteOnce_AdvancesOnce()
    {
        var daily = Repeating(
            "FREQ=DAILY",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 12));
        var weekly = Repeating(
            "FREQ=WEEKLY;BYDAY=FR",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 13));
        var sixMonthly = Repeating(
            "FREQ=MONTHLY;INTERVAL=6",
            RecurrenceMode.AfterCompletion,
            Day(2026, 11, 1));

        daily.Complete(November(12, 8), NewYork);
        weekly.Complete(November(13, 8), NewYork);
        sixMonthly.Complete(November(12, 8), NewYork);

        Assert.Equal(Day(2026, 11, 13), daily.DueDate);
        Assert.Equal(Day(2026, 11, 20), weekly.DueDate);
        Assert.Equal(Day(2027, 5, 12), sixMonthly.DueDate);
        foreach (var item in new[] { daily, weekly, sixMonthly })
        {
            Assert.False(item.IsCompleted);
            Assert.Equal(item.DueDate, item.OccurrenceDate);
            Assert.NotNull(item.Recurrence);
        }
    }

    [Fact]
    public void CompleteTwice_AdvancesTwice()
    {
        var weekly = Repeating(
            "FREQ=WEEKLY;BYDAY=FR",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 13));

        weekly.Complete(November(13, 8), NewYork);
        weekly.Complete(November(13, 9), NewYork);

        Assert.Equal(Day(2026, 11, 27), weekly.DueDate);
    }

    // Named regression test (MILESTONES.md M1). Do not weaken.
    [Fact]
    public void SettingDueDate_KeepsRepeatRule()
    {
        foreach (var mode in Enum.GetValues<RecurrenceMode>())
        {
            var item = Repeating(
                "FREQ=WEEKLY;BYDAY=FR",
                mode,
                Day(2026, 11, 13));
            var reminder = Reminder.FromDue(TimeSpan.FromMinutes(-30));
            item.AddReminder(reminder);
            var recurrence = item.Recurrence;

            item.SetDue(Day(2026, 11, 11), new TimeOnly(9, 0));
            item.SetDue(Day(2026, 11, 16));

            Assert.Equal(Day(2026, 11, 16), item.DueDate);
            Assert.NotNull(item.Recurrence);
            Assert.Same(recurrence, item.Recurrence);
            Assert.Equal(
                "FREQ=WEEKLY;BYDAY=FR",
                item.Recurrence.Rule.ToString());
            Assert.Equal(mode, item.Recurrence.Mode);
            Assert.Equal(Day(2026, 11, 13), item.OccurrenceDate);
            Assert.Same(reminder, Assert.Single(item.Reminders));
        }
    }

    [Fact]
    public void ClearDue_OnRepeatingItem_IsRefused()
    {
        var item = Repeating(
            "FREQ=DAILY",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 12));

        Assert.Throws<InvalidOperationException>(item.ClearDue);

        Assert.NotNull(item.Recurrence);
        Assert.Equal(Day(2026, 11, 12), item.DueDate);
    }

    [Fact]
    public void MovedEarlierAndCompleted_DoesNotComeRoundAgain()
    {
        var item = Repeating(
            "FREQ=WEEKLY;BYDAY=FR",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 13));

        // Friday's occurrence is moved to Wednesday and done then.
        item.SetDue(Day(2026, 11, 11), new TimeOnly(9, 0));
        item.Complete(November(11, 10), NewYork);

        Assert.Equal(Day(2026, 11, 20), item.DueDate);
        Assert.Equal(Day(2026, 11, 20), item.OccurrenceDate);
        Assert.Equal(new TimeOnly(9, 0), item.DueTime);
    }

    [Fact]
    public void Complete_UsesTheOwnersLocalDay()
    {
        var item = Repeating(
            "FREQ=DAILY",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 12));

        // 03:00 UTC on the 13th is still the evening of the 12th.
        item.Complete(
            new DateTimeOffset(2026, 11, 13, 3, 0, 0, TimeSpan.Zero),
            NewYork);

        Assert.Equal(Day(2026, 11, 13), item.DueDate);
    }

    [Fact]
    public void Repeat_FixedSchedule_StartsOnFirstDayTheRuleAllows()
    {
        // The 11th is a Wednesday.
        var item = Repeating(
            "FREQ=WEEKLY;BYDAY=FR",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 11));

        Assert.Equal(Day(2026, 11, 13), item.DueDate);
        Assert.Equal(Day(2026, 11, 13), item.OccurrenceDate);
    }

    [Fact]
    public void Repeat_RuleAlreadyEnded_IsRefused()
    {
        var item = new Item("Renew the passport");

        Assert.Throws<ArgumentException>(
            () => item.Repeat(
                RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261101"),
                RecurrenceMode.FixedSchedule,
                Day(2026, 11, 12)));

        Assert.Null(item.Recurrence);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public void Complete_LastOccurrence_FinishesTheItem()
    {
        var item = Repeating(
            "FREQ=DAILY;UNTIL=20261113",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 12));

        item.Complete(November(12, 8), NewYork);
        Assert.False(item.IsCompleted);

        item.Complete(November(13, 8), NewYork);
        Assert.Equal(November(13, 8), item.CompletedAt);
        Assert.Equal(Day(2026, 11, 13), item.DueDate);
    }

    [Fact]
    public void Complete_OneOffItem_FinishesIt()
    {
        var item = new Item("Buy a ladder");

        item.Complete(November(12, 8), NewYork);

        Assert.True(item.IsCompleted);
        Assert.Throws<InvalidOperationException>(
            () => item.Complete(November(12, 9), NewYork));
        Assert.Equal(November(12, 8), item.CompletedAt);
    }

    [Fact]
    public void StopRepeating_KeepsTheDueDate()
    {
        var item = Repeating(
            "FREQ=DAILY",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 12));

        item.StopRepeating();
        item.Complete(November(12, 8), NewYork);

        Assert.Null(item.Recurrence);
        Assert.Null(item.OccurrenceDate);
        Assert.Equal(Day(2026, 11, 12), item.DueDate);
        Assert.True(item.IsCompleted);
    }

    [Fact]
    public void Complete_MovesDeferDateWithTheOccurrence()
    {
        var item = Repeating(
            "FREQ=WEEKLY;BYDAY=FR",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 13));
        item.DeferUntil = Day(2026, 11, 11);

        item.Complete(November(13, 8), NewYork);

        Assert.Equal(Day(2026, 11, 18), item.DeferUntil);
    }

    [Fact]
    public void Complete_DropsDeferDateLaterThanTheOccurrence()
    {
        var item = Repeating(
            "FREQ=DAILY",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 12));
        item.DeferUntil = Day(2026, 11, 14);

        item.Complete(November(12, 8), NewYork);

        Assert.Null(item.DeferUntil);
    }

    [Fact]
    public void NewItem_StartsInTheInbox()
    {
        var item = new Item("  Call the plumber ");

        Assert.Equal("Call the plumber", item.Title);
        Assert.Equal(ItemState.Inbox, item.State);
        Assert.Equal(Priority.None, item.Priority);
        Assert.Null(item.Project);
        Assert.Null(item.DueDate);
        Assert.NotEqual(Guid.Empty, item.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitle_IsRefused(string title)
    {
        Assert.Throws<ArgumentException>(() => new Item(title));

        var item = new Item("Call the plumber");
        Assert.Throws<ArgumentException>(() => item.Title = title);
    }

    [Fact]
    public void Contexts_AreASet()
    {
        var item = new Item("Call the plumber");
        var phone = new Context("phone");

        item.AddContext(phone);
        item.AddContext(phone);
        Assert.Same(phone, Assert.Single(item.Contexts));

        item.RemoveContext(phone);
        Assert.Empty(item.Contexts);
    }
}
