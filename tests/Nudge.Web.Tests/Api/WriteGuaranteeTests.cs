using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nudge.Web.Api;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// What every write endpoint guarantees, checked on each one in
/// <see cref="WriteCatalog"/>, and a check that the catalog has every
/// write endpoint the app maps: it is logged and can be undone, a dry
/// run stores nothing, a retry with its key is answered without
/// being done again, a key cannot be reused for another request, and
/// an unknown query parameter is refused rather than ignored.
/// </summary>
public sealed class WriteGuaranteeTests : IDisposable
{
    private static readonly string[] Writes =
        ["POST", "PUT", "PATCH", "DELETE"];

    private readonly Api _api = new();

    public static IEnumerable<object[]> Names => WriteCatalog.Names;

    public void Dispose()
    {
        _api.Dispose();
    }

    private static bool Succeeded(Reply reply)
    {
        return reply.Status is HttpStatusCode.OK or HttpStatusCode.Created;
    }

    /// <summary>Every endpoint the app maps on the API side, as
    /// method and route, with whether it goes through the write
    /// runner. The web UI's endpoints are checked in
    /// <c>UiEndpointTests</c>.</summary>
    private List<(string Name, bool Runner)> Endpoints()
    {
        return _api.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(EndpointSides.IsApiSide)
            .SelectMany(
                endpoint => (endpoint.Metadata
                        .GetMetadata<HttpMethodMetadata>()
                        ?.HttpMethods ?? ["*"])
                    .Select(
                        method => (
                            $"{method} {endpoint.RoutePattern.RawText}",
                            endpoint.Metadata.GetMetadata<WriteEndpoint>()
                                is not null)))
            .ToList();
    }

    [Fact]
    public void Catalog_HasEveryWriteEndpoint_AndNothingElse()
    {
        var writes = Endpoints()
            .Where(e => !e.Name.StartsWith("GET ", StringComparison.Ordinal))
            .Select(e => e.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            WriteCatalog.Cases
                .Select(c => c.Name)
                .Order(StringComparer.Ordinal),
            writes);
    }

    // A write mapped by hand, without the runner, would have none of
    // the guarantees. Every endpoint that is not a GET carries the
    // runner's mark, and only those do.
    [Fact]
    public void EveryWriteEndpoint_GoesThroughTheRunner()
    {
        foreach (var (name, runner) in Endpoints())
        {
            var method = name[..name.IndexOf(' ', StringComparison.Ordinal)];
            Assert.True(
                runner == Writes.Contains(method),
                $"{name}: {(runner ? "a GET" : "a write")} where"
                + $" {(runner ? "a write" : "a GET")} was expected.");
            Assert.True(
                method == "GET" || Writes.Contains(method),
                $"{name}: unexpected method.");
        }
    }

    /// <summary>Ids made anew each time, as in a dry run and then the
    /// real request, written the same.</summary>
    private static string WithoutIds(string json)
    {
        return Regex.Replace(
            json,
            "[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}",
            "<id>");
    }

    // Undoing the creation of an item, project or context marks it
    // deleted rather than removing the row, so what is compared is
    // what is not deleted.
    [Theory]
    [MemberData(nameof(Names))]
    public async Task Write_IsLoggedAndCanBeUndone(string name)
    {
        var request = await WriteCatalog.Named(name).Arrange(_api);
        var before = _api.Dump(live: true);
        var changes = _api.Count("Changes");

        var reply = await _api.SendAsync(request);

        Assert.True(Succeeded(reply), reply.Body);
        Assert.False(reply.Json["dryRun"]!.GetValue<bool>());
        Assert.NotEqual(before, _api.Dump(live: true));
        Assert.Equal(changes + 1, _api.Count("Changes"));

        // The change the response reports is the one in the log.
        var id = reply.ChangeId;
        var logged = await _api.GetAsync($"/api/changes/{id}");
        Assert.Equal(
            reply.Json["change"]!.ToJsonString(),
            logged.ToJsonString());
        Assert.NotEmpty(logged["entries"]!.AsArray());

        var undo = await _api.SendAsync("POST", $"/api/changes/{id}/undo");

        Assert.Equal(HttpStatusCode.OK, undo.Status);
        Assert.Equal(before, _api.Dump(live: true));
        Assert.Equal(
            id,
            undo.Json["change"]!["undoes"]!.GetValue<long>());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task DryRun_StoresNothing(string name)
    {
        var request = await WriteCatalog.Named(name).Arrange(_api);
        var before = _api.Everything();

        var reply = await _api.SendAsync(request, "dry-run-key", dryRun: true);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.True(reply.Json["dryRun"]!.GetValue<bool>());
        var change = reply.Json["change"]!;
        Assert.Null(change["id"]);
        Assert.NotEmpty(change["entries"]!.AsArray());
        Assert.Equal(before, _api.Everything());

        // And the real request does what the dry run said it would.
        var real = await _api.SendAsync(request);
        Assert.True(Succeeded(real), real.Body);
        Assert.Equal(
            WithoutIds(change["entries"]!.ToJsonString()),
            WithoutIds(real.Json["change"]!["entries"]!.ToJsonString()));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Retry_WithItsKey_IsAnsweredWithoutBeingDoneAgain(
        string name)
    {
        var request = await WriteCatalog.Named(name).Arrange(_api);
        var first = await _api.SendAsync(request, "key-1");
        Assert.True(Succeeded(first), first.Body);
        Assert.False(first.Replayed);
        var after = _api.Everything();

        var retry = await _api.SendAsync(request, "key-1");

        Assert.True(retry.Replayed);
        Assert.Equal(first.Status, retry.Status);
        Assert.Equal(first.ContentType, retry.ContentType);
        Assert.Equal(first.Body, retry.Body);
        Assert.Equal(after, _api.Everything());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Key_UsedForAnotherRequest_IsRefused(string name)
    {
        var request = await WriteCatalog.Named(name).Arrange(_api);
        Assert.True(Succeeded(await _api.SendAsync(request, "key-1")));
        var after = _api.Everything();

        var other = await _api.SendAsync(
            "POST",
            "/api/contexts",
            """{"name":"somewhere else"}""",
            "key-1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.Status);
        Assert.Equal(after, _api.Everything());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task UnknownOrMisspeltQuery_IsRefused(string name)
    {
        var request = await WriteCatalog.Named(name).Arrange(_api);
        var before = _api.Everything();

        foreach (var query in new[]
        {
            "?dry_run=true",
            "?dryrun=true",
            "?DryRun=true",
            "?dryRun=yes",
            "?dryRun=True",
            "?dryRun=1",
            "?dryRun=",
            "?dryRun",
            "?dryRun=true&dryRun=true",
            "?dryRun=true&force=true",
        })
        {
            var reply = await _api.SendAsync(
                request.Method,
                request.Path + query,
                request.Body);

            Assert.True(
                reply.Status == HttpStatusCode.BadRequest,
                $"{query}: {(int)reply.Status} {reply.Body}");
        }

        Assert.Equal(before, _api.Everything());
    }
}
