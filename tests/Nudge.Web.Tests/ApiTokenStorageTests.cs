using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Auth;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>
/// Token rows are kept out of the change log and out of undo, like
/// the record of idempotent requests.
/// </summary>
public sealed class ApiTokenStorageTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    // Saving a token needs no Source, because nothing is recorded:
    // were it recorded, this save would be refused for want of one.
    [Fact]
    public async Task CreatingAndRevoking_RecordsNoChange()
    {
        await using (var db = _database.NewContext())
        {
            db.Source = null;
            await ApiTokens.CreateAsync(db, "agent", Now);
        }

        await using (var db = _database.NewContext())
        {
            db.Source = null;
            await ApiTokens.RevokeAsync(db, "agent", Now.AddHours(1));
        }

        Assert.Empty(_database.Changes());
        Assert.Equal(
            1L,
            _database.Scalar(
                "SELECT COUNT(*) FROM ApiTokens WHERE RevokedAt IS NOT NULL"));
    }

    // A save that writes the owner's data and a token in one go logs
    // the data and not the token.
    [Fact]
    public async Task SavedAlongsideData_TheTokenIsLeftOut()
    {
        await using var db = _database.NewContext();
        db.Contexts.Add(new Context("phone"));
        var (token, _) = await ApiTokens.CreateAsync(db, "agent", Now);

        var change = Assert.Single(_database.Changes());
        var entry = Assert.Single(change.Entries);
        Assert.Equal("Contexts", entry.TableName);
        Assert.DoesNotContain(ApiTokens.Hash(token), entry.After);
    }

    [Fact]
    public async Task Undo_OfAnEntryNamingTheTokenTable_IsRefused()
    {
        await using (var db = _database.NewContext())
        {
            await ApiTokens.CreateAsync(db, "agent", Now);
            await ApiTokens.RevokeAsync(db, "agent", Now);
            db.Contexts.Add(new Context("phone"));
            await db.SaveChangesAsync();
        }

        // A forged entry that would put the token back as active.
        var id = _database.Scalar("SELECT Id FROM ApiTokens");
        _database.Execute(
            $$"""
            UPDATE ChangeEntries
            SET TableName = 'ApiTokens',
                RowKey = '{"Id":"{{id}}"}',
                Kind = 'Changed',
                Before = '{"RevokedAt":null}',
                After = '{"RevokedAt":"2026-11-14T09:30:00.0000000Z"}'
            """);

        await using var undo = _database.NewContext();
        var change = _database.Changes().Single().Id;
        await Assert.ThrowsAsync<UndoRefusedException>(
            () => undo.UndoAsync(change));
        Assert.NotNull(_database.Scalar("SELECT RevokedAt FROM ApiTokens"));
    }

    [Fact]
    public async Task FindActive_FindsOnlyAnActiveToken()
    {
        await using var db = _database.NewContext();
        var (token, row) = await ApiTokens.CreateAsync(db, "agent", Now);

        Assert.Equal((row.Id, "agent"), await ApiTokens.FindActiveAsync(db, token));
        Assert.Null(await ApiTokens.FindActiveAsync(db, token + " "));
        Assert.Null(
            await ApiTokens.FindActiveAsync(db, ApiTokens.Hash(token)));

        await ApiTokens.RevokeAsync(db, "agent", Now);
        Assert.Null(await ApiTokens.FindActiveAsync(db, token));
    }

    [Fact]
    public async Task Names_AreUniqueInTheDatabaseToo()
    {
        await using (var db = _database.NewContext())
        {
            await ApiTokens.CreateAsync(db, "agent", Now);
        }

        await using var other = _database.NewContext();
        other.ApiTokens.Add(new ApiToken("AGENT", new string('0', 64), Now));
        await Assert.ThrowsAsync<DbUpdateException>(
            () => other.SaveChangesAsync());
    }
}
