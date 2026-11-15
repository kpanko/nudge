using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// The real app on a database of its own, with a fixed clock, and
/// ways to call it and to look at what it stored. Each test makes
/// its own, so tests share nothing.
/// </summary>
internal sealed class Api : IDisposable
{
    /// <summary>Midday in New York on Friday 13 November 2026.</summary>
    public static readonly DateTimeOffset Start =
        new(2026, 11, 13, 17, 0, 0, TimeSpan.Zero);

    public Api()
    {
        Factory = new NudgeWebFactory { Clock = Clock };
        Client = Factory.CreateClient();
    }

    public FixedClock Clock { get; } = new(Start);

    public NudgeWebFactory Factory { get; }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    public Task<Reply> SendAsync(Request request)
    {
        return SendAsync(
            request.Method,
            request.Path,
            request.Body,
            null);
    }

    public async Task<Reply> SendAsync(
        string method,
        string path,
        string? body = null,
        string? key = null,
        string contentType = "application/json")
    {
        using var message = new HttpRequestMessage(
            new HttpMethod(method),
            path);
        if (body is not null)
        {
            message.Content = new StringContent(body, Encoding.UTF8);
            message.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse(contentType);
        }

        if (key is not null)
        {
            message.Headers.Add("Idempotency-Key", key);
        }

        using var response = await Client.SendAsync(message);
        return new Reply(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            response.Content.Headers.ContentType?.ToString(),
            response.Headers.TryGetValues(
                "Idempotent-Replayed",
                out var replayed)
                && replayed.Single() == "true");
    }

    public Task<Reply> SendAsync(
        Request request,
        string? key,
        bool dryRun = false)
    {
        return SendAsync(
            request.Method,
            dryRun ? request.Path + "?dryRun=true" : request.Path,
            request.Body,
            key);
    }

    /// <summary>A GET that must succeed, as JSON.</summary>
    public async Task<JsonNode> GetAsync(string path)
    {
        var reply = await SendAsync("GET", path);
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        return reply.Json;
    }

    /// <summary>A write that must succeed; returns the data.</summary>
    public async Task<JsonNode> WriteAsync(
        string method,
        string path,
        string? body = null)
    {
        var reply = await SendAsync(method, path, body);
        Assert.True(
            reply.Status is HttpStatusCode.OK or HttpStatusCode.Created,
            $"{method} {path}: {(int)reply.Status} {reply.Body}");
        return reply.Json["data"]!;
    }

    public async Task<string> CreateContextAsync(string name)
    {
        var data = await WriteAsync(
            "POST",
            "/api/contexts",
            $$"""{"name":"{{name}}"}""");
        return data["id"]!.GetValue<string>();
    }

    public async Task<string> CreateProjectAsync(
        string name,
        string? parentId = null)
    {
        var parent = parentId is null ? "null" : $"\"{parentId}\"";
        var data = await WriteAsync(
            "POST",
            "/api/projects",
            $$"""{"name":"{{name}}","parentId":{{parent}}}""");
        return data["id"]!.GetValue<string>();
    }

    public async Task<JsonNode> CreateItemAsync(string body)
    {
        return await WriteAsync("POST", "/api/items", body);
    }

    /// <summary>
    /// A weekly item, due on Fridays, with a reminder half an hour
    /// before and one at a fixed instant, first due on Friday 20
    /// November at 14:30.
    /// </summary>
    public Task<JsonNode> CreateWeeklyAsync(string title = "Water the plants")
    {
        return CreateItemAsync(
            $$"""
            {
              "title": "{{title}}",
              "due": { "date": "2026-11-20", "time": "14:30:00" },
              "repeat": { "rule": "FREQ=WEEKLY;BYDAY=FR",
                          "mode": "fixedSchedule" },
              "reminders": [
                { "offsetMinutes": -30 },
                { "at": "2026-11-19T14:00:00Z" }
              ]
            }
            """);
    }

    /// <summary>
    /// Every row of every table that holds the owner's data, as
    /// text, as <see cref="TestDatabase.Dump"/> gives it for an
    /// in-memory database. With <paramref name="live"/>, rows
    /// marked deleted are left out.
    /// </summary>
    public string Dump(bool live = false)
    {
        using var connection = Open();
        var tables = Strings(
            connection,
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table'
              AND name NOT IN
                ('Changes', 'ChangeEntries', 'IdempotencyRecords')
              AND name NOT LIKE '\_\_EF%' ESCAPE '\'
              AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
            ORDER BY name
            """);
        var text = new StringBuilder();
        foreach (var table in tables)
        {
            var rows = new List<string>();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (live && Deleted(reader))
                {
                    continue;
                }

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

    private static bool Deleted(SqliteDataReader reader)
    {
        for (var column = 0; column < reader.FieldCount; column++)
        {
            if (reader.GetName(column) == "DeletedAt")
            {
                return !reader.IsDBNull(column);
            }
        }

        return false;
    }

    /// <summary>The number of rows in a table.</summary>
    public long Count(string table)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Everything the database holds, change log and
    /// idempotency records included.</summary>
    public string Everything()
    {
        return Dump()
            + $"Changes={Count("Changes")}"
            + $" ChangeEntries={Count("ChangeEntries")}"
            + $" IdempotencyRecords={Count("IdempotencyRecords")}"
            + $" sequence={Sequence()}";
    }

    private object? Sequence()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT seq FROM sqlite_sequence WHERE name = 'Changes'";
        return command.ExecuteScalar();
    }

    private SqliteConnection Open()
    {
        // Make sure the app has started and created the file.
        _ = Factory.Services;
        var connection = new SqliteConnection(
            $"Data Source={Factory.DatabasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static List<string> Strings(
        SqliteConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}

/// <summary>A request to send.</summary>
internal sealed record Request(
    string Method,
    string Path,
    string? Body = null);

/// <summary>What came back.</summary>
internal sealed record Reply(
    HttpStatusCode Status,
    string Body,
    string? ContentType,
    bool Replayed)
{
    public JsonNode Json => JsonNode.Parse(Body)!;

    /// <summary>The number of the change a write made.</summary>
    public long ChangeId => Json["change"]!["id"]!.GetValue<long>();

    /// <summary>The first error a 400 gives for a field.</summary>
    public string Error(string field)
    {
        var errors = Json["errors"]?[field]
            ?? throw new Xunit.Sdk.XunitException(
                $"No error for {field}: {Body}");
        return errors[0]!.GetValue<string>();
    }
}
