using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Web.Data;

namespace Nudge.Web.Auth;

/// <summary>
/// The <c>token</c> command line, run as
/// <c>dotnet Nudge.Web.dll token ...</c>. It manages API tokens
/// directly in the database and does not start the web server; no
/// HTTP endpoint can make or revoke a token, so a token cannot mint
/// another.
/// </summary>
public static class TokenCommand
{
    public const string Name = "token";

    /// <summary>A problem with the request itself.</summary>
    public const int UsageError = 2;

    /// <summary>The request was understood and refused.</summary>
    public const int Refused = 1;

    private const string Usage =
        """
        Usage:
          token create <name>         Print a new token, once, on stdout.
          token list                  Show every token (never the token).
          token revoke <name-or-id>   Stop accepting a token.
        """;

    /// <summary>Run from <c>Program</c>, with the arguments after
    /// <c>token</c>.</summary>
    public static Task<int> MainAsync(string[] args)
    {
        // The same configuration the web app reads (appsettings,
        // environment variables), without starting it. Command-line
        // arguments are left out: they are this command's.
        var configuration = WebApplication
            .CreateBuilder(new WebApplicationOptions { Args = [] })
            .Configuration;
        return RunAsync(
            args,
            configuration,
            Console.Out,
            Console.Error,
            TimeProvider.System);
    }

    /// <summary>
    /// Opens the database the configuration names, brings it up to
    /// date if it is behind, and carries out the command. Only the
    /// new token, from <c>create</c>, and the list, from
    /// <c>list</c>, go to <paramref name="output"/>; everything else
    /// goes to <paramref name="error"/>.
    /// </summary>
    /// <returns>The exit code: 0, <see cref="Refused"/> or
    /// <see cref="UsageError"/>.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        TimeProvider clock)
    {
        var known = args switch
        {
            ["create", _] or ["list"] or ["revoke", _] => true,
            _ => false,
        };
        if (!known)
        {
            await error.WriteLineAsync(Usage);
            return UsageError;
        }

        var connectionString = DatabaseSetup.ConnectionString(configuration);

        // Said first, so that a token made in the wrong database
        // (with the setting missing, a new nudge.db where the command
        // runs) does not go unnoticed.
        await error.WriteLineAsync(
            "Database: "
            + new SqliteConnectionStringBuilder(connectionString).DataSource);
        var options = new DbContextOptionsBuilder<NudgeDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using var db = new NudgeDbContext(options, clock);

        // On a database the app has already brought up to date, as
        // in production while it runs, nothing is pending and this
        // leaves the database alone. On a new or older one it does
        // what the app's startup does.
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            DatabaseSetup.Prepare(connectionString, db);
        }

        try
        {
            switch (args)
            {
                case ["create", var name]:
                    var (token, row) = await ApiTokens.CreateAsync(
                        db,
                        name,
                        clock.GetUtcNow());
                    await output.WriteLineAsync(token);
                    await error.WriteLineAsync(
                        $"Created token {row.Name} ({row.Id}). It is"
                        + " shown only once, above; changes made with it"
                        + $" are recorded as api:{row.Name}.");
                    return 0;

                case ["list"]:
                    await ListAsync(db, output);
                    return 0;

                case ["revoke", var nameOrId]:
                    var revoked = await ApiTokens.RevokeAsync(
                        db,
                        nameOrId,
                        clock.GetUtcNow());
                    await error.WriteLineAsync(
                        $"Revoked token {revoked.Name} ({revoked.Id}).");
                    return 0;
            }
        }
        catch (TokenException e)
        {
            await error.WriteLineAsync(e.Message);
            return Refused;
        }

        throw new InvalidOperationException("Unreachable.");
    }

    private static async Task ListAsync(
        NudgeDbContext db,
        TextWriter output)
    {
        var tokens = await ApiTokens.ListAsync(db);
        if (tokens.Count == 0)
        {
            await output.WriteLineAsync("No tokens.");
            return;
        }

        var width = Math.Max(4, tokens.Max(token => token.Name.Length));
        await output.WriteLineAsync(
            $"{"ID",-36}  {"NAME".PadRight(width)}"
            + $"  {"CREATED",-20}  REVOKED");
        foreach (var token in tokens)
        {
            var revoked = token.RevokedAt is { } at
                ? ApiTokens.Instant(at)
                : "-";
            await output.WriteLineAsync(
                $"{token.Id,-36}  {token.Name.PadRight(width)}"
                + $"  {ApiTokens.Instant(token.CreatedAt),-20}  {revoked}");
        }
    }
}
