using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>
/// Deleting marks a row instead of removing it, and a marked row
/// stays out of sight unless it is asked for.
/// </summary>
public sealed class SoftDeleteTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 13, 17, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Earlier = Now.AddDays(-1);

    /// <summary><see cref="Now"/> as the database writes it.</summary>
    private const string NowText = "2026-11-13T17:00:00.0000000Z";

    private readonly TestDatabase _database =
        new() { Clock = new FixedClock(Now) };

    public void Dispose()
    {
        _database.Dispose();
    }

    private void Save(params Item[] items)
    {
        using var db = _database.NewContext();
        db.Items.AddRange(items);
        db.SaveChanges();
    }

    /// <summary>Does something in a context of its own and saves.
    /// </summary>
    private void Do(Action<NudgeDbContext> work)
    {
        using var db = _database.NewContext();
        work(db);
        db.SaveChanges();
    }

    [Fact]
    public void DeletedItem_IsKept_ButLeftOutOfEveryQuery()
    {
        var item = new Item("Order the worktop")
        {
            Project = new Project("House"),
        };
        item.AddContext(new Context("phone"));
        item.AddReminder(Reminder.FromDue(TimeSpan.Zero));
        Save(item, new Item("Buy a ladder"));

        Do(db => db.Items.Single(i => i.Id == item.Id).Delete(Now));

        using var read = _database.NewContext();
        Assert.Equal(
            ["Buy a ladder"],
            read.Items.Select(i => i.Title).ToList());
        Assert.Equal(1, read.Items.Count());
        Assert.False(read.Items.Any(i => i.Id == item.Id));
        Assert.Null(read.Items.Find(item.Id));
        Assert.Null(read.Items.SingleOrDefault(i => i.Id == item.Id));

        // It is all still there for whoever asks for deleted items.
        var deleted = read.Items
            .IgnoreQueryFilters()
            .Single(i => i.DeletedAt != null);
        Assert.Equal(item.Id, deleted.Id);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(Now, deleted.DeletedAt);
        Assert.Equal("House", deleted.Project?.Name);
        Assert.Equal("phone", Assert.Single(deleted.Contexts).Name);
        Assert.Single(deleted.Reminders);
        Assert.Equal(
            NowText,
            _database.Scalar(
                "SELECT DeletedAt FROM Items WHERE DeletedAt IS NOT NULL"));

        // And the deletion is one changed column in the log.
        var entry = Assert.Single(_database.Changes().Last().Entries);
        Assert.Equal(ChangeKind.Changed, entry.Kind);
        Assert.Equal("""{"DeletedAt":null}""", entry.Before);
        Assert.Equal($$"""{"DeletedAt":"{{NowText}}"}""", entry.After);
    }

    [Fact]
    public void DeletedProjectOrContext_IsLeftOutOfQueries()
    {
        var house = new Project("House");
        var phone = new Context("phone");
        Do(
            db =>
            {
                db.Projects.AddRange(house, new Project("Garden"));
                db.Contexts.AddRange(phone, new Context("errands"));
            });

        Do(
            db =>
            {
                db.Projects.Single(p => p.Id == house.Id).Delete(Now);
                db.Contexts.Single(c => c.Id == phone.Id).Delete(Now);
            });

        using var read = _database.NewContext();
        Assert.Equal("Garden", read.Projects.Single().Name);
        Assert.Null(read.Projects.Find(house.Id));
        Assert.Equal("errands", read.Contexts.Single().Name);
        Assert.Null(read.Contexts.Find(phone.Id));
        Assert.Equal(
            2,
            read.Projects.IgnoreQueryFilters().Count());
        Assert.Equal(
            Now,
            read.Contexts
                .IgnoreQueryFilters()
                .Single(c => c.Id == phone.Id)
                .DeletedAt);
    }

    [Fact]
    public void DeletedContext_ComesOffItsItems_ButTheLinkIsKept()
    {
        var item = new Item("Call the plumber");
        item.AddContext(new Context("phone"));
        item.AddContext(new Context("errands"));
        Save(item);

        Do(db => db.Contexts.Single(c => c.Name == "phone").Delete(Now));

        using (var read = _database.NewContext())
        {
            Assert.Equal(
                "errands",
                Assert.Single(read.Items.Single().Contexts).Name);
        }

        // Saving the item, even taking its other context off, does
        // not touch the link it cannot see. Taking the deletion back
        // puts the context on the item again.
        Do(
            db =>
            {
                var loaded = db.Items.Single();
                loaded.Notes = "Before nine.";
                loaded.RemoveContext(loaded.Contexts.Single());
            });
        Assert.Equal(
            "phone",
            _database.Scalar(
                """
                SELECT c.Name FROM ItemContexts ic
                JOIN Contexts c ON c.Id = ic.ContextId
                """));
    }

    [Fact]
    public void DeletedContext_DoesNotHoldOnToItsName()
    {
        Do(db => db.Contexts.Add(new Context("phone")));
        Do(db => db.Contexts.Single().Delete(Now));

        Do(db => db.Contexts.Add(new Context("Phone")));

        using var read = _database.NewContext();
        Assert.Equal("Phone", read.Contexts.Single().Name);
        Assert.Equal(2, read.Contexts.IgnoreQueryFilters().Count());

        // Among contexts that are not deleted, names stay unique.
        read.Contexts.Add(new Context("PHONE"));
        Assert.Throws<DbUpdateException>(() => read.SaveChanges());
    }

    [Fact]
    public async Task DeleteProject_TakesEverythingUnderIt()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        var tiling = new Project("Tiling", kitchen);
        var garden = new Project("Garden");
        var oldJob = new Item("Strip the wallpaper") { Project = kitchen };
        Save(
            new Item("Paint the hall") { Project = house },
            new Item("Order the worktop") { Project = kitchen },
            new Item("Choose the tiles") { Project = tiling },
            new Item("Fix the gate") { Project = garden },
            new Item("Buy a ladder"),
            oldJob);
        Do(db => db.Items.Single(i => i.Id == oldJob.Id).Delete(Earlier));
        var changesBefore = _database.Changes().Count;

        await using (var db = _database.NewContext())
        {
            Assert.True(await db.DeleteProjectAsync(house.Id, Now));
            await db.SaveChangesAsync();
        }

        using var read = _database.NewContext();
        Assert.Equal("Garden", read.Projects.Single().Name);
        Assert.Equal(
            ["Buy a ladder", "Fix the gate"],
            read.Items.Select(i => i.Title).Order().ToList());
        foreach (var table in new[] { "Projects", "Items" })
        {
            Assert.Equal(
                3L,
                _database.Scalar(
                    $"SELECT COUNT(*) FROM {table}"
                    + $" WHERE DeletedAt = '{NowText}'"));
        }

        // What was deleted before keeps the time it was deleted at:
        // it did not go with the project and will not come back
        // with it.
        Assert.Equal(
            Earlier,
            read.Items
                .IgnoreQueryFilters()
                .Single(i => i.Id == oldJob.Id)
                .DeletedAt);

        // All of it is one change, so one undo.
        var changes = _database.Changes();
        Assert.Equal(changesBefore + 1, changes.Count);
        Assert.Equal(6, changes.Last().Entries.Count);
        Assert.All(
            changes.Last().Entries,
            entry =>
            {
                Assert.Equal(ChangeKind.Changed, entry.Kind);
                Assert.Equal(
                    $$"""{"DeletedAt":"{{NowText}}"}""",
                    entry.After);
            });
    }

    [Fact]
    public async Task DeleteProject_ThatIsNotThere_DoesNothing()
    {
        var house = new Project("House");
        Do(db => db.Projects.Add(house));
        Do(db => db.Projects.Single().Delete(Earlier));

        await using var db = _database.NewContext();

        Assert.False(await db.DeleteProjectAsync(Guid.NewGuid(), Now));
        Assert.False(await db.DeleteProjectAsync(house.Id, Now));
        Assert.False(db.ChangeTracker.HasChanges());
    }
}
