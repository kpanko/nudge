using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>Taking a change back, and when that is refused.</summary>
public sealed class UndoTests : IDisposable
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly DateTimeOffset Now =
        new(2026, 11, 13, 17, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    /// <summary><see cref="Later"/> as the database writes it.
    /// </summary>
    private const string LaterText = "2026-11-13T17:05:00.0000000Z";

    /// <summary>
    /// Things that can be done to the item <see cref="SaveWorktop"/>
    /// makes. Each one has to come back out exactly.
    /// </summary>
    private static readonly Dictionary<string, Action<NudgeDbContext, Item>>
        Edits = new()
        {
            ["reschedule"] = (_, item) =>
                item.SetDue(Day(11), new TimeOnly(9, 0)),
            ["drop the due time"] = (_, item) => item.SetDue(Day(13)),
            ["complete"] = (_, item) =>
                item.Complete(Morning(13), NewYork),
            ["stop repeating"] = (_, item) => item.StopRepeating(),
            ["stop repeating and clear the due date"] = (_, item) =>
            {
                item.StopRepeating();
                item.ClearDue();
            },
            ["stop repeating and finish"] = (_, item) =>
            {
                item.StopRepeating();
                item.Complete(Morning(13), NewYork);
            },
            ["repeat differently"] = (_, item) => item.Repeat(
                RecurrenceRule.Parse("FREQ=MONTHLY;INTERVAL=6"),
                RecurrenceMode.AfterCompletion,
                Day(20)),
            ["reword"] = (_, item) =>
            {
                item.Title = "Order the oak worktop";
                item.Notes = "";
            },
            ["refile"] = (_, item) =>
            {
                item.State = ItemState.Someday;
                item.Priority = Priority.None;
                item.DoNotAutoReschedule = true;
                item.DeferUntil = null;
            },
            ["take out of its project"] = (_, item) => item.Project = null,
            ["move to another project"] = (db, item) =>
                item.Project = db.Projects.Single(p => p.Name == "Garden"),
            ["rename its project"] = (_, item) =>
                item.Project!.Name = "Home",
            ["take a context off"] = (_, item) => item.RemoveContext(
                item.Contexts.Single(c => c.Name == "phone")),
            ["put a context on"] = (db, item) => item.AddContext(
                db.Contexts.Single(c => c.Name == "home")),
            ["rename a context"] = (_, item) =>
                item.Contexts.Single(c => c.Name == "phone").Name = "calls",
            ["delete a context"] = (_, item) =>
                item.Contexts.Single(c => c.Name == "phone").Delete(Now),
            ["take a reminder off"] = (_, item) => item.RemoveReminder(
                item.Reminders.Single(r => r.Offset is not null)),
            ["swap the reminders"] = (_, item) =>
            {
                foreach (var reminder in item.Reminders.ToList())
                {
                    item.RemoveReminder(reminder);
                }

                item.AddReminder(Reminder.FromDue(TimeSpan.FromHours(-2)));
            },
            ["delete"] = (_, item) => item.Delete(Now),
            ["several at once"] = (db, item) =>
            {
                item.Title = "Order the oak worktop";
                item.Complete(Morning(13), NewYork);
                item.Project = db.Projects.Single(p => p.Name == "Garden");
                item.RemoveContext(item.Contexts.First());
                item.AddContext(db.Contexts.Single(c => c.Name == "home"));
                item.RemoveReminder(item.Reminders[0]);
            },
        };

    private readonly FixedClock _clock = new(Now);
    private readonly TestDatabase _database = new();

    public UndoTests()
    {
        _database.Clock = _clock;
    }

    public static IEnumerable<object[]> EditNames =>
        Edits.Keys.Select(name => new object[] { name });

    public void Dispose()
    {
        _database.Dispose();
    }

    private static DateOnly Day(int day)
    {
        return new DateOnly(2026, 11, day);
    }

    /// <summary>08:00 in New York on a day in November 2026.</summary>
    private static DateTimeOffset Morning(int day)
    {
        return new DateTimeOffset(
            2026,
            11,
            day,
            8,
            0,
            0,
            TimeSpan.FromHours(-5));
    }

    /// <summary>
    /// Does something in a context of its own, saves, and returns
    /// the number of the change that was recorded for it.
    /// </summary>
    private long Do(Action<NudgeDbContext> work)
    {
        var before = _database.Changes().Count;
        using (var db = _database.NewContext())
        {
            work(db);
            db.SaveChanges();
        }

        var changes = _database.Changes();
        Assert.Equal(before + 1, changes.Count);
        return changes[^1].Id;
    }

    private long Edit(
        Guid id,
        Action<Item> edit)
    {
        return Do(db => edit(db.Items.Single(i => i.Id == id)));
    }

    private async Task<Change> UndoAsync(long changeId)
    {
        await using var db = _database.NewContext();
        var undo = await db.UndoAsync(changeId);
        Assert.NotNull(undo);
        return undo;
    }

    /// <summary>An id as the database and the log write it.</summary>
    private static string Text(Guid id)
    {
        return id.ToString().ToUpperInvariant();
    }

    private Item Load(Guid id)
    {
        using var db = _database.NewContext();
        return db.Items.Single(i => i.Id == id);
    }

    /// <summary>
    /// A weekly item with everything set, in the project House, plus
    /// a project and a context it does not use: Garden and "home".
    /// </summary>
    private Guid SaveWorktop()
    {
        var item = new Item("Order the worktop")
        {
            Notes = "Oak, three metres.",
            State = ItemState.NextAction,
            Priority = Priority.Medium,
            Project = new Project("House"),
            DeferUntil = Day(11),
        };
        item.AddContext(new Context("phone"));
        item.AddContext(new Context("errands"));
        item.SetDue(Day(13), new TimeOnly(14, 30));
        item.Repeat(
            RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=FR"),
            RecurrenceMode.FixedSchedule,
            Day(13));
        item.AddReminder(Reminder.FromDue(TimeSpan.FromMinutes(-30)));
        item.AddReminder(Reminder.AtInstant(Morning(12)));
        Do(
            db =>
            {
                db.Items.Add(item);
                db.Projects.Add(new Project("Garden"));
                db.Contexts.Add(new Context("home"));
            });
        return item.Id;
    }

    [Theory]
    [MemberData(nameof(EditNames))]
    public async Task Undo_PutsBackExactlyWhatWasStored(string edit)
    {
        var id = SaveWorktop();
        var before = _database.Dump();

        var change = Do(
            db => Edits[edit](db, db.Items.Single(i => i.Id == id)));
        var after = _database.Dump();
        Assert.NotEqual(before, after);

        var undo = await UndoAsync(change);
        Assert.Equal(before, _database.Dump());
        Assert.Equal(change, undo.Undoes);

        // Taking the undo back does the edit over again, exactly.
        var redo = await UndoAsync(undo.Id);
        Assert.Equal(after, _database.Dump());
        Assert.Equal(undo.Id, redo.Undoes);
    }

    [Fact]
    public async Task Undo_IsRecordedAsAChangeThatMirrorsTheOneItTakesBack()
    {
        var id = SaveWorktop();
        var change = Edit(id, item => item.SetDue(Day(11)));
        var original = _database.Changes().Last();

        _clock.Now = Later;
        Change? returned;
        await using (var db = _database.NewContext())
        {
            db.Source = "web";
            db.Description = "Undo from the history page";
            returned = await db.UndoAsync(change);
        }

        var undo = _database.Changes().Last();
        Assert.Equal(change + 1, undo.Id);
        Assert.Equal(undo.Id, returned?.Id);
        Assert.Equal(Later, undo.At);
        Assert.Equal("web", undo.Source);
        Assert.Equal("Undo from the history page", undo.Description);
        Assert.Equal(change, undo.Undoes);

        var was = Assert.Single(original.Entries);
        var now = Assert.Single(undo.Entries);
        Assert.Equal(ChangeKind.Changed, now.Kind);
        Assert.Equal(was.TableName, now.TableName);
        Assert.Equal(was.RowKey, now.RowKey);
        Assert.Equal(was.After, now.Before);
        Assert.Equal(was.Before, now.After);
        Assert.Equal(
            """{"DueDate":"2026-11-11","DueTime":null}""",
            now.Before);
        Assert.Equal(
            """{"DueDate":"2026-11-13","DueTime":"14:30:00"}""",
            now.After);
    }

    // "Complete once advances once" has to survive an undo: the
    // occurrence that was completed comes back, and completing it
    // again moves the item on by one, not two.
    [Fact]
    public async Task Undo_OfCompletingARepeatingItem_ReopensThatOccurrence()
    {
        var id = SaveWorktop();
        var change = Edit(id, item => item.Complete(Morning(13), NewYork));
        Assert.Equal(Day(20), Load(id).DueDate);
        Assert.Equal(Day(18), Load(id).DeferUntil);

        await UndoAsync(change);

        var reopened = Load(id);
        Assert.False(reopened.IsCompleted);
        Assert.Equal(Day(13), reopened.DueDate);
        Assert.Equal(new TimeOnly(14, 30), reopened.DueTime);
        Assert.Equal(Day(13), reopened.OccurrenceDate);
        Assert.Equal(Day(11), reopened.DeferUntil);
        Assert.Equal(
            "FREQ=WEEKLY;BYDAY=FR",
            reopened.Recurrence?.Rule.ToString());
        Assert.Equal(2, reopened.Reminders.Count);

        Edit(id, item => item.Complete(Morning(13), NewYork));
        Assert.Equal(Day(20), Load(id).DueDate);
        Assert.Equal(Day(20), Load(id).OccurrenceDate);
    }

    // The only way to reopen an item that was finished.
    [Fact]
    public async Task Undo_OfFinishingAnItem_ReopensIt()
    {
        var item = new Item("Buy a ladder");
        Do(db => db.Items.Add(item));
        var change = Edit(
            item.Id,
            loaded => loaded.Complete(Morning(13), NewYork));
        Assert.True(Load(item.Id).IsCompleted);

        await UndoAsync(change);

        Assert.False(Load(item.Id).IsCompleted);
        Assert.Null(Load(item.Id).CompletedAt);
        Edit(item.Id, loaded => loaded.Complete(Morning(14), NewYork));
        Assert.Equal(Morning(14), Load(item.Id).CompletedAt);
    }

    [Fact]
    public async Task Undo_OfAdding_MarksWhatWasAddedDeleted_AndCanBeUndone()
    {
        var item = new Item("Fix the gate") { Project = new Project("Garden") };
        item.AddContext(new Context("outside"));
        item.SetDue(Day(14));
        item.AddReminder(Reminder.FromDue(TimeSpan.FromHours(9)));
        var change = Do(db => db.Items.Add(item));
        var added = _database.Dump();

        _clock.Now = Later;
        var undo = await UndoAsync(change);

        using (var read = _database.NewContext())
        {
            Assert.Empty(read.Items.ToList());
            Assert.Empty(read.Projects.ToList());
            Assert.Empty(read.Contexts.ToList());
        }

        // The rows of items, projects and contexts are never
        // removed, not even here: they are marked, at the time of
        // the undo.
        foreach (var table in new[] { "Items", "Projects", "Contexts" })
        {
            Assert.Equal(
                1L,
                _database.Scalar(
                    $"SELECT COUNT(*) FROM {table}"
                    + $" WHERE DeletedAt = '{LaterText}'"));
        }

        // The reminder and the link to the context have no such
        // mark and are gone ...
        Assert.Equal(0L, _database.Scalar("SELECT COUNT(*) FROM Reminders"));
        Assert.Equal(
            0L,
            _database.Scalar("SELECT COUNT(*) FROM ItemContexts"));

        // ... until the undo is taken back, which restores the lot.
        await UndoAsync(undo.Id);
        Assert.Equal(added, _database.Dump());
        var restored = Load(item.Id);
        Assert.Equal("Garden", restored.Project?.Name);
        Assert.Equal("outside", Assert.Single(restored.Contexts).Name);
        Assert.Equal(
            item.Reminders.Single().Id,
            Assert.Single(restored.Reminders).Id);
    }

    [Fact]
    public async Task Undo_OfDeletingAProject_BringsBackWhatWentWithIt()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        var oldJob = new Item("Strip the wallpaper") { Project = kitchen };
        Do(
            db => db.Items.AddRange(
                new Item("Paint the hall") { Project = house },
                new Item("Order the worktop") { Project = kitchen },
                oldJob));
        Edit(oldJob.Id, item => item.Delete(Now.AddDays(-1)));
        var before = _database.Dump();

        long change;
        await using (var db = _database.NewContext())
        {
            Assert.True(await db.DeleteProjectAsync(house.Id, Now));
            await db.SaveChangesAsync();
            change = _database.Changes().Last().Id;
        }

        using (var read = _database.NewContext())
        {
            Assert.Empty(read.Projects.ToList());
            Assert.Empty(read.Items.ToList());
        }

        await UndoAsync(change);

        // Exactly as it was: the item deleted the day before is
        // still deleted.
        Assert.Equal(before, _database.Dump());
        using (var read = _database.NewContext())
        {
            var projects = read.Projects.ToList();
            Assert.Equal(
                ["House", "Kitchen"],
                projects.Select(p => p.Name).Order());
            Assert.Equal(
                "House",
                projects.Single(p => p.Name == "Kitchen").Parent?.Name);
            Assert.Equal(
                ["Order the worktop", "Paint the hall"],
                read.Items.Select(i => i.Title).Order().ToList());
        }
    }

    [Fact]
    public async Task Undo_IsRefused_WhenWhatItWouldPutBackWasChangedAgain()
    {
        var id = SaveWorktop();
        var first = Edit(
            id,
            item =>
            {
                item.SetDue(Day(11));
                item.Title = "Order the oak worktop";
            });
        Edit(id, item => item.SetDue(Day(16)));
        var before = _database.Dump();
        var changes = _database.Changes().Count;

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(first));

        // It says what stands in the way, and does none of it: the
        // title is not put back either.
        var reason = Assert.Single(error.Reasons);
        Assert.Contains("Items", reason);
        Assert.Contains(id.ToString().ToUpperInvariant(), reason);
        Assert.Contains("DueDate", reason);
        Assert.DoesNotContain("Title", reason);
        Assert.Contains(reason, error.Message);
        Assert.Equal(before, _database.Dump());
        Assert.Equal(changes, _database.Changes().Count);
    }

    [Fact]
    public async Task Undo_IsNotStoppedByLaterChangesToOtherThings()
    {
        var id = SaveWorktop();
        var first = Edit(
            id,
            item => item.SetDue(Day(11), new TimeOnly(9, 0)));
        Do(
            db =>
            {
                var item = db.Items.Single(i => i.Id == id);
                item.Title = "Order the oak worktop";
                item.Priority = Priority.High;
                item.Project = null;
                item.AddContext(db.Contexts.Single(c => c.Name == "home"));
                item.RemoveReminder(item.Reminders[0]);
            });

        await UndoAsync(first);

        var after = Load(id);
        Assert.Equal(Day(13), after.DueDate);
        Assert.Equal(new TimeOnly(14, 30), after.DueTime);
        Assert.Equal("Order the oak worktop", after.Title);
        Assert.Equal(Priority.High, after.Priority);
        Assert.Null(after.Project);
        Assert.Equal(3, after.Contexts.Count);
        Assert.Single(after.Reminders);
    }

    [Fact]
    public async Task Undo_Twice_IsRefused_UntilTheUndoIsTakenBack()
    {
        var id = SaveWorktop();
        var change = Edit(id, item => item.Title = "Order the oak worktop");

        var undo = await UndoAsync(change);
        Assert.Equal("Order the worktop", Load(id).Title);
        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(change));
        Assert.Equal(
            $"Change {change} has already been undone, by change"
            + $" {undo.Id}.",
            Assert.Single(error.Reasons));

        var redo = await UndoAsync(undo.Id);
        Assert.Equal("Order the oak worktop", Load(id).Title);
        error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(undo.Id));
        Assert.Contains($"by change {redo.Id}", error.Message);

        // The change is in force again, so it can be undone again.
        var again = await UndoAsync(change);
        Assert.Equal("Order the worktop", Load(id).Title);
        Assert.Equal(change, again.Undoes);
        Assert.Equal(
            [change, undo.Id, redo.Id, again.Id],
            _database.Changes().TakeLast(4).Select(c => c.Id));
    }

    [Fact]
    public async Task Undo_ThatWouldLeaveAnItemInADeletedProject_IsRefused()
    {
        var id = SaveWorktop();
        var houseId = Load(id).Project!.Id;
        var deleteItem = Edit(id, item => item.Delete(Now));
        long deleteProject;
        await using (var db = _database.NewContext())
        {
            Assert.True(await db.DeleteProjectAsync(houseId, Later));
            await db.SaveChangesAsync();
            deleteProject = _database.Changes().Last().Id;
        }

        var before = _database.Dump();

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(deleteItem));

        Assert.Equal(
            "Afterwards this would be wrong:"
            + $" Item {Text(id)} is not deleted, but its project is.",
            Assert.Single(error.Reasons));
        Assert.Equal(before, _database.Dump());

        // Undone in the opposite order to the one they happened in,
        // both come back.
        await UndoAsync(deleteProject);
        await UndoAsync(deleteItem);
        Assert.Equal("House", Load(id).Project?.Name);
    }

    // Why taking back an addition marks the row rather than removing
    // it, and why even that can be refused: other rows may have
    // come to depend on it.
    [Fact]
    public async Task Undo_OfAddingAProjectThatHasSinceGainedAnItem_IsRefused()
    {
        var add = Do(db => db.Projects.Add(new Project("Garden")));
        Do(
            db => db.Items.Add(
                new Item("Fix the gate") { Project = db.Projects.Single() }));
        var before = _database.Dump();

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(add));

        var item = _database.Scalar("SELECT Id FROM Items");
        Assert.Equal(
            "Afterwards this would be wrong:"
            + $" Item {item} is not deleted, but its project is.",
            Assert.Single(error.Reasons));
        Assert.Equal(before, _database.Dump());
    }

    [Fact]
    public async Task Undo_ThatWouldLeaveAProjectUnderADeletedOne_IsRefused()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        Do(db => db.Projects.Add(kitchen));
        var deleteKitchen = Do(
            db => db.Projects.Single(p => p.Name == "Kitchen").Delete(Now));
        Do(db => db.Projects.Single(p => p.Name == "House").Delete(Later));

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(deleteKitchen));

        Assert.Equal(
            "Afterwards this would be wrong:"
            + $" Project {Text(kitchen.Id)} is not deleted, but the"
            + " project it is under is.",
            Assert.Single(error.Reasons));
    }

    [Fact]
    public async Task Undo_ThatWouldPutAProjectUnderItself_IsRefused()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        Do(db => db.Projects.Add(new Project("Tiling", kitchen)));

        // Kitchen comes out from under House, and then House goes
        // under Kitchen. Putting Kitchen back would make a loop.
        var lift = Do(
            db => db.Projects
                .ToList()
                .Single(p => p.Name == "Kitchen")
                .MoveUnder(null));
        Do(
            db =>
            {
                var projects = db.Projects.ToList();
                projects.Single(p => p.Name == "House").MoveUnder(
                    projects.Single(p => p.Name == "Kitchen"));
            });
        var before = _database.Dump();

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(lift));

        // Tiling would hang below the loop without being on it, and
        // is not named.
        Assert.Equal(
            new[] { house.Id, kitchen.Id }
                .Select(
                    id => "Afterwards this would be wrong:"
                        + $" Project {Text(id)} is under itself.")
                .Order(),
            error.Reasons.Order());
        Assert.Equal(before, _database.Dump());
    }

    [Fact]
    public async Task Undo_ThatWouldGiveTwoContextsOneName_IsRefused()
    {
        Do(db => db.Contexts.Add(new Context("phone")));
        var delete = Do(db => db.Contexts.Single().Delete(Now));
        Do(db => db.Contexts.Add(new Context("Phone")));
        var before = _database.Dump();

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(delete));

        Assert.Contains("UNIQUE", Assert.Single(error.Reasons));
        Assert.Equal(before, _database.Dump());
    }

    [Fact]
    public async Task Undo_OfAddingSomethingSinceTakenAway_IsRefused()
    {
        var id = SaveWorktop();
        var add = Do(
            db => db.Items.Single(i => i.Id == id).AddContext(
                db.Contexts.Single(c => c.Name == "home")));
        Edit(
            id,
            item => item.RemoveContext(
                item.Contexts.Single(c => c.Name == "home")));

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(add));

        var reason = Assert.Single(error.Reasons);
        Assert.StartsWith("ItemContexts ", reason);
        Assert.EndsWith("no longer exists.", reason);
    }

    [Fact]
    public async Task Undo_OfAddingSomethingSinceDeleted_IsRefused()
    {
        var item = new Item("Buy a ladder");
        var add = Do(db => db.Items.Add(item));
        Edit(item.Id, loaded => loaded.Delete(Now));

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(add));

        Assert.EndsWith(
            "has been deleted since.",
            Assert.Single(error.Reasons));
    }

    [Fact]
    public async Task Undo_OfAChangeToARowThatIsGone_IsRefused()
    {
        var item = new Item("Buy a ladder");
        Do(db => db.Items.Add(item));
        var change = Edit(
            item.Id,
            loaded => loaded.Title = "Buy a step ladder");
        _database.Execute("DELETE FROM Items");

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(change));

        Assert.EndsWith(
            "no longer exists.",
            Assert.Single(error.Reasons));
    }

    // Names of tables and columns in the log end up in SQL. They
    // are only used once the model is known to have them, which
    // also covers an entry made before a column was renamed or
    // dropped.
    [Theory]
    [InlineData("""SET TableName = 'Things'""")]
    [InlineData(
        """
        SET TableName = 'Changes', RowKey = '{"Id":1}',
            "Before" = '{"Source":"someone else"}',
            "After" = '{"Source":"test"}'
        """)]
    [InlineData("""SET "After" = '{"Colour":"red"}'""")]
    [InlineData("""SET "Before" = '{"Title\" = ''x''; --":"y"}'""")]
    [InlineData("""SET RowKey = '{"Title":"Buy a ladder"}'""")]
    [InlineData("""SET RowKey = '{}'""")]
    public async Task Undo_OfAnEntryThatDoesNotFitTheTables_IsRefused(
        string tampering)
    {
        var item = new Item("Buy a ladder");
        Do(db => db.Items.Add(item));
        var change = Edit(
            item.Id,
            loaded => loaded.Title = "Buy a step ladder");
        _database.Execute(
            $"UPDATE ChangeEntries {tampering} WHERE ChangeId = {change}");
        var before = _database.Dump();

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(change));

        Assert.Contains("no longer has", Assert.Single(error.Reasons));
        Assert.Equal(before, _database.Dump());
    }

    [Fact]
    public async Task Undo_NeedsACleanContextAndSomeoneToAnswerForIt()
    {
        var item = new Item("Buy a ladder");
        var change = Do(db => db.Items.Add(item));
        var before = _database.Dump();

        await using (var db = _database.NewContext())
        {
            db.Items.Add(new Item("Not saved yet"));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.UndoAsync(change));

            // And it is still there to be saved.
            Assert.True(db.ChangeTracker.HasChanges());
        }

        await using (var db = _database.NewContext())
        {
            db.Source = null;

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.UndoAsync(change));
            Assert.Contains("Source", error.Message);
        }

        await using (var db = _database.NewContext())
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.UndoAsync(change));
            Assert.Contains("transaction of its own", error.Message);
        }

        Assert.Equal(before, _database.Dump());
        Assert.Equal("test", Assert.Single(_database.Changes()).Source);
    }

    [Fact]
    public async Task Undo_MakesItsContextForgetWhatItHadRead()
    {
        var id = SaveWorktop();
        var change = Edit(id, item => item.Title = "Order the oak worktop");

        await using var db = _database.NewContext();
        var stale = db.Items.Single(i => i.Id == id);

        Assert.NotNull(await db.UndoAsync(change));

        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal("Order the oak worktop", stale.Title);
        Assert.Equal(
            "Order the worktop",
            db.Items.Single(i => i.Id == id).Title);

        // The same holds after an undo that was refused.
        _ = db.Items.Single(i => i.Id == id);
        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => db.UndoAsync(change));
        Assert.Contains("already been undone", error.Message);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Undo_OfAChangeThatDoesNotExist_ReturnsNothing()
    {
        var id = SaveWorktop();
        var before = _database.Dump();
        var changes = _database.Changes().Count;

        await using var db = _database.NewContext();

        Assert.Null(await db.UndoAsync(changes + 1));
        Assert.Null(await db.UndoAsync(0));
        Assert.Equal(before, _database.Dump());
        Assert.Equal(changes, _database.Changes().Count);
        Assert.Equal("Order the worktop", Load(id).Title);
    }

    // The rules about the data as a whole are checked after an undo,
    // but only what the undo itself would break counts against it.
    // Refusing every undo because something else is wrong already
    // would take the tool away just when it is needed.
    [Fact]
    public async Task Undo_IsNotStoppedByWhatWasWrongAlready()
    {
        var id = SaveWorktop();
        var change = Edit(id, item => item.Title = "Order the oak worktop");

        // The project is marked by hand, without the item in it.
        _database.Execute(
            "UPDATE Projects SET DeletedAt = '2026-11-13T17:00:00.0000000Z'"
            + " WHERE Name = 'House'");

        await UndoAsync(change);

        Assert.Equal("Order the worktop", Load(id).Title);
    }

    // An entry made before the way a value is stored was changed,
    // such as a list that has since been renamed.
    [Theory]
    [InlineData("""SET "Before" = '{"Title":5}'""")]
    [InlineData("""SET "Before" = '{"State":"Sometime"}'""")]
    [InlineData("""SET "After" = '{"Priority":"high"}'""")]
    [InlineData("""SET "After" = '{"DueDate":"next week"}'""")]
    [InlineData("""SET "Before" = '{"RecurrenceRule":"FREQ=HOURLY"}'""")]
    [InlineData("""SET "Before" = '{"CompletedAt":"2026-11-13 17:00"}'""")]
    [InlineData("""SET RowKey = '{"Id":"the ladder"}'""")]
    public async Task Undo_OfAnEntryWithAValueThatCannotBeRead_IsRefused(
        string tampering)
    {
        var item = new Item("Buy a ladder");
        Do(db => db.Items.Add(item));
        var change = Edit(
            item.Id,
            loaded => loaded.Title = "Buy a step ladder");
        _database.Execute(
            $"UPDATE ChangeEntries {tampering} WHERE ChangeId = {change}");
        var before = _database.Dump();

        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => UndoAsync(change));

        Assert.Contains(
            "can no longer be read",
            Assert.Single(error.Reasons));
        Assert.Equal(before, _database.Dump());
    }
}
