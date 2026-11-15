using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nudge.Web.Data;

/// <summary>
/// Where the database is, and how the app brings it up to date when
/// it starts. The token command line finds the database the same
/// way, but never prepares it.
/// </summary>
public static class DatabaseSetup
{
    public static string ConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("Nudge")
            ?? "Data Source=nudge.db";
    }

    /// <summary>
    /// Creates the database file if there is none, in WAL mode, and
    /// applies every pending migration. If a migration fails this
    /// throws, and whoever called it should stop rather than run
    /// against a schema it does not expect.
    /// </summary>
    public static void Prepare(
        string connectionString,
        NudgeDbContext db)
    {
        // Create the database file up front, in WAL mode, so that
        // Litestream has something to replicate from the first start.
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "PRAGMA journal_mode = WAL;"
                + " PRAGMA application_id = 0x4E554447;";
            command.ExecuteNonQuery();
        }

        // While it migrates, EF Core holds a lock: a row in this table.
        // A process killed at that moment leaves the row behind, and
        // the next start would wait for it for ever. Only the app
        // migrates this database (the token command line refuses one
        // that is not up to date), so a lock found now is such a
        // leftover. EF Core creates the table again when it needs it.
        db.Database.ExecuteSqlRaw(
            "DROP TABLE IF EXISTS \"__EFMigrationsLock\";");
        db.Database.Migrate();
    }
}
