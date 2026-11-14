using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>What saving writes to the change log.</summary>
public sealed class ChangeLogTests : IDisposable
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>What the clock shows: midday in New York.</summary>
    private static readonly DateTimeOffset Now =
        new(2026, 11, 13, 17, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database =
        new() { Clock = new FixedClock(Now) };

    public void Dispose()
    {
        _database.Dispose();
    }

    private static DateOnly Day(int day)
    {
        return new DateOnly(2026, 11, day);
    }

    /// <summary>08:00 in New York on a day in November 2026, which
    /// is 13:00 UTC.</summary>
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

    /// <summary>An id as the database and the log write it.</summary>
    private static string Text(Guid id)
    {
        return id.ToString().ToUpperInvariant();
    }

    private static Item Weekly()
    {
        var item = new Item("Water the plants");
        item.Repeat(
            RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=FR"),
            RecurrenceMode.FixedSchedule,
            Day(13));
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
    private void Edit(
        Guid id,
        Action<Item> edit)
    {
        using var db = _database.NewContext();
        edit(db.Items.Single(i => i.Id == id));
        db.SaveChanges();
    }

    private Change LastChange()
    {
        return _database.Changes().Last();
    }

    private static ChangeEntry EntryFor(
        Change change,
        string table)
    {
        return Assert.Single(change.Entries, e => e.TableName == table);
    }

    [Fact]
    public void NewItem_IsRecordedWithEveryRowItAdds()
    {
        var house = new Project("House");
        var phone = new Context("phone");
        var reminder = Reminder.FromDue(TimeSpan.FromMinutes(-30));
        var item = new Item("Order the worktop") { Project = house };
        item.AddContext(phone);
        item.AddReminder(reminder);
        using (var db = _database.NewContext())
        {
            db.Description = "Add item";
            db.Items.Add(item);
            db.SaveChanges();
        }

        var change = Assert.Single(_database.Changes());
        Assert.Equal(1, change.Id);
        Assert.Equal(Now, change.At);
        Assert.Equal("test", change.Source);
        Assert.Equal("Add item", change.Description);
        Assert.Null(change.Undoes);
        Assert.Equal(
            ["Contexts", "ItemContexts", "Items", "Projects", "Reminders"],
            change.Entries.Select(e => e.TableName).Order());
        Assert.Equal(
            [0, 1, 2, 3, 4],
            change.Entries.Select(e => e.Ordinal).Order());
        Assert.All(
            change.Entries,
            entry =>
            {
                Assert.Equal(ChangeKind.Added, entry.Kind);
                Assert.Null(entry.Before);
            });

        var added = EntryFor(change, "Items");
        Assert.Equal($$"""{"Id":"{{Text(item.Id)}}"}""", added.RowKey);
        var after = JsonNode.Parse(added.After!)!;
        Assert.Equal("Order the worktop", (string?)after["Title"]);
        Assert.Equal(Text(house.Id), (string?)after["ProjectId"]);
        Assert.Equal("Inbox", (string?)after["State"]);

        JsonAssert.Equal(
            $$"""
            {
              "Id": "{{Text(house.Id)}}",
              "DeletedAt": null,
              "Name": "House",
              "ParentId": null
            }
            """,
            EntryFor(change, "Projects").After);
        JsonAssert.Equal(
            $$"""
            {
              "Id": "{{Text(phone.Id)}}",
              "DeletedAt": null,
              "Name": "phone"
            }
            """,
            EntryFor(change, "Contexts").After);
        JsonAssert.Equal(
            $$"""
            {
              "Id": "{{Text(reminder.Id)}}",
              "At": null,
              "ItemId": "{{Text(item.Id)}}",
              "Offset": "-00:30:00"
            }
            """,
            EntryFor(change, "Reminders").After);

        // A link has no id of its own: its key is the pair.
        var link = EntryFor(change, "ItemContexts");
        var pair =
            "{\"ItemId\":\"" + Text(item.Id)
            + "\",\"ContextId\":\"" + Text(phone.Id) + "\"}";
        Assert.Equal(pair, link.RowKey);
        Assert.Equal(pair, link.After);
    }

    // Entries are read by people and by the undo. Changing how a
    // value is written here needs thought about the entries already
    // in the log; this test is the reminder.
    [Fact]
    public void Edit_RecordsOnlyWhatChanged_AsItIsStored()
    {
        var item = new Item("Put the bins out");
        item.SetDue(Day(13));
        var id = Save(item);

        Edit(
            id,
            loaded =>
            {
                loaded.SetDue(Day(16), new TimeOnly(9, 0));
                loaded.Priority = Priority.High;
                loaded.State = ItemState.NextAction;
                loaded.DoNotAutoReschedule = true;
                loaded.Complete(Morning(12), NewYork);
            });

        var entry = Assert.Single(LastChange().Entries);
        Assert.Equal("Items", entry.TableName);
        Assert.Equal(ChangeKind.Changed, entry.Kind);
        Assert.Equal($$"""{"Id":"{{Text(id)}}"}""", entry.RowKey);
        JsonAssert.Equal(
            """
            {
              "CompletedAt": null,
              "DoNotAutoReschedule": false,
              "DueDate": "2026-11-13",
              "DueTime": null,
              "Priority": 0,
              "State": "Inbox"
            }
            """,
            entry.Before);
        JsonAssert.Equal(
            """
            {
              "CompletedAt": "2026-11-12T13:00:00.0000000Z",
              "DoNotAutoReschedule": true,
              "DueDate": "2026-11-16",
              "DueTime": "09:00:00",
              "Priority": 3,
              "State": "NextAction"
            }
            """,
            entry.After);

        // Written without spaces or line breaks.
        Assert.StartsWith("""{"CompletedAt":null,"Do""", entry.Before);
    }

    [Fact]
    public void Text_IsLeftReadable()
    {
        Save(new Item("Don't forget the café") { Notes = "<Zoë's> & co" });

        var after = EntryFor(LastChange(), "Items").After;

        Assert.Contains("\"Title\":\"Don't forget the café\"", after);
        Assert.Contains("\"Notes\":\"<Zoë's> & co\"", after);
    }

    // Rescheduling writes the due date and nothing else: the log
    // shows it, for the second named M1 regression test.
    [Fact]
    public void SettingDueDate_IsRecordedAsTheDueDateAlone()
    {
        var item = Weekly();
        item.AddReminder(Reminder.FromDue(TimeSpan.FromMinutes(-30)));
        var id = Save(item);

        Edit(id, loaded => loaded.SetDue(Day(11)));

        var entry = Assert.Single(LastChange().Entries);
        Assert.Equal("""{"DueDate":"2026-11-13"}""", entry.Before);
        Assert.Equal("""{"DueDate":"2026-11-11"}""", entry.After);
    }

    [Fact]
    public void Completing_ARepeatingItem_RecordsTheDatesThatMoved()
    {
        var item = Weekly();
        item.DeferUntil = Day(11);
        var id = Save(item);

        Edit(id, loaded => loaded.Complete(Morning(13), NewYork));

        var entry = Assert.Single(LastChange().Entries);
        JsonAssert.Equal(
            """
            {
              "DeferUntil": "2026-11-11",
              "DueDate": "2026-11-13",
              "OccurrenceDate": "2026-11-13"
            }
            """,
            entry.Before);
        JsonAssert.Equal(
            """
            {
              "DeferUntil": "2026-11-18",
              "DueDate": "2026-11-20",
              "OccurrenceDate": "2026-11-20"
            }
            """,
            entry.After);
    }

    // EF Core reports the parts of a repeat rule that is not there
    // as "FixedSchedule" and the year 1. The columns hold null, and
    // so must the log, or an undo would look for the wrong values.
    [Fact]
    public void RepeatRule_IsRecordedAsItsColumns_NullWhenThereIsNone()
    {
        var id = Save(new Item("Water the plants"));
        var added = JsonNode.Parse(EntryFor(LastChange(), "Items").After!)!;
        Assert.Null(added["RecurrenceRule"]);
        Assert.Null(added["RecurrenceMode"]);
        Assert.Null(added["RecurrenceAnchor"]);
        Assert.Null(added["OccurrenceDate"]);

        Edit(
            id,
            item => item.Repeat(
                RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=FR"),
                RecurrenceMode.FixedSchedule,
                Day(13)));
        var started = Assert.Single(LastChange().Entries);
        JsonAssert.Equal(
            """
            {
              "DueDate": null,
              "OccurrenceDate": null,
              "RecurrenceAnchor": null,
              "RecurrenceMode": null,
              "RecurrenceRule": null
            }
            """,
            started.Before);
        JsonAssert.Equal(
            """
            {
              "DueDate": "2026-11-13",
              "OccurrenceDate": "2026-11-13",
              "RecurrenceAnchor": "2026-11-13",
              "RecurrenceMode": "FixedSchedule",
              "RecurrenceRule": "FREQ=WEEKLY;BYDAY=FR"
            }
            """,
            started.After);

        Edit(
            id,
            item => item.Repeat(
                RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=3"),
                RecurrenceMode.AfterCompletion,
                Day(13)));
        var changed = Assert.Single(LastChange().Entries);
        JsonAssert.Equal(
            """
            {
              "RecurrenceMode": "FixedSchedule",
              "RecurrenceRule": "FREQ=WEEKLY;BYDAY=FR"
            }
            """,
            changed.Before);
        JsonAssert.Equal(
            """
            {
              "RecurrenceMode": "AfterCompletion",
              "RecurrenceRule": "FREQ=DAILY;INTERVAL=3"
            }
            """,
            changed.After);

        Edit(id, item => item.StopRepeating());
        var stopped = Assert.Single(LastChange().Entries);
        JsonAssert.Equal(
            """
            {
              "OccurrenceDate": "2026-11-13",
              "RecurrenceAnchor": "2026-11-13",
              "RecurrenceMode": "AfterCompletion",
              "RecurrenceRule": "FREQ=DAILY;INTERVAL=3"
            }
            """,
            stopped.Before);
        JsonAssert.Equal(
            """
            {
              "OccurrenceDate": null,
              "RecurrenceAnchor": null,
              "RecurrenceMode": null,
              "RecurrenceRule": null
            }
            """,
            stopped.After);
    }

    [Fact]
    public void TakingAwayAReminderOrAContext_RecordsTheRowThatWent()
    {
        var phone = new Context("phone");
        var reminder = Reminder.AtInstant(Morning(26));
        var item = new Item("Order the worktop");
        item.AddContext(phone);
        item.AddReminder(reminder);
        var id = Save(item);

        Edit(
            id,
            loaded =>
            {
                loaded.RemoveContext(loaded.Contexts.Single());
                loaded.RemoveReminder(loaded.Reminders.Single());
            });

        // The item's own row did not change, and the context stays.
        var change = LastChange();
        Assert.Equal(
            ["ItemContexts", "Reminders"],
            change.Entries.Select(e => e.TableName).Order());
        Assert.All(
            change.Entries,
            entry =>
            {
                Assert.Equal(ChangeKind.Removed, entry.Kind);
                Assert.Null(entry.After);
            });
        JsonAssert.Equal(
            $$"""
            {
              "Id": "{{Text(reminder.Id)}}",
              "At": "2026-11-26T13:00:00.0000000Z",
              "ItemId": "{{Text(id)}}",
              "Offset": null
            }
            """,
            EntryFor(change, "Reminders").Before);
        JsonAssert.Equal(
            $$"""
            {
              "ItemId": "{{Text(id)}}",
              "ContextId": "{{Text(phone.Id)}}"
            }
            """,
            EntryFor(change, "ItemContexts").Before);
    }

    // If this fails, a table or column was added that the change
    // log does not see, so changes to it could not be undone. The
    // rows this test adds must cover every table of the owner's
    // data.
    [Fact]
    public void EveryColumnOfEveryTable_IsRecorded()
    {
        var item = new Item("Order the worktop")
        {
            Project = new Project("House"),
        };
        item.AddContext(new Context("phone"));
        item.AddReminder(Reminder.FromDue(TimeSpan.Zero));
        Save(item);

        var recorded = LastChange().Entries.ToDictionary(
            entry => entry.TableName,
            entry => JsonNode.Parse(entry.After!)!
                .AsObject()
                .Select(pair => pair.Key)
                .Order()
                .ToList());

        var tables = _database.DataTables();
        Assert.Equal(
            ["Contexts", "ItemContexts", "Items", "Projects", "Reminders"],
            tables);
        Assert.Equal(tables, recorded.Keys.Order());
        foreach (var table in tables)
        {
            Assert.Equal(_database.Columns(table).Order(), recorded[table]);
        }
    }

    [Fact]
    public void Save_WithNothingChanged_RecordsNothing()
    {
        var id = Save(Weekly());

        // Read, and set to what it already was.
        Edit(
            id,
            item =>
            {
                item.Title = "Water the plants";
                item.SetDue(Day(13));
            });

        Assert.Single(_database.Changes());
    }

    [Fact]
    public void Changes_AreNumberedInTheOrderTheyWereSaved()
    {
        var clock = (FixedClock)_database.Clock;
        var id = Save(new Item("Buy a ladder"));

        // The clock going backwards does not reorder the log.
        clock.Now = Now.AddHours(-1);
        using (var db = _database.NewContext())
        {
            db.Source = " planning-agent ";
            db.Description = "Tidy the title";
            db.Items.Single(i => i.Id == id).Title = "Buy a step ladder";
            db.SaveChanges();
        }

        clock.Now = Now.AddHours(1);
        Edit(id, item => item.Notes = "Two metres.");

        var changes = _database.Changes();
        Assert.Equal([1L, 2L, 3L], changes.Select(c => c.Id));
        Assert.Equal(
            [Now, Now.AddHours(-1), Now.AddHours(1)],
            changes.Select(c => c.At));
        Assert.Equal(
            ["test", "planning-agent", "test"],
            changes.Select(c => c.Source));
        Assert.Equal(
            ["", "Tidy the title", ""],
            changes.Select(c => c.Description));
    }

    [Fact]
    public async Task SaveChangesAsync_IsRecordedToo()
    {
        await using (var db = _database.NewContext())
        {
            db.Items.Add(new Item("Buy a ladder"));
            await db.SaveChangesAsync();
        }

        var change = Assert.Single(_database.Changes());
        Assert.Equal("Items", Assert.Single(change.Entries).TableName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Save_WithoutSayingWho_IsRefused(string? source)
    {
        using var db = _database.NewContext();
        db.Source = source;
        db.Items.Add(new Item("Buy a ladder"));

        var error = Assert.Throws<InvalidOperationException>(
            () => db.SaveChanges());

        Assert.Contains("Source", error.Message);
        Assert.Equal(0L, _database.Scalar("SELECT COUNT(*) FROM Items"));
        Assert.Equal(0L, _database.Scalar("SELECT COUNT(*) FROM Changes"));
    }

    [Fact]
    public void FailedSave_LeavesNothingInTheLog()
    {
        using (var first = _database.NewContext())
        {
            first.Contexts.Add(new Context("phone"));
            first.SaveChanges();
        }

        using var db = _database.NewContext();
        var duplicate = new Context("Phone");
        db.Contexts.Add(duplicate);
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.Single(_database.Changes());

        // The same context can go on, and records the save that
        // works once, not the one that failed as well.
        duplicate.Name = "Phone booth";
        db.SaveChanges();

        var changes = _database.Changes();
        Assert.Equal(2, changes.Count);
        Assert.Equal(
            "Phone booth",
            (string?)JsonNode.Parse(
                Assert.Single(changes[1].Entries).After!)!["Name"]);
        Assert.Equal(
            2L,
            _database.Scalar("SELECT COUNT(*) FROM ChangeEntries"));
    }

    // "Deletes are soft": the row of an item, project or context is
    // marked, never removed, whatever the calling code tries.
    [Fact]
    public void RemovingTheRowOfAnItemProjectOrContext_IsRefused()
    {
        var item = new Item("Order the worktop")
        {
            Project = new Project("House"),
        };
        item.AddContext(new Context("phone"));
        var id = Save(item);

        void Refused(Action<NudgeDbContext, Item> remove)
        {
            using var db = _database.NewContext();
            remove(db, db.Items.Single(i => i.Id == id));
            var error = Assert.Throws<InvalidOperationException>(
                () => db.SaveChanges());
            Assert.Contains("never removed", error.Message);
        }

        var before = _database.Dump();
        Refused((db, loaded) => db.Items.Remove(loaded));
        Refused((db, loaded) => db.Projects.Remove(loaded.Project!));
        Refused((db, loaded) => db.Contexts.Remove(loaded.Contexts.Single()));

        Assert.Equal(before, _database.Dump());
        Assert.Single(_database.Changes());
    }

    [Fact]
    public void TheLog_IsOnlyEverAddedTo()
    {
        Save(new Item("Buy a ladder"));

        using (var db = _database.NewContext())
        {
            var change = db.Changes.Single();
            Assert.Throws<InvalidOperationException>(
                () =>
                {
                    db.Changes.Remove(change);
                    db.SaveChanges();
                });
        }

        using (var db = _database.NewContext())
        {
            var entry = db.Changes.Single().Entries.Single();
            db.Remove(entry);
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }

        using (var db = _database.NewContext())
        {
            var entry = db.Changes.Single().Entries.Single();
            db.Entry(entry).Property(e => e.After).CurrentValue = "{}";
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }

        Assert.Single(_database.Changes());
        Assert.Throws<SqliteException>(
            () => _database.Execute("DELETE FROM Changes"));
    }

    // An entry says which values it has by its kind; the database
    // holds it to that.
    [Theory]
    [InlineData("""SET "Before" = '{}'""")]
    [InlineData("""SET "After" = NULL""")]
    [InlineData("""SET Kind = 'Changed'""")]
    [InlineData("""SET Kind = 'Removed'""")]
    public void Entry_HasTheValuesItsKindCallsFor(string change)
    {
        Save(new Item("Buy a ladder"));

        var error = Assert.Throws<SqliteException>(
            () => _database.Execute($"UPDATE ChangeEntries {change}"));

        Assert.Contains("CK_ChangeEntries_Kind", error.Message);
    }
}
