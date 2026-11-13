using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>What starting the real app does to its database.</summary>
public sealed class StartupTests(NudgeWebFactory factory)
    : IClassFixture<NudgeWebFactory>
{
    /// <summary>Runs SQL on the app's database file and returns the
    /// first value it gives back, if any.</summary>
    private object? Sql(string sql)
    {
        // Starts the app if it is not running yet.
        _ = factory.Services;

        using var connection = new SqliteConnection(
            $"Data Source={factory.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public void Startup_BringsTheDatabaseUpToDate()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();

        Assert.True(File.Exists(factory.DatabasePath));
        Assert.NotEmpty(db.Database.GetAppliedMigrations());
        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.Empty(db.Items.ToList());
    }

    [Fact]
    public void Startup_AgainOnTheSameDatabase_ChangesNothing()
    {
        using var first = factory.Services.CreateScope();
        var applied = first.ServiceProvider
            .GetRequiredService<NudgeDbContext>()
            .Database
            .GetAppliedMigrations()
            .ToList();

        using var restarted = factory.WithWebHostBuilder(_ => { });
        using var second = restarted.Services.CreateScope();

        Assert.Equal(
            applied,
            second.ServiceProvider
                .GetRequiredService<NudgeDbContext>()
                .Database
                .GetAppliedMigrations());
    }

    // Litestream only replicates a database in WAL mode.
    [Fact]
    public void Database_IsInWalMode()
    {
        Assert.Equal("wal", Sql("PRAGMA journal_mode;"));
        Assert.Equal(0x4E554447L, Sql("PRAGMA application_id;"));
    }

    // EF Core waits for ever for a migration lock that a killed
    // process left behind, and has no setting to stop it. If this
    // test times out, startup no longer clears that lock: look at
    // how the EF Core version in use holds it.
    [Fact]
    public async Task Startup_IsNotHeldUpByAnAbandonedMigrationLock()
    {
        Sql(
            """
            INSERT INTO "__EFMigrationsLock" ("Id", "Timestamp")
            VALUES (1, '2026-11-13 00:00:00')
            """);

        try
        {
            using var restarted = factory.WithWebHostBuilder(_ => { });
            var start = Task.Run(() => restarted.Services);

            await start.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(
                0L,
                Sql("""SELECT COUNT(*) FROM "__EFMigrationsLock";"""));
        }
        finally
        {
            // Do not leave the lock for the tests that follow.
            Sql("""DELETE FROM "__EFMigrationsLock";""");
        }
    }

    // Without this SQLite accepts rows that point at nothing.
    [Fact]
    public void App_EnforcesForeignKeys()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        Assert.Equal(1L, command.ExecuteScalar());
    }
}
