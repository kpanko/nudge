using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Web;
using Nudge.Web.Api;
using Nudge.Web.Auth;
using Nudge.Web.Data;

// "token create|list|revoke": manage API tokens without starting the
// web server.
if (args is [TokenCommand.Name, .. var tokenArgs])
{
    return await TokenCommand.MainAsync(tokenArgs);
}

var builder = WebApplication.CreateBuilder(args);
var connectionString = DatabaseSetup.ConnectionString(builder.Configuration);
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
builder.Services.AddNudgeAuthentication();
builder.Services.AddNudgeApi();
builder.Services.AddNudgeOpenApi();
var app = builder.Build();
app.Logger.LogInformation("Owner time zone: {Zone}", ownerTimeZone);

// Bring the schema up to date before serving anything. If a
// migration fails the app stops here, where the failure shows,
// rather than running against a schema it does not expect.
using (var scope = app.Services.CreateScope())
{
    DatabaseSetup.Prepare(
        connectionString,
        scope.ServiceProvider.GetRequiredService<NudgeDbContext>());
}

// Anything that goes wrong is answered with problem details (RFC
// 9457), including 404s and 405s from routing.
app.UseExceptionHandler();
app.UseStatusCodePages();

// Every endpoint requires an API token unless it allows anonymous
// requests, as the two below do; see ApiTokenAuthentication.
app.UseAuthentication();
app.UseAuthorization();

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
    })
    // Fly's health check.
    .AllowAnonymous();

app.MapNudgeApi();

// Served in every environment, and without a token: the agent and
// the MCP server (M6) read it, and it describes a public
// repository's API.
app.MapOpenApi(OpenApiSetup.DocumentPath).AllowAnonymous();

app.Run();
return 0;

public partial class Program;
