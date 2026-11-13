using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>
/// A throwaway in-memory database with every migration applied, the
/// same way the app applies them when it starts. Each call to
/// <see cref="NewContext"/> returns a context that has loaded
/// nothing, so a test that saves through one and reads through
/// another proves the data went to the database and came back.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection =
        new("Data Source=:memory:;Foreign Keys=True");

    public TestDatabase()
    {
        // An in-memory database lasts as long as its connection.
        _connection.Open();
        using var db = NewContext();
        db.Database.Migrate();
    }

    public NudgeDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<NudgeDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new NudgeDbContext(options);
    }

    /// <summary>Runs SQL directly, going round the entities.</summary>
    public void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The first column of the first row, as SQLite holds
    /// it: text, a whole number, or null.</summary>
    public object? Scalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
