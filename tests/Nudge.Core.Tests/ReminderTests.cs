namespace Nudge.Core.Tests;

public class ReminderTests
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTimeOffset Local(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int utcOffsetHours)
    {
        return new DateTimeOffset(
            year,
            month,
            day,
            hour,
            minute,
            0,
            TimeSpan.FromHours(utcOffsetHours));
    }

    [Fact]
    public void Relative_CountsFromDueDateAndTime()
    {
        var item = new Item("Leave for the dentist");
        item.SetDue(new DateOnly(2026, 11, 12), new TimeOnly(14, 0));
        var reminder = Reminder.FromDue(TimeSpan.FromMinutes(-30));

        Assert.Equal(
            Local(2026, 11, 12, 13, 30, -5),
            reminder.FireTime(item, NewYork));
    }

    [Fact]
    public void Relative_WithoutDueTime_CountsFromMidnight()
    {
        var item = new Item("Put the bins out");
        item.SetDue(new DateOnly(2026, 11, 12));

        Assert.Equal(
            Local(2026, 11, 12, 9, 0, -5),
            Reminder.FromDue(TimeSpan.FromHours(9))
                .FireTime(item, NewYork));
        Assert.Equal(
            Local(2026, 11, 11, 18, 0, -5),
            Reminder.FromDue(TimeSpan.FromHours(-6))
                .FireTime(item, NewYork));
    }

    [Fact]
    public void Relative_KeepsWallClockWhenTheClocksChange()
    {
        // Clocks go forward on 14 March 2027, so 09:00 is only eight
        // hours after midnight.
        var item = new Item("Put the bins out");
        item.SetDue(new DateOnly(2027, 3, 14));

        Assert.Equal(
            Local(2027, 3, 14, 9, 0, -4),
            Reminder.FromDue(TimeSpan.FromHours(9))
                .FireTime(item, NewYork));
    }

    [Fact]
    public void Relative_FollowsTheItemWhenItIsRescheduledOrRepeats()
    {
        var item = new Item("Put the bins out");
        item.SetDue(new DateOnly(2026, 11, 12), new TimeOnly(7, 0));
        item.Repeat(
            RecurrenceRule.Parse("FREQ=WEEKLY"),
            RecurrenceMode.FixedSchedule,
            new DateOnly(2026, 11, 12));
        var reminder = Reminder.FromDue(TimeSpan.FromHours(-1));
        item.AddReminder(reminder);

        item.SetDue(new DateOnly(2026, 11, 13), new TimeOnly(7, 0));
        Assert.Equal(
            Local(2026, 11, 13, 6, 0, -5),
            reminder.FireTime(item, NewYork));

        item.Complete(Local(2026, 11, 13, 7, 5, -5), NewYork);
        Assert.Equal(
            Local(2026, 11, 19, 6, 0, -5),
            reminder.FireTime(item, NewYork));
        Assert.Equal(TimeSpan.FromHours(-1), reminder.Offset);
    }

    [Fact]
    public void Relative_WithoutDueDate_DoesNotFire()
    {
        var item = new Item("Sort the garage");

        Assert.Null(
            Reminder.FromDue(TimeSpan.Zero).FireTime(item, NewYork));
    }

    [Fact]
    public void Absolute_FiresAtItsInstantWhateverTheDueDate()
    {
        var at = Local(2026, 11, 12, 16, 45, -5);
        var reminder = Reminder.AtInstant(at);
        var item = new Item("Sort the garage");

        Assert.Equal(at, reminder.FireTime(item, NewYork));

        item.SetDue(new DateOnly(2026, 12, 1));
        Assert.Equal(at, reminder.FireTime(item, NewYork));
    }

    [Fact]
    public void CompletedItem_DoesNotFire()
    {
        var at = Local(2026, 11, 12, 16, 45, -5);
        var item = new Item("Sort the garage");
        item.Complete(Local(2026, 11, 12, 9, 0, -5), NewYork);

        Assert.Null(Reminder.AtInstant(at).FireTime(item, NewYork));
    }

    [Fact]
    public void Relative_OffsetOverAYear_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Reminder.FromDue(TimeSpan.FromDays(-367)));
    }

    [Fact]
    public void AddReminder_Twice_KeepsOne()
    {
        var item = new Item("Sort the garage");
        var reminder = Reminder.FromDue(TimeSpan.Zero);

        item.AddReminder(reminder);
        item.AddReminder(reminder);
        Assert.Single(item.Reminders);

        item.RemoveReminder(reminder);
        Assert.Empty(item.Reminders);
    }
}
