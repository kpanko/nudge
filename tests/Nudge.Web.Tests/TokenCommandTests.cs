using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nudge.Web.Auth;
using Nudge.Web.Data;

namespace Nudge.Web.Tests;

/// <summary>
/// The <c>token</c> command line, run as the app runs it, on a
/// database file of its own that it creates itself.
/// </summary>
public sealed class TokenCommandTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"nudge-token-test-{Guid.NewGuid():N}.db");

    private readonly FixedClock _clock = new(Now);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // Still in use; the temporary folder gets cleaned.
            }
        }
    }

    private async Task<(int Exit, string Output, string Error)> RunAsync(
        params string[] args)
    {
        // No time zone: the command line does not need one.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Nudge"] = $"Data Source={_path}",
                })
            .Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await TokenCommand.RunAsync(
            args,
            configuration,
            output,
            error,
            _clock);
        return (exit, output.ToString(), error.ToString());
    }

    private async Task<string> CreateAsync(string name)
    {
        var (exit, output, error) = await RunAsync("create", name);
        Assert.True(exit == 0, error);
        return output.TrimEnd('\n', '\r');
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            $"Data Source={_path};Pooling=False");
        connection.Open();
        return connection;
    }

    private object? Scalar(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>Every value in every table, as text.</summary>
    private List<string> EveryValue()
    {
        using var connection = Open();
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var values = new List<string>();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    values.Add($"{reader.GetValue(column)}");
                }
            }
        }

        return values;
    }

    [Fact]
    public async Task Create_OnAFreshDatabase_PrintsOnlyTheToken()
    {
        Assert.False(File.Exists(_path));

        var (exit, output, error) = await RunAsync("create", "planning-agent");

        Assert.Equal(0, exit);
        Assert.Matches(
            new Regex(@"\Anudge_[A-Za-z0-9_-]{43}\r?\n\z"),
            output);
        Assert.StartsWith($"Database: {_path}", error);
        Assert.Contains("planning-agent", error);
        Assert.Contains("api:planning-agent", error);

        // Brought up to date as the app's startup does.
        Assert.True(File.Exists(_path));
        Assert.Equal("wal", Scalar("PRAGMA journal_mode;"));
        var options = new DbContextOptionsBuilder<NudgeDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;
        await using var db = new NudgeDbContext(options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Create_StoresOnlyTheHash()
    {
        var token = await CreateAsync("planning-agent");

        var values = EveryValue();
        Assert.DoesNotContain(values, value => value.Contains(token));
        Assert.DoesNotContain(
            values,
            value => value.Contains(token[ApiTokens.Prefix.Length..]));
        Assert.Contains(ApiTokens.Hash(token), values);
        Assert.Equal(
            Now.UtcDateTime,
            DateTime.Parse(
                (string)Scalar("SELECT CreatedAt FROM ApiTokens")!,
                null,
                System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    [Fact]
    public async Task Create_MakesADifferentTokenEachTime()
    {
        Assert.NotEqual(await CreateAsync("one"), await CreateAsync("two"));
    }

    [Fact]
    public async Task List_ShowsEveryToken_ButNeverATokenOrHash()
    {
        var agent = await CreateAsync("planning-agent");
        _clock.Now = Now.AddHours(1);
        var phone = await CreateAsync("phone");
        _clock.Now = Now.AddHours(2);
        Assert.Equal(0, (await RunAsync("revoke", "phone")).Exit);

        var (exit, output, error) = await RunAsync("list");

        Assert.Equal(0, exit);
        Assert.Equal($"Database: {_path}", error.Trim());
        var lines = output.TrimEnd().Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^ID +NAME +CREATED +REVOKED", lines[0]);
        Assert.Matches(
            @"^[0-9a-f-]{36}  planning-agent  2026-11-14T09:30:00Z +-",
            lines[1]);
        Assert.Matches(
            @"^[0-9a-f-]{36}  phone +2026-11-14T10:30:00Z"
            + @" +2026-11-14T11:30:00Z",
            lines[2]);
        foreach (var token in new[] { agent, phone })
        {
            Assert.DoesNotContain(token, output);
            Assert.DoesNotContain(
                ApiTokens.Hash(token),
                output,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task List_WithNoTokens_SaysSo()
    {
        var (exit, output, _) = await RunAsync("list");

        Assert.Equal(0, exit);
        Assert.Equal("No tokens.", output.Trim());
    }

    [Fact]
    public async Task Revoke_ByName_OrById()
    {
        await CreateAsync("one");
        await CreateAsync("two");
        var id = (string)Scalar("SELECT Id FROM ApiTokens WHERE Name = 'two'")!;

        var byName = await RunAsync("revoke", "one");
        var byId = await RunAsync("revoke", id.ToLowerInvariant());

        Assert.Equal(0, byName.Exit);
        Assert.Equal("", byName.Output);
        Assert.Contains("one", byName.Error);
        Assert.Equal(0, byId.Exit);
        Assert.Contains("two", byId.Error);
        Assert.Equal(
            2L,
            Scalar("SELECT COUNT(*) FROM ApiTokens WHERE RevokedAt IS NOT NULL"));
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_IsRefused()
    {
        await CreateAsync("one");
        await RunAsync("revoke", "one");
        var revokedAt = Scalar("SELECT RevokedAt FROM ApiTokens");
        _clock.Now = Now.AddDays(1);

        var (exit, output, error) = await RunAsync("revoke", "one");

        Assert.Equal(TokenCommand.Refused, exit);
        Assert.Equal("", output);
        Assert.Contains("already revoked", error);
        Assert.Equal(revokedAt, Scalar("SELECT RevokedAt FROM ApiTokens"));
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("0198a3c4-0000-7000-8000-000000000000")]
    public async Task Revoke_UnknownToken_IsRefused(string nameOrId)
    {
        await CreateAsync("one");

        var (exit, output, error) = await RunAsync("revoke", nameOrId);

        Assert.Equal(TokenCommand.Refused, exit);
        Assert.Equal("", output);
        Assert.Contains($"no token with the name or id {nameOrId}", error);
        Assert.Equal(
            0L,
            Scalar("SELECT COUNT(*) FROM ApiTokens WHERE RevokedAt IS NOT NULL"));
    }

    // Names are never reused, so the change log's "api:<name>"
    // always means one token.
    [Theory]
    [InlineData("planning-agent")]
    [InlineData("Planning-Agent")]
    public async Task Create_WithANameInUse_IsRefused(string name)
    {
        await CreateAsync("planning-agent");
        await RunAsync("revoke", "planning-agent");

        var (exit, output, error) = await RunAsync("create", name);

        Assert.Equal(TokenCommand.Refused, exit);
        Assert.Equal("", output);
        Assert.Contains("already exists", error);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ApiTokens"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("slash/name")]
    [InlineData("naïve")]
    [InlineData("api:agent")]
    [InlineData("agent\n")]
    [InlineData("0198a3c4-0000-7000-8000-000000000000")]
    public async Task Create_WithABadName_IsRefused(string name)
    {
        var (exit, output, error) = await RunAsync("create", name);

        Assert.Equal(TokenCommand.Refused, exit);
        Assert.Equal("", output);
        Assert.Contains("name", error);
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM ApiTokens"));
    }

    [Fact]
    public async Task Names_MayBeUpTo64Characters()
    {
        Assert.Equal(0, (await RunAsync("create", new string('a', 64))).Exit);
        Assert.Equal(
            TokenCommand.Refused,
            (await RunAsync("create", new string('b', 65))).Exit);
        Assert.Equal(
            0,
            (await RunAsync("create", "Agent_2.phone-1")).Exit);
    }

    [Theory]
    [InlineData]
    [InlineData("create")]
    [InlineData("create", "a", "b")]
    [InlineData("list", "all")]
    [InlineData("revoke")]
    [InlineData("delete", "one")]
    public async Task Usage_IsShownForAnythingElse(params string[] args)
    {
        var (exit, output, error) = await RunAsync(args);

        Assert.Equal(TokenCommand.UsageError, exit);
        Assert.Equal("", output);
        Assert.Contains("Usage:", error);
    }

    // Tokens are security state, not the owner's data.
    [Fact]
    public async Task CreatingAndRevoking_RecordsNoChange()
    {
        await CreateAsync("one");
        await RunAsync("revoke", "one");

        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM Changes"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM ChangeEntries"));
    }
}
