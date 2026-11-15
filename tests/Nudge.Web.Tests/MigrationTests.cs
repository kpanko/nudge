using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>
/// Migrations run over a database that already has rows in it. Each
/// test here stops at an earlier migration, writes rows the way the
/// app of that time would have, and then brings the database up to
/// date.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private readonly SqliteConnection _connection =
        new("Data Source=:memory:;Foreign Keys=True");

    public MigrationTests()
    {
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private NudgeDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<NudgeDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new NudgeDbContext(options) { Source = "test" };
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void ChangeLogAndSoftDelete_KeepsTheRowsThatWereThere()
    {
        using (var old = NewContext())
        {
            old.GetService<IMigrator>().Migrate("InitialCreate");
        }

        Execute(
            """
            INSERT INTO Projects (Id, Name)
            VALUES ('11111111-1111-7111-8111-111111111111', 'House');
            INSERT INTO Contexts (Id, Name)
            VALUES ('22222222-2222-7222-8222-222222222222', 'phone');
            INSERT INTO Items
                (Id, Title, Notes, State, Priority, ProjectId,
                 DoNotAutoReschedule, DueDate)
            VALUES
                ('33333333-3333-7333-8333-333333333333',
                 'Order the worktop', '', 'NextAction', 2,
                 '11111111-1111-7111-8111-111111111111', 0, '2026-11-13');
            INSERT INTO ItemContexts (ItemId, ContextId)
            VALUES
                ('33333333-3333-7333-8333-333333333333',
                 '22222222-2222-7222-8222-222222222222');
            INSERT INTO Reminders (Id, "Offset", ItemId)
            VALUES
                ('44444444-4444-7444-8444-444444444444', '-00:30:00',
                 '33333333-3333-7333-8333-333333333333');
            """);

        using var db = NewContext();
        db.Database.Migrate();

        // Nothing that was there counts as deleted, and it reads as
        // it did.
        Assert.Empty(db.Database.GetPendingMigrations());
        var item = db.Items.Single();
        Assert.Equal("Order the worktop", item.Title);
        Assert.Equal(ItemState.NextAction, item.State);
        Assert.Equal(Priority.Medium, item.Priority);
        Assert.Equal(new DateOnly(2026, 11, 13), item.DueDate);
        Assert.False(item.IsDeleted);
        Assert.Equal("House", item.Project?.Name);
        Assert.False(item.Project?.IsDeleted);
        Assert.Equal("phone", Assert.Single(item.Contexts).Name);
        Assert.Equal(
            TimeSpan.FromMinutes(-30),
            Assert.Single(item.Reminders).Offset);

        // The log starts empty and takes the next save.
        Assert.Empty(db.Changes.ToList());
        item.Title = "Order the oak worktop";
        db.SaveChanges();
        Assert.Equal(1, db.Changes.Single().Id);

        // Context names are still unique.
        db.Contexts.Add(new Context("Phone"));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    // The migration rebuilds Items and Projects to change their
    // foreign keys. Every row must come through, and the database
    // must still refuse to remove a project that is in use.
    [Fact]
    public void KeepTrackedDependentsOnProjectRemoval_KeepsTheRows()
    {
        using (var old = NewContext())
        {
            old.GetService<IMigrator>().Migrate("ApiTokens");
        }

        Execute(
            """
            INSERT INTO Projects (Id, Name)
            VALUES ('11111111-1111-7111-8111-111111111111', 'House');
            INSERT INTO Projects (Id, Name, ParentId)
            VALUES ('55555555-5555-7555-8555-555555555555', 'Kitchen',
                    '11111111-1111-7111-8111-111111111111');
            INSERT INTO Contexts (Id, Name)
            VALUES ('22222222-2222-7222-8222-222222222222', 'phone');
            INSERT INTO Items
                (Id, Title, Notes, State, Priority, ProjectId,
                 DoNotAutoReschedule, DueDate, DueTime, RecurrenceRule,
                 RecurrenceMode, RecurrenceAnchor, OccurrenceDate)
            VALUES
                ('33333333-3333-7333-8333-333333333333',
                 'Order the worktop', '', 'NextAction', 2,
                 '55555555-5555-7555-8555-555555555555', 0, '2026-11-20',
                 '09:00:00', 'FREQ=WEEKLY;BYDAY=FR', 'FixedSchedule',
                 '2026-11-13', '2026-11-20');
            INSERT INTO ItemContexts (ItemId, ContextId)
            VALUES
                ('33333333-3333-7333-8333-333333333333',
                 '22222222-2222-7222-8222-222222222222');
            INSERT INTO Reminders (Id, "Offset", ItemId)
            VALUES
                ('44444444-4444-7444-8444-444444444444', '-00:30:00',
                 '33333333-3333-7333-8333-333333333333');
            """);

        using (var db = NewContext())
        {
            db.Database.Migrate();

            Assert.Empty(db.Database.GetPendingMigrations());
            Assert.False(db.Database.HasPendingModelChanges());
            var item = db.Items.Single();
            Assert.Equal("Order the worktop", item.Title);
            Assert.Equal(new DateOnly(2026, 11, 20), item.DueDate);
            Assert.Equal(new TimeOnly(9, 0), item.DueTime);
            Assert.Equal(
                "FREQ=WEEKLY;BYDAY=FR",
                item.Recurrence?.Rule.ToString());
            Assert.Equal("phone", Assert.Single(item.Contexts).Name);
            Assert.Single(item.Reminders);
            var projects = db.Projects.ToList();
            Assert.Equal(2, projects.Count);
            Assert.Equal("Kitchen", item.Project?.Name);
            Assert.Equal("House", item.Project?.Parent?.Name);
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check";
            using var reader = command.ExecuteReader();
            Assert.False(reader.Read());
        }

        // The checks and the foreign keys are still there.
        Assert.Throws<SqliteException>(
            () => Execute(
                "UPDATE Items SET RecurrenceMode = NULL"));
        Assert.Throws<SqliteException>(
            () => Execute(
                "DELETE FROM Projects WHERE Name = 'Kitchen'"));
        Assert.Throws<SqliteException>(
            () => Execute(
                "DELETE FROM Projects WHERE Name = 'House'"));
    }
}
