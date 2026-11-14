using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>
/// Undoing inside a transaction the caller holds, as the API does so
/// that it can roll a dry run back, and the record of idempotent
/// requests, which the change log leaves alone.
/// </summary>
public sealed class UndoWithinTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    private long Do(Action<NudgeDbContext> work)
    {
        using var db = _database.NewContext();
        work(db);
        db.SaveChanges();
        return db.LastChange!.Id;
    }

    private static IdempotencyRecord Record(
        string key,
        long? changeId)
    {
        return new IdempotencyRecord(
            key,
            "POST",
            "/api/items",
            "fingerprint",
            201,
            "{}",
            changeId,
            DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task UndoWithin_Committed_IsAnUndoLikeAnyOther()
    {
        var item = new Item("Buy a ladder");
        var change = Do(db => db.Items.Add(item));

        await using var db = _database.NewContext();
        await using var transaction = await db.Database
            .BeginTransactionAsync();
        var undo = await db.UndoWithinAsync(change);
        await transaction.CommitAsync();

        Assert.Equal(change, undo!.Id - 1);
        Assert.Equal(change, undo.Undoes);
        await using var check = _database.NewContext();
        Assert.Empty(check.Items);
    }

    [Fact]
    public async Task UndoWithin_RolledBack_LeavesNoTrace()
    {
        var item = new Item("Buy a ladder");
        var change = Do(db => db.Items.Add(item));
        var before = _database.Dump();

        await using (var db = _database.NewContext())
        {
            await using var transaction = await db.Database
                .BeginTransactionAsync();
            Assert.NotNull(await db.UndoWithinAsync(change));
            await transaction.RollbackAsync();
        }

        Assert.Equal(before, _database.Dump());
        Assert.Single(_database.Changes());
    }

    // Refused after it has written: taking back the project would
    // leave the item that was put in it since in a deleted project.
    [Fact]
    public async Task UndoWithin_Refused_LeavesTheTransactionUsable()
    {
        var project = new Project("House");
        var change = Do(db => db.Projects.Add(project));
        Do(
            db => db.Items.Add(
                new Item("Order the worktop")
                {
                    Project = db.Projects.Single(),
                }));

        await using var db = _database.NewContext();
        await using var transaction = await db.Database
            .BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => db.UndoWithinAsync(change));
        Assert.Contains("project is", error.Message);

        // What the transaction goes on to do is kept, and nothing of
        // the undo is.
        db.Contexts.Add(new Context("phone"));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Equal(3, _database.Changes().Count);
        Assert.Null(_database.Scalar("SELECT DeletedAt FROM Projects"));
        Assert.Equal(
            1L,
            _database.Scalar("SELECT COUNT(*) FROM Contexts"));
    }

    [Fact]
    public async Task UndoWithin_NeedsATransaction()
    {
        var change = Do(db => db.Items.Add(new Item("Buy a ladder")));

        await using var db = _database.NewContext();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.UndoWithinAsync(change));

        Assert.Contains("transaction", error.Message);
    }

    [Fact]
    public void LastChange_IsTheChangeTheLastSaveRecorded()
    {
        using var db = _database.NewContext();
        Assert.Null(db.LastChange);

        db.Items.Add(new Item("Buy a ladder"));
        db.SaveChanges();
        var first = db.LastChange;

        // A save that changes nothing records nothing, and the last
        // change stays the one before.
        db.SaveChanges();

        Assert.NotNull(first);
        Assert.Same(first, db.LastChange);
        Assert.Equal(_database.Changes().Single().Id, first.Id);
    }

    [Fact]
    public void IdempotencyRecord_IsNotInTheChangeLog()
    {
        using (var db = _database.NewContext())
        {
            db.IdempotencyRecords.Add(Record("key-1", null));
            db.SaveChanges();
        }

        Assert.Empty(_database.Changes());
        Assert.Equal(
            1L,
            _database.Scalar("SELECT COUNT(*) FROM IdempotencyRecords"));
    }

    [Fact]
    public async Task Undo_LeavesTheRecordOfTheRequestInPlace()
    {
        long change;
        using (var db = _database.NewContext())
        {
            db.Items.Add(new Item("Buy a ladder"));
            db.SaveChanges();
            change = db.LastChange!.Id;
            db.IdempotencyRecords.Add(Record("key-1", change));
            db.SaveChanges();
        }

        Assert.Single(Assert.Single(_database.Changes()).Entries);
        await using (var db = _database.NewContext())
        {
            Assert.NotNull(await db.UndoAsync(change));
        }

        Assert.Equal(
            change,
            _database.Scalar(
                "SELECT ChangeId FROM IdempotencyRecords"
                + " WHERE Key = 'key-1'"));
    }

    [Fact]
    public async Task Undo_OfAnEntryNamingTheRecordTable_IsRefused()
    {
        var change = Do(db => db.Items.Add(new Item("Buy a ladder")));
        _database.Execute(
            """
            UPDATE ChangeEntries SET TableName = 'IdempotencyRecords'
            """);

        await using var db = _database.NewContext();
        var error = await Assert.ThrowsAsync<UndoRefusedException>(
            () => db.UndoAsync(change));

        Assert.Contains("no longer has", error.Message);
    }
}
