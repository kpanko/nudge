using Microsoft.EntityFrameworkCore;
using Nudge.Core;

namespace Nudge.Web.Tests;

/// <summary>
/// Items saved to the database and read back through a different
/// context, so nothing is answered from memory.
/// </summary>
public sealed class ItemPersistenceTests : IDisposable
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private readonly TestDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

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

    private Guid Save(Item item)
    {
        using var db = _database.NewContext();
        db.Items.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    /// <summary>Reads the item in a context of its own, changes it
    /// and saves.</summary>
    private void Change(
        Guid id,
        Action<Item> change)
    {
        using var db = _database.NewContext();
        change(db.Items.Single(i => i.Id == id));
        db.SaveChanges();
    }

    private Item Load(Guid id)
    {
        using var db = _database.NewContext();
        return db.Items.Single(i => i.Id == id);
    }

    [Fact]
    public void NewItem_RoundTrips()
    {
        var item = new Item("Call the plumber");

        var loaded = Load(Save(item));

        Assert.NotSame(item, loaded);
        Assert.Equal(item.Id, loaded.Id);
        Assert.Equal("Call the plumber", loaded.Title);
        Assert.Equal("", loaded.Notes);
        Assert.Equal(ItemState.Inbox, loaded.State);
        Assert.Equal(Priority.None, loaded.Priority);
        Assert.Null(loaded.Project);
        Assert.Null(loaded.DeferUntil);
        Assert.False(loaded.DoNotAutoReschedule);
        Assert.Empty(loaded.Contexts);
        Assert.Empty(loaded.Reminders);
        Assert.Null(loaded.DueDate);
        Assert.Null(loaded.DueTime);
        Assert.Null(loaded.Recurrence);
        Assert.Null(loaded.OccurrenceDate);
        Assert.Null(loaded.CompletedAt);
    }

    [Fact]
    public void Item_WithEverythingSet_RoundTrips()
    {
        var house = new Project("House");
        var item = new Item("Order the worktop")
        {
            Notes = "Oak, three metres.",
            State = ItemState.WaitingFor,
            Priority = Priority.High,
            Project = house,
            DeferUntil = Day(2026, 11, 20),
            DoNotAutoReschedule = true,
        };
        item.AddContext(new Context("phone"));
        item.AddContext(new Context("errands"));
        item.SetDue(Day(2026, 11, 26), new TimeOnly(14, 30));
        item.Repeat(
            RecurrenceRule.Parse("FREQ=MONTHLY;INTERVAL=2;BYDAY=-1FR"),
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 26));
        var relative = Reminder.FromDue(TimeSpan.FromMinutes(-30));
        var absolute = Reminder.AtInstant(November(26, 9));
        item.AddReminder(relative);
        item.AddReminder(absolute);

        var loaded = Load(Save(item));

        Assert.Equal("Order the worktop", loaded.Title);
        Assert.Equal("Oak, three metres.", loaded.Notes);
        Assert.Equal(ItemState.WaitingFor, loaded.State);
        Assert.Equal(Priority.High, loaded.Priority);
        Assert.Equal(house.Id, loaded.Project?.Id);
        Assert.Equal("House", loaded.Project?.Name);
        Assert.Equal(Day(2026, 11, 20), loaded.DeferUntil);
        Assert.True(loaded.DoNotAutoReschedule);
        Assert.Equal(
            ["errands", "phone"],
            loaded.Contexts.Select(c => c.Name).Order());

        // The last Friday of November 2026 is the 27th.
        Assert.Equal(Day(2026, 11, 27), loaded.DueDate);
        Assert.Equal(new TimeOnly(14, 30), loaded.DueTime);
        Assert.Equal(Day(2026, 11, 27), loaded.OccurrenceDate);
        Assert.NotNull(loaded.Recurrence);
        Assert.Equal(
            "FREQ=MONTHLY;INTERVAL=2;BYDAY=-1FR",
            loaded.Recurrence.Rule.ToString());
        Assert.Equal(RecurrenceMode.FixedSchedule, loaded.Recurrence.Mode);
        Assert.Equal(Day(2026, 11, 26), loaded.Recurrence.Anchor);

        Assert.Equal(2, loaded.Reminders.Count);
        var loadedRelative = loaded.Reminders.Single(
            r => r.Id == relative.Id);
        Assert.Equal(TimeSpan.FromMinutes(-30), loadedRelative.Offset);
        Assert.Null(loadedRelative.At);
        var loadedAbsolute = loaded.Reminders.Single(
            r => r.Id == absolute.Id);
        Assert.Equal(November(26, 9), loadedAbsolute.At);
        Assert.Null(loadedAbsolute.Offset);

        // And it still behaves like the item that was saved.
        Assert.Equal(
            item.Reminders.Select(r => r.FireTime(item, NewYork)).Order(),
            loaded.Reminders.Select(r => r.FireTime(loaded, NewYork)).Order());
        Assert.Null(loaded.CompletedAt);
    }

    // Named regression test (MILESTONES.md M1). Do not weaken.
    [Fact]
    public void CompleteOnce_AdvancesOnce()
    {
        var daily = Save(
            Repeating(
                "FREQ=DAILY",
                RecurrenceMode.FixedSchedule,
                Day(2026, 11, 12)));
        var weekly = Save(
            Repeating(
                "FREQ=WEEKLY;BYDAY=FR",
                RecurrenceMode.FixedSchedule,
                Day(2026, 11, 13)));
        var sixMonthly = Save(
            Repeating(
                "FREQ=MONTHLY;INTERVAL=6",
                RecurrenceMode.AfterCompletion,
                Day(2026, 11, 1)));

        Change(daily, item => item.Complete(November(12, 8), NewYork));
        Change(weekly, item => item.Complete(November(13, 8), NewYork));
        Change(sixMonthly, item => item.Complete(November(12, 8), NewYork));

        Assert.Equal(Day(2026, 11, 13), Load(daily).DueDate);
        Assert.Equal(Day(2026, 11, 20), Load(weekly).DueDate);
        Assert.Equal(Day(2027, 5, 12), Load(sixMonthly).DueDate);
        foreach (var id in new[] { daily, weekly, sixMonthly })
        {
            var item = Load(id);
            Assert.False(item.IsCompleted);
            Assert.Equal(item.DueDate, item.OccurrenceDate);
            Assert.NotNull(item.Recurrence);
        }

        // Saving again without completing does not move it on.
        Change(weekly, item => item.Notes = "Not the cactus.");
        Assert.Equal(Day(2026, 11, 20), Load(weekly).DueDate);
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
            var id = Save(item);

            Change(
                id,
                loaded => loaded.SetDue(
                    Day(2026, 11, 11),
                    new TimeOnly(9, 0)));
            Change(id, loaded => loaded.SetDue(Day(2026, 11, 16)));

            var after = Load(id);
            Assert.Equal(Day(2026, 11, 16), after.DueDate);
            Assert.Null(after.DueTime);
            Assert.NotNull(after.Recurrence);
            Assert.Equal(
                "FREQ=WEEKLY;BYDAY=FR",
                after.Recurrence.Rule.ToString());
            Assert.Equal(mode, after.Recurrence.Mode);
            Assert.Equal(Day(2026, 11, 13), after.Recurrence.Anchor);
            Assert.Equal(Day(2026, 11, 13), after.OccurrenceDate);
            var afterReminder = Assert.Single(after.Reminders);
            Assert.Equal(reminder.Id, afterReminder.Id);
            Assert.Equal(TimeSpan.FromMinutes(-30), afterReminder.Offset);
        }
    }

    [Fact]
    public void SettingDueDate_WritesOnlyTheDueDateAndTime()
    {
        var item = Repeating(
            "FREQ=WEEKLY;BYDAY=FR",
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 13));
        item.AddReminder(Reminder.FromDue(TimeSpan.FromMinutes(-30)));
        var id = Save(item);

        using var db = _database.NewContext();
        var loaded = db.Items.Single(i => i.Id == id);
        loaded.SetDue(Day(2026, 11, 11), new TimeOnly(9, 0));
        db.ChangeTracker.DetectChanges();

        var entry = db.Entry(loaded);
        Assert.Equal(
            ["DueDate", "DueTime"],
            entry.Properties
                .Where(p => p.IsModified)
                .Select(p => p.Metadata.Name)
                .Order());
        Assert.False(entry.ComplexProperty(i => i.Recurrence).IsModified);
        var reminder = Assert.Single(db.ChangeTracker.Entries<Reminder>());
        Assert.Equal(EntityState.Unchanged, reminder.State);
    }

    [Fact]
    public void MovedEarlierAndCompleted_DoesNotComeRoundAgain()
    {
        var id = Save(
            Repeating(
                "FREQ=WEEKLY;BYDAY=FR",
                RecurrenceMode.FixedSchedule,
                Day(2026, 11, 13)));

        // Friday's occurrence is moved to Wednesday and done then.
        // Only the stored occurrence day says it was Friday's.
        Change(
            id,
            item => item.SetDue(Day(2026, 11, 11), new TimeOnly(9, 0)));
        Change(id, item => item.Complete(November(11, 10), NewYork));

        var after = Load(id);
        Assert.Equal(Day(2026, 11, 20), after.DueDate);
        Assert.Equal(Day(2026, 11, 20), after.OccurrenceDate);
        Assert.Equal(new TimeOnly(9, 0), after.DueTime);
    }

    [Fact]
    public void Complete_LastOccurrence_FinishesTheItem()
    {
        var id = Save(
            Repeating(
                "FREQ=DAILY;UNTIL=20261113",
                RecurrenceMode.FixedSchedule,
                Day(2026, 11, 12)));

        Change(id, item => item.Complete(November(12, 8), NewYork));
        Assert.False(Load(id).IsCompleted);

        Change(id, item => item.Complete(November(13, 8), NewYork));
        var after = Load(id);
        Assert.Equal(November(13, 8), after.CompletedAt);
        Assert.Equal(Day(2026, 11, 13), after.DueDate);
        Assert.Equal(
            "FREQ=DAILY;UNTIL=20261113",
            after.Recurrence?.Rule.ToString());
    }

    [Fact]
    public void Complete_OneOffItem_FinishesIt()
    {
        var id = Save(new Item("Buy a ladder"));

        Change(id, item => item.Complete(November(12, 8), NewYork));

        var after = Load(id);
        Assert.Equal(November(12, 8), after.CompletedAt);
        Assert.Throws<InvalidOperationException>(
            () => after.Complete(November(12, 9), NewYork));
    }

    [Fact]
    public void StopRepeating_RemovesTheRuleAndKeepsTheDueDate()
    {
        var id = Save(
            Repeating(
                "FREQ=DAILY",
                RecurrenceMode.AfterCompletion,
                Day(2026, 11, 12)));

        Change(id, item => item.StopRepeating());

        var after = Load(id);
        Assert.Null(after.Recurrence);
        Assert.Null(after.OccurrenceDate);
        Assert.Equal(Day(2026, 11, 12), after.DueDate);

        Change(id, item => item.ClearDue());
        Assert.Null(Load(id).DueDate);
    }

    [Fact]
    public void Repeat_CanBeAddedAndChangedLater()
    {
        var id = Save(new Item("Water the plants"));

        Change(
            id,
            item => item.Repeat(
                RecurrenceRule.Parse("FREQ=DAILY"),
                RecurrenceMode.FixedSchedule,
                Day(2026, 11, 12)));
        Assert.Equal("FREQ=DAILY", Load(id).Recurrence?.Rule.ToString());

        Change(
            id,
            item => item.Repeat(
                RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2"),
                RecurrenceMode.AfterCompletion,
                Day(2026, 11, 14)));
        var after = Load(id);
        Assert.Equal(
            "FREQ=WEEKLY;INTERVAL=2",
            after.Recurrence?.Rule.ToString());
        Assert.Equal(RecurrenceMode.AfterCompletion, after.Recurrence?.Mode);
        Assert.Equal(Day(2026, 11, 14), after.Recurrence?.Anchor);
        Assert.Equal(Day(2026, 11, 14), after.DueDate);
    }

    [Fact]
    public void Repeat_WithTheSameRuleAgain_IsNotAChange()
    {
        var id = Save(
            Repeating(
                "FREQ=WEEKLY;BYDAY=FR",
                RecurrenceMode.FixedSchedule,
                Day(2026, 11, 13)));

        using var db = _database.NewContext();
        var item = db.Items.Single(i => i.Id == id);
        item.Repeat(
            RecurrenceRule.Parse("freq=weekly;byday=fr"),
            RecurrenceMode.FixedSchedule,
            Day(2026, 11, 13));

        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public void Reminders_CanBeAddedAndRemovedLater()
    {
        var first = Reminder.FromDue(TimeSpan.FromHours(9));
        var item = new Item("Put the bins out");
        item.SetDue(Day(2026, 11, 12));
        item.AddReminder(first);
        var id = Save(item);

        var second = Reminder.FromDue(new TimeSpan(-1, -6, 0, 0));
        Change(id, loaded => loaded.AddReminder(second));
        Assert.Equal(
            new[] { first.Id, second.Id }.Order(),
            Load(id).Reminders.Select(r => r.Id).Order());
        Assert.Equal(
            new TimeSpan(-1, -6, 0, 0),
            Load(id).Reminders.Single(r => r.Id == second.Id).Offset);

        Change(
            id,
            loaded => loaded.RemoveReminder(
                loaded.Reminders.Single(r => r.Id == first.Id)));
        Assert.Equal(second.Id, Assert.Single(Load(id).Reminders).Id);
        Assert.Equal(1L, _database.Scalar("SELECT COUNT(*) FROM Reminders"));
    }

    [Fact]
    public void Contexts_AreSharedBetweenItems()
    {
        var phone = new Context("phone");
        var plumber = new Item("Call the plumber");
        var dentist = new Item("Call the dentist");
        plumber.AddContext(phone);
        dentist.AddContext(phone);
        using (var db = _database.NewContext())
        {
            db.Items.AddRange(plumber, dentist);
            db.SaveChanges();
        }

        Assert.Equal(1L, _database.Scalar("SELECT COUNT(*) FROM Contexts"));
        Assert.Equal(phone.Id, Assert.Single(Load(plumber.Id).Contexts).Id);
        Assert.Equal(phone.Id, Assert.Single(Load(dentist.Id).Contexts).Id);

        // Taking it off one item leaves the other, and the context.
        Change(
            plumber.Id,
            item => item.RemoveContext(item.Contexts.Single()));
        Assert.Empty(Load(plumber.Id).Contexts);
        Assert.Equal(phone.Id, Assert.Single(Load(dentist.Id).Contexts).Id);

        // A context that already exists, and a brand-new one.
        using (var db = _database.NewContext())
        {
            var item = db.Items.Single(i => i.Id == plumber.Id);
            item.AddContext(db.Contexts.Single(c => c.Name == "phone"));
            item.AddContext(new Context("home"));
            db.SaveChanges();
        }

        Assert.Equal(
            ["home", "phone"],
            Load(plumber.Id).Contexts.Select(c => c.Name).Order());
        Assert.Equal(2L, _database.Scalar("SELECT COUNT(*) FROM Contexts"));
    }

    [Fact]
    public void DeletingAnItem_TakesItsRemindersAndNothingElse()
    {
        var item = new Item("Order the worktop")
        {
            Project = new Project("House"),
        };
        item.AddContext(new Context("phone"));
        item.AddReminder(Reminder.AtInstant(November(26, 9)));
        var id = Save(item);

        using (var db = _database.NewContext())
        {
            db.Items.Remove(db.Items.Single(i => i.Id == id));
            db.SaveChanges();
        }

        Assert.Equal(0L, _database.Scalar("SELECT COUNT(*) FROM Items"));
        Assert.Equal(0L, _database.Scalar("SELECT COUNT(*) FROM Reminders"));
        Assert.Equal(
            0L,
            _database.Scalar("SELECT COUNT(*) FROM ItemContexts"));
        Assert.Equal(1L, _database.Scalar("SELECT COUNT(*) FROM Contexts"));
        Assert.Equal(1L, _database.Scalar("SELECT COUNT(*) FROM Projects"));
    }

    [Fact]
    public async Task Find_LoadsTheWholeItem()
    {
        var item = new Item("Order the worktop")
        {
            Project = new Project("House"),
        };
        item.AddContext(new Context("phone"));
        item.AddReminder(Reminder.AtInstant(November(26, 9)));
        var id = Save(item);

        await using var db = _database.NewContext();
        var found = await db.Items.FindAsync(id);

        Assert.NotNull(found);
        Assert.Equal("House", found.Project?.Name);
        Assert.Single(found.Contexts);
        Assert.Single(found.Reminders);
    }

    [Fact]
    public void Items_CanBeFilteredAndSortedByTheDatabase()
    {
        Item Make(
            string title,
            ItemState state,
            Priority priority,
            DateOnly? due)
        {
            var item = new Item(title)
            {
                State = state,
                Priority = priority,
            };
            if (due is { } date)
            {
                item.SetDue(date);
            }

            return item;
        }

        using (var db = _database.NewContext())
        {
            db.Items.AddRange(
                Make(
                    "Sort the garage",
                    ItemState.Someday,
                    Priority.High,
                    null),
                Make(
                    "Buy a ladder",
                    ItemState.NextAction,
                    Priority.Low,
                    Day(2026, 11, 20)),
                Make(
                    "Call the plumber",
                    ItemState.NextAction,
                    Priority.High,
                    Day(2026, 11, 14)),
                Make(
                    "Renew the passport",
                    ItemState.NextAction,
                    Priority.Medium,
                    Day(2027, 2, 1)));
            db.SaveChanges();
        }

        using var read = _database.NewContext();
        Assert.Equal(
            ["Call the plumber", "Renew the passport", "Buy a ladder"],
            read.Items
                .Where(i => i.State == ItemState.NextAction)
                .OrderByDescending(i => i.Priority)
                .Select(i => i.Title)
                .ToList());
        Assert.Equal(
            ["Call the plumber", "Buy a ladder"],
            read.Items
                .Where(i => i.DueDate <= new DateOnly(2026, 11, 30))
                .OrderBy(i => i.DueDate)
                .Select(i => i.Title)
                .ToList());
        Assert.Equal(
            ["Call the plumber", "Sort the garage"],
            read.Items
                .Where(i => i.Priority >= Priority.High)
                .OrderBy(i => i.Title)
                .Select(i => i.Title)
                .ToList());
    }

    [Fact]
    public void Instants_SortAndCompareInTheDatabase()
    {
        // In order of time: Tokyo, London, New York. As local text
        // with an offset they would sort the other way round.
        DateTimeOffset At(
            int day,
            int hour,
            int minute,
            int utcOffsetHours)
        {
            return new DateTimeOffset(
                2026,
                11,
                day,
                hour,
                minute,
                0,
                TimeSpan.FromHours(utcOffsetHours));
        }

        var utc = TimeZoneInfo.Utc;
        var newYork = new Item("Done in New York");
        newYork.Complete(At(12, 23, 30, -5), utc);
        var london = new Item("Done in London");
        london.Complete(At(13, 3, 0, 0), utc);
        var tokyo = new Item("Done in Tokyo");
        tokyo.Complete(At(13, 9, 0, 9), utc);
        using (var db = _database.NewContext())
        {
            db.Items.AddRange(newYork, london, tokyo, new Item("Not done"));
            db.SaveChanges();
        }

        using var read = _database.NewContext();
        Assert.Equal(
            ["Done in Tokyo", "Done in London", "Done in New York"],
            read.Items
                .Where(i => i.CompletedAt != null)
                .OrderBy(i => i.CompletedAt)
                .Select(i => i.Title)
                .ToList());

        // 02:00 UTC, written as Tokyo time.
        var cutoff = At(13, 11, 0, 9);
        Assert.Equal(
            ["Done in London", "Done in New York"],
            read.Items
                .Where(i => i.CompletedAt > cutoff)
                .OrderBy(i => i.CompletedAt)
                .Select(i => i.Title)
                .ToList());
    }
}
