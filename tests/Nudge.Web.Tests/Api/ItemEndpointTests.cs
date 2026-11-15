using System.Net;
using System.Text.Json.Nodes;

namespace Nudge.Web.Tests.Api;

/// <summary>Items through HTTP: what each endpoint does and what it
/// refuses.</summary>
public sealed class ItemEndpointTests : IDisposable
{
    private readonly Api _api = new();

    public void Dispose()
    {
        _api.Dispose();
    }

    private static string Id(JsonNode item)
    {
        return item["id"]!.GetValue<string>();
    }

    [Fact]
    public async Task Create_TheWholeItem_IsOneChange()
    {
        var context = await _api.CreateContextAsync("phone");
        var project = await _api.CreateProjectAsync("House");
        var changes = _api.Count("Changes");

        var reply = await _api.SendAsync(
            "POST",
            "/api/items",
            $$"""
            {
              "title": "  Order the worktop  ",
              "notes": "Oak, three metres.",
              "list": "nextAction",
              "priority": "high",
              "projectId": "{{project}}",
              "deferUntil": "2026-11-18",
              "due": { "date": "2026-11-20", "time": "14:30:00" },
              "repeat": { "rule": "RRULE:FREQ=WEEKLY;BYDAY=FR",
                          "mode": "fixedSchedule" },
              "contextIds": ["{{context}}"],
              "reminders": [ { "offsetMinutes": -30 },
                             { "at": "2026-11-19T09:00:00-05:00" } ],
              "doNotAutoReschedule": true
            }
            """);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(changes + 1, _api.Count("Changes"));
        var tables = reply.Json["change"]!["entries"]!
            .AsArray()
            .Select(e => e!["table"]!.GetValue<string>())
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            ["ItemContexts", "Items", "Reminders", "Reminders"],
            tables);

        var item = reply.Json["data"]!;
        Assert.Equal("Order the worktop", item["title"]!.GetValue<string>());
        Assert.Equal("nextAction", item["list"]!.GetValue<string>());
        Assert.Equal("high", item["priority"]!.GetValue<string>());
        Assert.Equal(project, item["projectId"]!.GetValue<string>());
        Assert.Equal(context, item["contextIds"]![0]!.GetValue<string>());
        Assert.Equal(
            """{"rule":"FREQ=WEEKLY;BYDAY=FR","mode":"fixedSchedule","start":"2026-11-20","occurrence":"2026-11-20"}""",
            item["repeat"]!.ToJsonString());
        Assert.Equal(
            """{"date":"2026-11-20","time":"14:30:00"}""",
            item["due"]!.ToJsonString());
        // Reminders come in the order of their ids, which is not
        // the order they were made in within the same millisecond.
        var reminders = item["reminders"]!.AsArray();
        Assert.Equal(2, reminders.Count);
        Assert.Contains(
            reminders,
            r => r!["offsetMinutes"]?.GetValue<int>() == -30
                && r["at"] is null);
        Assert.Contains(
            reminders,
            r => r!["at"]?.GetValue<string>() == "2026-11-19T14:00:00Z"
                && r["offsetMinutes"] is null);

        Assert.Equal(
            item.ToJsonString(),
            (await _api.GetAsync($"/api/items/{Id(item)}")).ToJsonString());
    }

    [Fact]
    public async Task Create_ThatFailsPartWay_SavesNothing()
    {
        var before = _api.Everything();

        var reply = await _api.SendAsync(
            "POST",
            "/api/items",
            """
            {
              "title": "Water the plants",
              "due": { "date": "2026-11-20" },
              "repeat": { "rule": "FREQ=WEEKLY", "mode": "fixedSchedule" },
              "reminders": [ { "offsetMinutes": -30 },
                             { "offsetMinutes": 600000 } ]
            }
            """);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains("366 days", reply.Error("reminders[1].offsetMinutes"));
        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Create_WithRepeatStart_MovesTheFirstOccurrenceToTheDueDate()
    {
        var item = await _api.CreateItemAsync(
            """
            {
              "title": "Water the plants",
              "due": { "date": "2026-11-21" },
              "repeat": { "rule": "FREQ=WEEKLY;BYDAY=FR",
                          "mode": "fixedSchedule",
                          "start": "2026-11-14" }
            }
            """);

        Assert.Equal("2026-11-21", item["due"]!["date"]!.GetValue<string>());
        Assert.Equal(
            "2026-11-20",
            item["repeat"]!["occurrence"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_WithADueDateThatIsNotAnOccurrence_StartsOnTheNext()
    {
        // The 18th is a Wednesday.
        var item = await _api.CreateItemAsync(
            """
            {
              "title": "Water the plants",
              "due": { "date": "2026-11-18", "time": "08:00:00" },
              "repeat": { "rule": "FREQ=WEEKLY;BYDAY=FR",
                          "mode": "fixedSchedule" }
            }
            """);

        Assert.Equal(
            """{"date":"2026-11-20","time":"08:00:00"}""",
            item["due"]!.ToJsonString());
    }

    [Theory]
    [InlineData(
        """{"title":"Water","repeat":{"rule":"FREQ=WEEKLY","mode":"fixedSchedule"}}""",
        "repeat.start")]
    [InlineData(
        """{"title":"Water","due":{"date":"2026-11-20"},"repeat":{"rule":"FREQ=HOURLY","mode":"fixedSchedule"}}""",
        "repeat.rule")]
    [InlineData(
        """{"title":"   "}""",
        "title")]
    [InlineData(
        """{"title":"Water","projectId":"0199a000-0000-7000-8000-000000000000"}""",
        "projectId")]
    [InlineData(
        """{"title":"Water","contextIds":["0199a000-0000-7000-8000-000000000000"]}""",
        "contextIds")]
    [InlineData(
        """{"title":"Water","reminders":[{}]}""",
        "reminders[0]")]
    [InlineData(
        """{"title":"Water","reminders":[{"at":"2026-11-19T09:00:00Z","offsetMinutes":5}]}""",
        "reminders[0]")]
    [InlineData(
        """{"title":"Water","list":"done"}""",
        "list")]
    [InlineData(
        """{"title":"Water","priority":3}""",
        "priority")]
    [InlineData(
        """{"title":"Water","reminders":[{"at":"2026-11-19T09:00:00"}]}""",
        "reminders[0].at")]
    [InlineData(
        """{"title":null}""",
        "title")]
    [InlineData(
        """{"notes":"No title"}""",
        "body")]
    public async Task Create_WithSomethingWrong_IsRefused_NamingIt(
        string body,
        string field)
    {
        var before = _api.Everything();

        var reply = await _api.SendAsync("POST", "/api/items", body);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal("application/problem+json", reply.ContentType);
        Assert.NotEmpty(reply.Error(field));
        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Edit_ChangesWhatIsGiven_AndClearsWhatIsNull()
    {
        var project = await _api.CreateProjectAsync("House");
        var id = Id(
            await _api.CreateItemAsync(
                $$"""
                {"title":"Fix the gutter","notes":"Before winter.",
                 "projectId":"{{project}}","deferUntil":"2026-11-15"}
                """));

        var item = await _api.WriteAsync(
            "PATCH",
            $"/api/items/{id}",
            """{"priority":"low","projectId":null,"deferUntil":null}""");

        Assert.Equal("low", item["priority"]!.GetValue<string>());
        Assert.Null(item["projectId"]);
        Assert.Null(item["deferUntil"]);
        Assert.Equal("Fix the gutter", item["title"]!.GetValue<string>());
        Assert.Equal("Before winter.", item["notes"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("notes")]
    [InlineData("list")]
    [InlineData("priority")]
    [InlineData("doNotAutoReschedule")]
    public async Task Edit_WithNullForAValueThatCannotBeCleared_IsRefused(
        string property)
    {
        var id = Id(await _api.CreateItemAsync("""{"title":"Fix it"}"""));
        var before = _api.Everything();

        var reply = await _api.SendAsync(
            "PATCH",
            $"/api/items/{id}",
            $$"""{"{{property}}":null}""");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains("Cannot be null", reply.Error(property));
        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Edit_WithAPropertyThatHasItsOwnEndpoint_SaysWhichOne()
    {
        var id = Id(await _api.CreateWeeklyAsync());

        var reply = await _api.SendAsync(
            "PATCH",
            $"/api/items/{id}",
            """{"reminders":[]}""");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(
            "Unknown property. Add a reminder with POST"
            + " /api/items/{id}/reminders, remove one with DELETE"
            + " /api/items/{id}/reminders/{reminderId}.",
            reply.Error("reminders"));
    }

    [Fact]
    public async Task ClearDue_OnAnItemThatDoesNotRepeat_ClearsIt()
    {
        var id = Id(
            await _api.CreateItemAsync(
                """{"title":"Post a card","due":{"date":"2026-11-20","time":"10:00:00"}}"""));

        var item = await _api.WriteAsync("DELETE", $"/api/items/{id}/due");

        Assert.Null(item["due"]);
    }

    [Fact]
    public async Task Repeat_ThenStop_KeepsTheDueDate()
    {
        var id = Id(
            await _api.CreateItemAsync(
                """{"title":"Water","due":{"date":"2026-11-18","time":"08:00:00"}}"""));

        var repeating = await _api.WriteAsync(
            "PUT",
            $"/api/items/{id}/repeat",
            """{"rule":"FREQ=WEEKLY;BYDAY=FR","mode":"fixedSchedule"}""");
        Assert.Equal(
            """{"date":"2026-11-20","time":"08:00:00"}""",
            repeating["due"]!.ToJsonString());
        Assert.Equal(
            "2026-11-18",
            repeating["repeat"]!["start"]!.GetValue<string>());

        var stopped = await _api.WriteAsync(
            "DELETE",
            $"/api/items/{id}/repeat");
        Assert.Null(stopped["repeat"]);
        Assert.Equal(
            repeating["due"]!.ToJsonString(),
            stopped["due"]!.ToJsonString());
    }

    [Fact]
    public async Task Repeat_OnAnItemWithoutADueDate_NeedsAStart()
    {
        var id = Id(await _api.CreateItemAsync("""{"title":"Water"}"""));

        var reply = await _api.SendAsync(
            "PUT",
            $"/api/items/{id}/repeat",
            """{"rule":"FREQ=WEEKLY","mode":"afterCompletion"}""");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.NotEmpty(reply.Error("start"));
    }

    // A finished item that took a rule would repeat on paper, but
    // could not be completed and its reminders would not fire.
    [Theory]
    [InlineData("")]
    [InlineData("?dryRun=true")]
    public async Task Repeat_OnAFinishedItem_IsRefused(string query)
    {
        var id = Id(
            await _api.CreateItemAsync(
                """{"title":"Post a card","due":{"date":"2026-11-18"}}"""));
        await _api.WriteAsync("POST", $"/api/items/{id}/complete", "{}");
        var before = _api.Everything();

        var reply = await _api.SendAsync(
            "PUT",
            $"/api/items/{id}/repeat{query}",
            """{"rule":"FREQ=WEEKLY;BYDAY=FR","mode":"fixedSchedule"}""");

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.Contains("completed", reply.Body);
        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Complete_ARepeatingItem_WithoutNamingTheOccurrence_IsRefused()
    {
        var id = Id(await _api.CreateWeeklyAsync());
        var before = _api.Everything();

        var reply = await _api.SendAsync("POST", $"/api/items/{id}/complete");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains("2026-11-20", reply.Error("occurrence"));
        Assert.Equal(before, _api.Everything());
    }

    // The client thinks the item repeats, so its view is out of date.
    [Fact]
    public async Task Complete_NamingAnOccurrence_OfAnItemThatDoesNotRepeat_IsRefused()
    {
        var id = Id(
            await _api.CreateItemAsync(
                """{"title":"Post a card","due":{"date":"2026-11-20"}}"""));
        var before = _api.Everything();

        var reply = await _api.SendAsync(
            "POST",
            $"/api/items/{id}/complete",
            """{"occurrence":"2026-11-20"}""");

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.Equal(before, _api.Everything());
    }

    [Fact]
    public async Task Complete_FinishesAnItemThatDoesNotRepeat_AndUndoReopensIt()
    {
        var id = Id(await _api.CreateItemAsync("""{"title":"Post a card"}"""));

        var reply = await _api.SendAsync(
            "POST",
            $"/api/items/{id}/complete",
            "{}");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(
            "2026-11-13T17:00:00Z",
            reply.Json["data"]!["completedAt"]!.GetValue<string>());
        var open = await _api.GetAsync("/api/items");
        Assert.Empty(open.AsArray());
        var all = await _api.GetAsync("/api/items?includeCompleted=true");
        Assert.Single(all.AsArray());

        await _api.WriteAsync("POST", $"/api/changes/{reply.ChangeId}/undo");

        var reopened = await _api.GetAsync($"/api/items/{id}");
        Assert.Null(reopened["completedAt"]);
    }

    [Fact]
    public async Task Deleted_IsNotFound_AndUndoBringsItBack()
    {
        var id = Id(await _api.CreateWeeklyAsync());

        var deleted = await _api.SendAsync("DELETE", $"/api/items/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.Status);
        Assert.Null(deleted.Json["data"]);

        foreach (var (method, path) in new[]
        {
            ("GET", $"/api/items/{id}"),
            ("DELETE", $"/api/items/{id}"),
            ("PUT", $"/api/items/{id}/due"),
        })
        {
            var reply = await _api.SendAsync(
                method,
                path,
                method == "PUT" ? """{"date":"2026-11-21"}""" : null);
            Assert.Equal(HttpStatusCode.NotFound, reply.Status);
            Assert.Equal("application/problem+json", reply.ContentType);
        }

        Assert.Empty((await _api.GetAsync("/api/items")).AsArray());

        await _api.WriteAsync("POST", $"/api/changes/{deleted.ChangeId}/undo");
        Assert.Equal(
            id,
            Id(await _api.GetAsync($"/api/items/{id}")));
    }

    [Fact]
    public async Task Contexts_AreAddedAndRemovedOneAtATime()
    {
        var phone = await _api.CreateContextAsync("phone");
        var home = await _api.CreateContextAsync("home");
        var id = Id(
            await _api.CreateItemAsync(
                $$"""{"title":"Call the plumber","contextIds":["{{home}}"]}"""));

        var added = await _api.WriteAsync(
            "PUT",
            $"/api/items/{id}/contexts/{phone}");
        Assert.Equal(2, added["contextIds"]!.AsArray().Count);

        // Adding it again changes nothing.
        var again = await _api.SendAsync(
            "PUT",
            $"/api/items/{id}/contexts/{phone}");
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Null(again.Json["change"]);

        var removed = await _api.WriteAsync(
            "DELETE",
            $"/api/items/{id}/contexts/{home}");
        Assert.Equal(
            phone,
            Assert.Single(removed["contextIds"]!.AsArray())!.GetValue<string>());

        var missing = await _api.SendAsync(
            "PUT",
            $"/api/items/{id}/contexts/0199a000-0000-7000-8000-000000000000");
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Reminders_AreAddedAndRemoved()
    {
        var id = Id(await _api.CreateItemAsync("""{"title":"Post a card"}"""));

        var added = await _api.SendAsync(
            "POST",
            $"/api/items/{id}/reminders",
            """{"at":"2026-11-19T09:00:00Z"}""");
        Assert.Equal(HttpStatusCode.Created, added.Status);
        var reminder = added.Json["data"]!["reminders"]![0]!["id"]!
            .GetValue<string>();

        var removed = await _api.WriteAsync(
            "DELETE",
            $"/api/items/{id}/reminders/{reminder}");
        Assert.Empty(removed["reminders"]!.AsArray());

        var again = await _api.SendAsync(
            "DELETE",
            $"/api/items/{id}/reminders/{reminder}");
        Assert.Equal(HttpStatusCode.NotFound, again.Status);
    }

    [Fact]
    public async Task List_FiltersByListProjectAndContext()
    {
        var phone = await _api.CreateContextAsync("phone");
        var house = await _api.CreateProjectAsync("House");
        await _api.CreateItemAsync(
            $$"""{"title":"Call the plumber","list":"nextAction","contextIds":["{{phone}}"]}""");
        await _api.CreateItemAsync(
            $$"""{"title":"Fix the gutter","list":"someday","projectId":"{{house}}"}""");
        await _api.CreateItemAsync("""{"title":"Think about a shed"}""");

        async Task<string[]> TitlesAsync(string query)
        {
            var items = await _api.GetAsync("/api/items" + query);
            return items.AsArray()
                .Select(i => i!["title"]!.GetValue<string>())
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        Assert.Equal(["Call the plumber"], await TitlesAsync("?list=nextAction"));
        Assert.Equal(["Fix the gutter"], await TitlesAsync($"?projectId={house}"));
        Assert.Equal(["Call the plumber"], await TitlesAsync($"?contextId={phone}"));
        Assert.Equal(3, (await TitlesAsync("")).Length);

        foreach (var query in new[]
        {
            "?list=done",
            "?list=1",
            "?projectid=" + house,
            "?includeCompleted=yes",
            "?list=inbox&list=someday",
        })
        {
            var reply = await _api.SendAsync("GET", "/api/items" + query);
            Assert.True(
                reply.Status == HttpStatusCode.BadRequest,
                $"{query}: {(int)reply.Status}");
        }
    }
}
