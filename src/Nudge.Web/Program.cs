using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Web;
using Nudge.Web.Api;
using Nudge.Web.Auth;
using Nudge.Web.Components;
using Nudge.Web.Data;

// "token create|list|revoke": manage API tokens without starting the
// web server.
if (args is [TokenCommand.Name, .. var tokenArgs])
{
    return await TokenCommand.MainAsync(tokenArgs);
}

var builder = WebApplication.CreateBuilder(args);
var connectionString = DatabaseSetup.ConnectionString(builder.Configuration);

// The request log writes every URL with its query string, where a
// token sent in the wrong place would end up. It is kept at Warning
// here, after the settings files are read, so that no settings file
// (or the lack of one) can turn it on.
builder.Logging.AddFilter(
    "Microsoft.AspNetCore.Hosting.Diagnostics",
    LogLevel.Warning);
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
builder.Services.AddNudgeWebUi(
    builder.Configuration,
    OwnerSignIn.From(builder.Configuration));
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

// Read first: whether the request came in over HTTPS (see
// AddNudgeWebUi).
app.UseForwardedHeaders();

// Anything that goes wrong is answered with problem details (RFC
// 9457), including 404s and 405s from routing, on the API side; the
// web UI shows a page of its own for a path that is not there.
app.UseExceptionHandler();
app.UseWhen(
    context => !WebUi.IsUiPath(context.Request.Path),
    api => api.UseStatusCodePages());
app.UseWhen(
    context => WebUi.IsUiPath(context.Request.Path),
    ui => ui.UseStatusCodePagesWithReExecute(
        WebUi.NotFoundPath,
        createScopeForStatusCodePages: true));
app.UseRouting();
app.UseUiNotFound();

// Every endpoint requires an API token unless it allows anonymous
// requests, as the two below do; see ApiTokenAuthentication. The
// web UI's endpoints ask for the owner's sign-in instead; see
// OwnerAuthentication.
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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

app.MapNudgeWebUi();

app.Run();
return 0;

public partial class Program;
