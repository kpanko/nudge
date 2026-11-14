using System.Text;
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

    /// <summary>What the contexts take the time from. The time is
    /// only used to date entries in the change log.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public NudgeDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<NudgeDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Saving is refused until the context knows who is saving.
        return new NudgeDbContext(options, Clock) { Source = "test" };
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

    /// <summary>One column of every row a query returns, as text.
    /// </summary>
    public List<string> Strings(
        string sql,
        int column = 0)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(column));
        }

        return values;
    }

    /// <summary>The tables that hold the owner's data: every table
    /// but the change log and the ones EF Core and SQLite keep for
    /// themselves.</summary>
    public List<string> DataTables()
    {
        return Strings(
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table'
              AND name NOT IN ('Changes', 'ChangeEntries')
              AND name NOT LIKE '\_\_EF%' ESCAPE '\'
              AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
            ORDER BY name
            """);
    }

    public List<string> Columns(string table)
    {
        return Strings($"PRAGMA table_info(\"{table}\")", 1);
    }

    /// <summary>The change log, oldest first.</summary>
    public List<Change> Changes()
    {
        using var db = NewContext();
        return db.Changes.OrderBy(change => change.Id).ToList();
    }

    /// <summary>
    /// Every row of every table that holds the owner's data, as
    /// text. Two dumps are equal exactly when those tables hold the
    /// same thing; the change log is not part of it.
    /// </summary>
    public string Dump()
    {
        var text = new StringBuilder();
        foreach (var table in DataTables())
        {
            var rows = new List<string>();
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new StringBuilder(table);
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    row.Append(" | ")
                        .Append(reader.GetName(column))
                        .Append('=')
                        .Append(
                            reader.IsDBNull(column)
                                ? "NULL"
                                : reader.GetValue(column));
                }

                rows.Add(row.ToString());
            }

            rows.Sort(StringComparer.Ordinal);
            rows.ForEach(row => text.AppendLine(row));
        }

        return text.ToString();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
