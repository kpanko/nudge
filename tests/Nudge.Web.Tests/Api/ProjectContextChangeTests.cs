using System.Net;

namespace Nudge.Web.Tests.Api;

/// <summary>Projects, contexts and the change log through HTTP.
/// </summary>
public sealed class ProjectContextChangeTests : IDisposable
{
    private readonly Api _api = new();

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task Project_MovedUnderItsOwnDescendant_IsRefused()
    {
        var house = await _api.CreateProjectAsync("House");
        var kitchen = await _api.CreateProjectAsync("Kitchen", house);
        var cupboards = await _api.CreateProjectAsync("Cupboards", kitchen);
        var before = _api.Everything();

        foreach (var parent in new[] { cupboards, kitchen, house })
        {
            var reply = await _api.SendAsync(
                "PATCH",
                $"/api/projects/{house}",
                $$"""{"parentId":"{{parent}}"}""");

            Assert.Equal(HttpStatusCode.Conflict, reply.Status);
            Assert.Contains("under itself", reply.Body);
        }

        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Project_MovesToTheTopLevel_WithNull()
    {
        var house = await _api.CreateProjectAsync("House");
        var kitchen = await _api.CreateProjectAsync("Kitchen", house);

        var moved = await _api.WriteAsync(
            "PATCH",
            $"/api/projects/{kitchen}",
            """{"parentId":null}""");

        Assert.Null(moved["parentId"]);
        Assert.Equal("Kitchen", moved["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProjectDelete_TakesEverythingUnderIt_InOneChange()
    {
        var house = await WriteCatalog.HouseWithContentsAsync(_api);

        var reply = await _api.SendAsync("DELETE", $"/api/projects/{house}");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var projects = await _api.GetAsync("/api/projects");
        Assert.Equal(
            "Garden",
            Assert.Single(projects.AsArray())!["name"]!.GetValue<string>());
        var items = await _api.GetAsync("/api/items");
        Assert.Equal(
            "Prune the roses",
            Assert.Single(items.AsArray())!["title"]!.GetValue<string>());
        Assert.Equal(4, reply.Json["change"]!["entries"]!.AsArray().Count);
    }

    // What a dry run reports for a delete that cascades is exactly
    // what the real delete then logs.
    [Fact]
    public async Task ProjectDelete_DryRun_ReportsTheEntriesTheDeleteLogs()
    {
        var house = await WriteCatalog.HouseWithContentsAsync(_api);

        var dry = await _api.SendAsync(
            "DELETE",
            $"/api/projects/{house}?dryRun=true");
        var real = await _api.SendAsync("DELETE", $"/api/projects/{house}");

        Assert.True(dry.Json["dryRun"]!.GetValue<bool>());
        Assert.Equal(
            dry.Json["change"]!["entries"]!.ToJsonString(),
            real.Json["change"]!["entries"]!.ToJsonString());
        Assert.Equal(
            real.Json["change"]!.ToJsonString(),
            (await _api.GetAsync($"/api/changes/{real.ChangeId}"))
                .ToJsonString());
    }

    [Fact]
    public async Task Project_WithAParentThatDoesNotExist_IsRefused()
    {
        var reply = await _api.SendAsync(
            "POST",
            "/api/projects",
            """{"name":"Kitchen","parentId":"0199a000-0000-7000-8000-000000000000"}""");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.NotEmpty(reply.Error("parentId"));
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("PHONE")]
    [InlineData(" Phone ")]
    public async Task Context_WithANameInUse_IsRefused(string name)
    {
        await _api.CreateContextAsync("phone");
        var errands = await _api.CreateContextAsync("errands");
        var before = _api.Everything();

        var created = await _api.SendAsync(
            "POST",
            "/api/contexts",
            $$"""{"name":"{{name}}"}""");
        var renamed = await _api.SendAsync(
            "PATCH",
            $"/api/contexts/{errands}",
            $$"""{"name":"{{name}}"}""");

        foreach (var reply in new[] { created, renamed })
        {
            Assert.Equal(HttpStatusCode.Conflict, reply.Status);
            Assert.Contains("already has this name", reply.Body);
        }

        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Context_Deleted_ComesOffItsItems_AndUndoPutsItBack()
    {
        var phone = await _api.CreateContextAsync("phone");
        var item = await _api.CreateItemAsync(
            $$"""{"title":"Call the plumber","contextIds":["{{phone}}"]}""");
        var id = item["id"]!.GetValue<string>();

        var deleted = await _api.SendAsync("DELETE", $"/api/contexts/{phone}");

        Assert.Empty(
            (await _api.GetAsync($"/api/items/{id}"))["contextIds"]!.AsArray());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _api.SendAsync("GET", $"/api/contexts/{phone}")).Status);

        await _api.WriteAsync("POST", $"/api/changes/{deleted.ChangeId}/undo");

        Assert.Equal(
            phone,
            (await _api.GetAsync($"/api/items/{id}"))["contextIds"]![0]!
                .GetValue<string>());
    }

    [Fact]
    public async Task Undo_ThatIsRefused_GivesTheReasons()
    {
        var item = await _api.CreateWeeklyAsync();
        var id = item["id"]!.GetValue<string>();
        var first = await _api.SendAsync(
            "PATCH",
            $"/api/items/{id}",
            """{"title":"Water the ferns"}""");
        await _api.WriteAsync(
            "PATCH",
            $"/api/items/{id}",
            """{"title":"Water the ferns and the palm"}""");
        var before = _api.Everything();

        var reply = await _api.SendAsync(
            "POST",
            $"/api/changes/{first.ChangeId}/undo");

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        var reason = Assert.Single(reply.Json["reasons"]!.AsArray())!
            .GetValue<string>();
        Assert.Contains("has been changed since: Title", reason);
        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Undo_Twice_IsRefused_AndAnUndoCanBeUndone()
    {
        var item = await _api.CreateWeeklyAsync();
        var change = (await _api.SendAsync(
            "DELETE",
            $"/api/items/{item["id"]}")).ChangeId;

        var undo = await _api.SendAsync("POST", $"/api/changes/{change}/undo");
        var twice = await _api.SendAsync("POST", $"/api/changes/{change}/undo");

        Assert.Equal(HttpStatusCode.OK, undo.Status);
        Assert.Equal(
            change,
            undo.Json["data"]!["id"]!.GetValue<long>());
        Assert.Equal(HttpStatusCode.Conflict, twice.Status);
        Assert.Contains("already been undone", twice.Body);

        var redo = await _api.SendAsync(
            "POST",
            $"/api/changes/{undo.ChangeId}/undo");
        Assert.Equal(HttpStatusCode.OK, redo.Status);
        Assert.Empty((await _api.GetAsync("/api/items")).AsArray());
    }

    [Fact]
    public async Task Undo_OfAChangeThatDoesNotExist_IsNotFound()
    {
        var reply = await _api.SendAsync("POST", "/api/changes/999/undo");

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        Assert.Equal(0, _api.Count("Changes"));
    }

    [Fact]
    public async Task Changes_AreListedNewestFirst_AndPaged()
    {
        for (var i = 0; i < 5; i++)
        {
            await _api.CreateContextAsync($"context {i}");
        }

        var page = await _api.GetAsync("/api/changes?limit=2");
        var ids = page.AsArray().Select(c => c!["id"]!.GetValue<long>()).ToList();
        Assert.Equal([5L, 4L], ids);

        var next = await _api.GetAsync("/api/changes?limit=2&before=4");
        Assert.Equal(
            [3L, 2L],
            next.AsArray().Select(c => c!["id"]!.GetValue<long>()));

        var change = page[0]!;
        Assert.Equal("api", change["source"]!.GetValue<string>());
        Assert.Equal("Create context", change["description"]!.GetValue<string>());
        Assert.Equal("2026-11-13T17:00:00Z", change["at"]!.GetValue<string>());
        Assert.Equal("added", change["entries"]![0]!["kind"]!.GetValue<string>());

        foreach (var query in new[] { "?limit=0", "?limit=201", "?after=3" })
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await _api.SendAsync("GET", "/api/changes" + query)).Status);
        }
    }
}
