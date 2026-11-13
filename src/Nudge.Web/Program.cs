using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var connectionString =
    builder.Configuration.GetConnectionString("Nudge")
    ?? "Data Source=nudge.db";
var app = builder.Build();

// Create the database file up front, in WAL mode, so that
// Litestream has something to replicate from the first start.
using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "PRAGMA journal_mode = WAL; PRAGMA application_id = 0x4E554447;";
    command.ExecuteNonQuery();
}

app.MapGet(
    "/healthz",
    () =>
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        command.ExecuteScalar();
        return Results.Ok(new { status = "ok" });
    });

app.Run();

public partial class Program;
