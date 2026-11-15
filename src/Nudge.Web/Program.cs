using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Web;
using Nudge.Web.Api;
using Nudge.Web.Data;

var builder = WebApplication.CreateBuilder(args);
var connectionString =
    builder.Configuration.GetConnectionString("Nudge")
    ?? "Data Source=nudge.db";
builder.Services.AddDbContext<NudgeDbContext>(
    options => options.UseSqlite(connectionString));

// Checked here, before anything is served: a missing or wrong zone
// stops the app rather than completing items on the wrong day.
var ownerTimeZone = OwnerTimeZone.From(
    builder.Configuration,
    builder.Environment);
builder.Services.AddSingleton(ownerTimeZone);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(
    options => JsonSetup.Apply(options.SerializerOptions));
builder.Services.AddNudgeApi();
var app = builder.Build();
app.Logger.LogInformation("Owner time zone: {Zone}", ownerTimeZone);

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

// Bring the schema up to date before serving anything. If a
// migration fails the app stops here, where the failure shows,
// rather than running against a schema it does not expect.
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider
        .GetRequiredService<NudgeDbContext>()
        .Database;

    // While it migrates, EF Core holds a lock: a row in this table.
    // A process killed at that moment leaves the row behind, and
    // the next start would wait for it for ever. Only one process
    // uses this database, so a lock found now is such a leftover.
    // EF Core creates the table again when it needs it.
    database.ExecuteSqlRaw("DROP TABLE IF EXISTS \"__EFMigrationsLock\";");
    database.Migrate();
}

// Anything that goes wrong is answered with problem details (RFC
// 9457), including 404s and 405s from routing.
app.UseExceptionHandler();
app.UseStatusCodePages();

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

app.MapNudgeApi();

app.Run();

public partial class Program;
