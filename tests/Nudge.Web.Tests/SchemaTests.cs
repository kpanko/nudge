using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Core;

namespace Nudge.Web.Tests;

/// <summary>
/// What the database itself holds and enforces, checked with plain
/// SQL so that the entities cannot hide a problem.
/// </summary>
public sealed class SchemaTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    private static Item RepeatingItem()
    {
        var item = new Item("Water the plants");
        item.Repeat(
            RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=FR"),
            RecurrenceMode.FixedSchedule,
            new DateOnly(2026, 11, 13));
        return item;
    }

    private void Save(params Item[] items)
    {
        using var db = _database.NewContext();
        db.Items.AddRange(items);
        db.SaveChanges();
    }

    // If this fails, the model was changed without adding a
    // migration: see the note on NudgeDbContext.
    [Fact]
    public void Model_MatchesTheMigrations()
    {
        using var db = _database.NewContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void EveryMigration_IsApplied()
    {
        using var db = _database.NewContext();

        Assert.NotEmpty(db.Database.GetAppliedMigrations());
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    // These numbers are what the Priority column holds.
    [Fact]
    public void Priority_NumbersDoNotChange()
    {
        Assert.Equal(0, (int)Priority.None);
        Assert.Equal(1, (int)Priority.Low);
        Assert.Equal(2, (int)Priority.Medium);
        Assert.Equal(3, (int)Priority.High);
    }

    // Changing how a value is stored needs a migration that rewrites
    // the rows already there; this test is the reminder.
    [Fact]
    public void Values_AreStoredInAReadableForm()
    {
        var item = RepeatingItem();
        item.State = ItemState.WaitingFor;
        item.Priority = Priority.High;
        item.DeferUntil = new DateOnly(2026, 11, 9);
        item.DoNotAutoReschedule = true;
        item.SetDue(new DateOnly(2026, 11, 13), new TimeOnly(14, 30));
        item.AddReminder(Reminder.FromDue(TimeSpan.FromMinutes(-30)));
        item.AddReminder(
            Reminder.AtInstant(
                new DateTimeOffset(
                    2026,
                    11,
                    12,
                    23,
                    30,
                    0,
                    TimeSpan.FromHours(-5))));
        Save(item);

        object? Column(string name)
        {
            return _database.Scalar($"SELECT {name} FROM Items");
        }

        Assert.Equal(
            item.Id.ToString().ToUpperInvariant(),
            Column("Id"));
        Assert.Equal("WaitingFor", Column("State"));
        Assert.Equal(3L, Column("Priority"));
        Assert.Equal("2026-11-09", Column("DeferUntil"));
        Assert.Equal(1L, Column("DoNotAutoReschedule"));
        Assert.Equal("2026-11-13", Column("DueDate"));
        Assert.Equal("14:30:00", Column("DueTime"));
        Assert.Equal("FREQ=WEEKLY;BYDAY=FR", Column("RecurrenceRule"));
        Assert.Equal("FixedSchedule", Column("RecurrenceMode"));
        Assert.Equal("2026-11-13", Column("RecurrenceAnchor"));
        Assert.Equal("2026-11-13", Column("OccurrenceDate"));
        Assert.Null(Column("CompletedAt"));
        Assert.Null(Column("ProjectId"));
        Assert.Equal(
            "-00:30:00",
            _database.Scalar(
                """
                SELECT "Offset" FROM Reminders WHERE "At" IS NULL
                """));
        Assert.Equal(
            "2026-11-13T04:30:00.0000000Z",
            _database.Scalar(
                """
                SELECT "At" FROM Reminders WHERE "Offset" IS NULL
                """));
    }

    [Fact]
    public void ItemThatDoesNotRepeat_HasNoRecurrenceColumns()
    {
        Save(new Item("Buy a ladder"));

        Assert.Equal(
            1L,
            _database.Scalar(
                """
                SELECT COUNT(*) FROM Items
                WHERE RecurrenceRule IS NULL
                  AND RecurrenceMode IS NULL
                  AND RecurrenceAnchor IS NULL
                  AND OccurrenceDate IS NULL
                """));
    }

    // Relied on: a name the enum does not have is an error when it
    // is read. Were it read as the enum's first value, the item
    // would quietly turn up in the inbox, or change how it repeats.
    [Theory]
    [InlineData("State")]
    [InlineData("RecurrenceMode")]
    public void UnknownName_IsAnError_NotTheFirstValue(string column)
    {
        Save(RepeatingItem());
        _database.Execute($"UPDATE Items SET {column} = 'Sometime'");

        using var db = _database.NewContext();
        var error = Assert.ThrowsAny<Exception>(() => db.Items.ToList());

        Assert.Contains("'Sometime'", error.ToString());
    }

    [Theory]
    [InlineData("OccurrenceDate = NULL", "CK_Items_Recurrence")]
    [InlineData("RecurrenceMode = NULL", "CK_Items_Recurrence")]
    [InlineData("RecurrenceAnchor = NULL", "CK_Items_Recurrence")]
    [InlineData("RecurrenceRule = NULL", "CK_Items_Recurrence")]
    [InlineData("DueDate = NULL", "CK_Items_Due")]
    public void RepeatingItem_CannotBeLeftHalfRepeating(
        string change,
        string constraint)
    {
        Save(RepeatingItem());

        var error = Assert.Throws<SqliteException>(
            () => _database.Execute($"UPDATE Items SET {change}"));

        Assert.Contains(constraint, error.Message);
    }

    [Fact]
    public void DueTime_NeedsADueDate()
    {
        Save(new Item("Buy a ladder"));

        var error = Assert.Throws<SqliteException>(
            () => _database.Execute("UPDATE Items SET DueTime = '09:00:00'"));

        Assert.Contains("CK_Items_Due", error.Message);
    }

    [Theory]
    [InlineData("""SET "At" = NULL, "Offset" = NULL""")]
    [InlineData(
        """SET "At" = '2026-11-13T04:30:00.0000000Z', "Offset" = '00:00:00'""")]
    public void Reminder_IsOneKindOrTheOther(string change)
    {
        var item = new Item("Buy a ladder");
        item.AddReminder(Reminder.FromDue(TimeSpan.Zero));
        Save(item);

        var error = Assert.Throws<SqliteException>(
            () => _database.Execute($"UPDATE Reminders {change}"));

        Assert.Contains("CK_Reminders_Kind", error.Message);
    }

    [Fact]
    public void Reminder_CannotBeLeftWithoutItsItem()
    {
        var item = new Item("Buy a ladder");
        item.AddReminder(Reminder.FromDue(TimeSpan.Zero));
        Save(item);

        Assert.Throws<SqliteException>(
            () => _database.Execute(
                "UPDATE Reminders SET ItemId = 'no-such-item'"));
    }

    [Fact]
    public void ContextNames_AreUnique_WhateverTheCase()
    {
        using (var db = _database.NewContext())
        {
            db.Contexts.Add(new Context("phone"));
            db.SaveChanges();
        }

        using (var db = _database.NewContext())
        {
            db.Contexts.Add(new Context("Phone"));
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        using (var db = _database.NewContext())
        {
            Assert.Equal(
                "phone",
                db.Contexts.Single(c => c.Name == "PHONE").Name);
            db.Contexts.Add(new Context("phone booth"));
            db.SaveChanges();
        }
    }
}
