using System.Net;
using Microsoft.Extensions.Logging;
using Nudge.Web.Api;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// Refusals made from an exception are logged with it, because a
/// bug inside Nudge.Core throws the same exception types as its
/// rules do, and would otherwise only ever look like a 400 or 409.
/// </summary>
public sealed class RefusalLogTests : IDisposable
{
    private readonly Api _api = new();

    public void Dispose()
    {
        _api.Dispose();
    }

    private List<LogSink.Entry> Refusals()
    {
        return _api.Logs.Entries
            .Where(e => e.Category == typeof(ApiProblem).FullName)
            .ToList();
    }

    [Fact]
    public async Task DomainRefusal_IsLoggedWithItsException()
    {
        var item = await _api.CreateItemAsync("""{"title":"Post a card"}""");
        var path = $"/api/items/{item["id"]}/complete";
        await _api.WriteAsync("POST", path);

        var reply = await _api.SendAsync("POST", path);

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        var entry = Assert.Single(Refusals());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains(path, entry.Message);
        Assert.Contains("409", entry.Message);
    }

    [Fact]
    public async Task InvalidValue_IsLoggedWithItsException_ButNotTheBody()
    {
        var reply = await _api.SendAsync(
            "POST",
            "/api/items",
            """{"title":"   ","notes":"private-marker"}""");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        var entry = Assert.Single(Refusals());
        Assert.IsAssignableFrom<ArgumentException>(entry.Exception);
        Assert.DoesNotContain(
            _api.Logs.Entries,
            e => e.Message.Contains("private-marker")
                || (e.Exception?.ToString().Contains("private-marker")
                    ?? false));
    }

    [Fact]
    public async Task ContextNameInUse_IsLoggedWithTheDatabasesError()
    {
        await _api.CreateContextAsync("phone");

        var reply = await _api.SendAsync(
            "POST",
            "/api/contexts",
            """{"name":"Phone"}""");

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.IsType<Microsoft.EntityFrameworkCore.DbUpdateException>(
            Assert.Single(Refusals()).Exception);
    }

    // A problem the API finds itself, with no exception behind it,
    // is the client's mistake and is not logged.
    [Fact]
    public async Task ProblemWithoutAnException_IsNotLogged()
    {
        var reply = await _api.SendAsync(
            "POST",
            "/api/items?dry_run=true",
            """{"title":"Post a card"}""");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Empty(Refusals());
    }
}
