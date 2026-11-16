using System.Net;
using System.Text.Json.Nodes;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// The writes of the web UI, sent as the browser sends its forms:
/// each is recorded as made by <c>web</c>, under the API's own
/// description, and its undo, from the notice shown after it, puts
/// the item back as it was. A form sent twice is carried out once,
/// one without the antiforgery token is refused, and a refusal is
/// shown on the page.
/// </summary>
public sealed class UiOperationTests : IAsyncLifetime
{
    /// <summary>Midday in New York on Friday 13 November 2026.
    /// </summary>
    private static readonly DateTimeOffset Now =
        new(2026, 11, 13, 17, 0, 0, TimeSpan.Zero);

    private readonly WebUiHost _host = new(now: Now);

    public async Task InitializeAsync()
    {
        await _host.SignInAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    private Task<Guid> InboxItemAsync(string title = "Call the plumber")
    {
        return _host.CreateItemAsync($$"""{"title":"{{title}}"}""");
    }

    private Task<Guid> WeeklyAsync()
    {
        return _host.CreateItemAsync(
            """
            {
              "title": "Water the ferns",
              "list": "nextAction",
              "due": { "date": "2026-11-13" },
              "repeat": { "rule": "FREQ=WEEKLY;BYDAY=FR",
                          "mode": "fixedSchedule" }
            }
            """);
    }

    private static string PathOf(HttpResponseMessage reply)
    {
        return new Uri(new Uri("https://localhost"), reply.Headers.Location!)
            .PathAndQuery;
    }

    /// <summary>Checks that the answer went back with the change,
    /// and that the change is the newest, made by the web UI.
    /// </summary>
    private long AssertWebChange(
        HttpResponseMessage reply,
        string description)
    {
        Assert.True(
            reply.StatusCode == HttpStatusCode.Redirect,
            $"{(int)reply.StatusCode}");
        var location = new Uri(
            new Uri("https://localhost"),
            reply.Headers.Location!);
        var id = long.Parse(WebUiHost.Query(location)["change"]);
        var last = _host.LastChange()!.Value;
        Assert.Equal(id, last.Id);
        Assert.Equal("web", last.Source);
        Assert.Equal(description, last.Description);
        return id;
    }

    /// <summary>Undoes a change with the notice on the page the
    /// write went back to, and checks the undo is the web UI's too.
    /// </summary>
    private async Task UndoFromNoticeAsync(HttpResponseMessage reply)
    {
        var page = await UiPage.OpenAsync(_host, PathOf(reply));
        Assert.Contains(
            page.Texts(".notice span"),
            text => text.StartsWith("Done:", StringComparison.Ordinal));

        AssertWebChange(await page.SubmitAsync("undo-notice"), "Undo change");
    }

    /// <summary>Does a write from the item's page, checks it, undoes
    /// it, and checks the item is as it was.</summary>
    private async Task<JsonNode> WriteAndUndoAsync(
        Guid id,
        string form,
        string description,
        params (string Name, string? Value)[] fields)
    {
        var before = await _host.ItemJsonAsync(id);
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync(form, fields);

        AssertWebChange(reply, description);
        var after = await _host.ItemJsonAsync(id);
        Assert.NotEqual(before, after);
        await UndoFromNoticeAsync(reply);
        Assert.Equal(before, await _host.ItemJsonAsync(id));
        return JsonNode.Parse(after ?? "null")!;
    }

    [Fact]
    public async Task Capture_PutsTheItemInTheInbox_AndUndoTakesItOut()
    {
        var inbox = await UiPage.OpenAsync(_host, "/inbox");

        var reply = await inbox.SubmitAsync(
            "capture",
            ("title", "  Book the dentist  "));

        AssertWebChange(reply, "Create item");
        var after = await UiPage.OpenAsync(_host, PathOf(reply));
        Assert.Contains("Book the dentist", after.Texts("li.item > a"));

        await UndoFromNoticeAsync(reply);
        Assert.DoesNotContain(
            "Book the dentist",
            (await UiPage.OpenAsync(_host, "/inbox")).Texts("li.item > a"));
    }

    [Fact]
    public async Task Process_MovesTheItemOutOfTheInbox_AndUndoBringsItBack()
    {
        var id = await InboxItemAsync();
        var project = await _host.CreateAsync("/api/projects", "House");
        var inbox = await UiPage.OpenAsync(_host, "/inbox");

        var before = await _host.ItemJsonAsync(id);
        var reply = await inbox.SubmitAsync(
            $"process-{id:N}",
            ("list", "WaitingFor"),
            ("projectId", project.ToString()),
            ("deferUntil", "2026-11-16"));

        AssertWebChange(reply, "Edit item");
        var item = JsonNode.Parse((await _host.ItemJsonAsync(id))!)!;
        Assert.Equal("waitingFor", item["list"]!.GetValue<string>());
        Assert.Equal(project.ToString(), item["projectId"]!.GetValue<string>());
        Assert.Equal("2026-11-16", item["deferUntil"]!.GetValue<string>());
        Assert.Empty(
            (await UiPage.OpenAsync(_host, "/inbox")).Texts("li.item > a"));

        await UndoFromNoticeAsync(reply);
        Assert.Equal(before, await _host.ItemJsonAsync(id));
    }

    [Fact]
    public async Task Complete_FromAList_FinishesTheItem_AndUndoReopensIt()
    {
        var id = await InboxItemAsync();
        var before = await _host.ItemJsonAsync(id);
        var inbox = await UiPage.OpenAsync(_host, "/inbox");

        var reply = await inbox.SubmitAsync($"complete-{id:N}");

        AssertWebChange(reply, "Complete item");
        var item = JsonNode.Parse((await _host.ItemJsonAsync(id))!)!;
        Assert.NotNull(item["completedAt"]);
        Assert.Empty(
            (await UiPage.OpenAsync(_host, "/inbox")).Texts("li.item > a"));

        await UndoFromNoticeAsync(reply);
        Assert.Equal(before, await _host.ItemJsonAsync(id));
    }

    [Fact]
    public async Task Defer_SetsTheDate_AndUndoTakesItBack()
    {
        var id = await InboxItemAsync();

        var after = await WriteAndUndoAsync(
            id,
            "defer",
            "Edit item",
            ("deferUntil", "2026-12-01"));

        Assert.Equal("2026-12-01", after["deferUntil"]!.GetValue<string>());
    }

    [Fact]
    public async Task EditRecurrence_SetsTheRule_AndUndoTakesItBack()
    {
        var id = await _host.CreateItemAsync(
            """{"title":"Pay rent","due":{"date":"2026-11-30"}}""");

        var after = await WriteAndUndoAsync(
            id,
            "repeat",
            "Set repeat rule",
            ("rule", "FREQ=MONTHLY;BYMONTHDAY=-1"),
            ("mode", "FixedSchedule"));

        Assert.Equal(
            "FREQ=MONTHLY;BYMONTHDAY=-1",
            after["repeat"]!["rule"]!.GetValue<string>());
    }

    [Fact]
    public async Task EditRecurrence_OfARepeatingItem_KeepsItsDueDate()
    {
        var id = await WeeklyAsync();

        var after = await WriteAndUndoAsync(
            id,
            "repeat",
            "Set repeat rule",
            ("rule", "FREQ=WEEKLY;INTERVAL=2;BYDAY=FR"));

        Assert.Equal(
            "FREQ=WEEKLY;INTERVAL=2;BYDAY=FR",
            after["repeat"]!["rule"]!.GetValue<string>());
        Assert.Equal(
            "2026-11-13",
            after["due"]!["date"]!.GetValue<string>());
    }

    [Fact]
    public async Task StopRepeating_KeepsTheDueDate_AndUndoBringsTheRuleBack()
    {
        var id = await WeeklyAsync();

        var after = await WriteAndUndoAsync(
            id,
            "stop-repeating",
            "Stop repeating");

        Assert.Null(after["repeat"]);
        Assert.Equal("2026-11-13", after["due"]!["date"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reschedule_AndClearTheDueDate_CanEachBeUndone()
    {
        var id = await InboxItemAsync();
        await WriteAndUndoAsync(
            id,
            "due",
            "Reschedule item",
            ("date", "2026-11-20"),
            ("time", "14:30"));

        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        AssertWebChange(
            await page.SubmitAsync("due", ("date", "2026-11-20")),
            "Reschedule item");
        await WriteAndUndoAsync(id, "clear-due", "Clear due date");
    }

    [Fact]
    public async Task Edit_ChangesTheFields_AndUndoTakesThemBack()
    {
        var id = await InboxItemAsync();

        var after = await WriteAndUndoAsync(
            id,
            "edit",
            "Edit item",
            ("title", "Call the plumber about the tap"),
            ("notes", "After nine."),
            ("list", "NextAction"),
            ("priority", "High"),
            ("doNotAutoReschedule", "true"));

        Assert.Equal(
            "Call the plumber about the tap",
            after["title"]!.GetValue<string>());
        Assert.Equal("high", after["priority"]!.GetValue<string>());
        Assert.True(after["doNotAutoReschedule"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ContextsAndReminders_CanBeAddedAndRemoved()
    {
        var id = await InboxItemAsync();
        await _host.CreateAsync("/api/contexts", "phone");
        await _host.ApiAsync(
            "PUT",
            $"/api/items/{id}/due",
            """{"date":"2026-11-20"}""");

        var withContext = await WriteAndUndoAsync(
            id,
            "add-context",
            "Add context to item");
        Assert.Single(withContext["contextIds"]!.AsArray());
        var withReminder = await WriteAndUndoAsync(
            id,
            "add-reminder",
            "Add reminder",
            ("offsetMinutes", "-30"));
        Assert.Equal(
            -30,
            withReminder["reminders"]![0]!["offsetMinutes"]!.GetValue<int>());

        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        AssertWebChange(await page.SubmitAsync("add-context"), "Add context to item");
        var context = JsonNode.Parse((await _host.ItemJsonAsync(id))!)!
            ["contextIds"]![0]!.GetValue<string>();
        await WriteAndUndoAsync(
            id,
            $"remove-context-{Guid.Parse(context):N}",
            "Remove context from item");
    }

    [Fact]
    public async Task Delete_TakesTheItemAway_AndUndoBringsItBack()
    {
        var id = await InboxItemAsync();
        var before = await _host.ItemJsonAsync(id);
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync("delete");

        AssertWebChange(reply, "Delete item");
        Assert.StartsWith("/inbox?change=", PathOf(reply), StringComparison.Ordinal);
        Assert.Null(await _host.ItemJsonAsync(id));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _host.GetAsync($"/items/{id}")).StatusCode);

        await UndoFromNoticeAsync(reply);
        Assert.Equal(before, await _host.ItemJsonAsync(id));
    }

    [Fact]
    public async Task Undo_FromTheChangesPage_TakesAnApiChangeBack()
    {
        var id = await InboxItemAsync();
        var before = await _host.ItemJsonAsync(id);
        await _host.ApiAsync(
            "PATCH",
            $"/api/items/{id}",
            """{"title":"Renamed by the agent"}""");
        var made = _host.LastChange()!.Value;
        Assert.StartsWith("api:", made.Source, StringComparison.Ordinal);
        var changes = await UiPage.OpenAsync(_host, "/changes");

        var reply = await changes.SubmitAsync($"undo-{made.Id}");

        AssertWebChange(reply, "Undo change");
        Assert.Equal(before, await _host.ItemJsonAsync(id));
    }

    // The same form sent twice, as a double click or a resend would,
    // is carried out once and answered the same.
    [Fact]
    public async Task SameFormSentTwice_IsCarriedOutOnce()
    {
        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        var fields = inbox.Fields("capture");
        fields["title"] = "Buy stamps";
        var changes = _host.Count("Changes");

        var first = await inbox.SendAsync(fields);
        var second = await inbox.SendAsync(fields);

        Assert.Equal(changes + 1, _host.Count("Changes"));
        Assert.Equal(PathOf(first), PathOf(second));
        Assert.Single(
            (await UiPage.OpenAsync(_host, "/inbox")).Texts("li.item > a"),
            "Buy stamps");
    }

    [Fact]
    public async Task SameKey_WithOtherValues_IsRefusedOnThePage()
    {
        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        var fields = inbox.Fields("capture");
        await inbox.SendAsync(fields, ("title", "Buy stamps"));
        var changes = _host.Count("Changes");

        var reply = await inbox.SendAsync(fields, ("title", "Buy envelopes"));

        var page = await UiPage.FromAsync(_host, "/inbox", reply);
        Assert.Contains(
            page.Texts(".error"),
            text => text.Contains("already sent", StringComparison.Ordinal));
        Assert.Equal(changes, _host.Count("Changes"));
    }

    [Fact]
    public async Task Form_WithoutTheAntiforgeryToken_IsRefused()
    {
        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        var changes = _host.Count("Changes");

        var reply = await inbox.SubmitAsync(
            "capture",
            ("title", "Buy stamps"),
            ("__RequestVerificationToken", null));

        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Equal(changes, _host.Count("Changes"));
        Assert.Equal(0, _host.Count("Items"));
    }

    [Fact]
    public async Task Form_WithAnotherSessionsToken_IsRefused()
    {
        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        var fields = inbox.Fields("capture");
        using var other = new WebUiHost(now: Now);
        await other.SignInAsync();
        var token = (await UiPage.OpenAsync(other, "/inbox"))
            .Fields("capture")["__RequestVerificationToken"];

        var reply = await inbox.SendAsync(
            fields,
            ("title", "Buy stamps"),
            ("__RequestVerificationToken", token));

        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Equal(0, _host.Count("Items"));
    }

    [Fact]
    public async Task Refusal_IsShownOnThePage_AndNothingIsWritten()
    {
        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        var changes = _host.Count("Changes");

        var reply = await inbox.SubmitAsync("capture", ("title", "   "));

        Assert.StartsWith(
            "text/html",
            reply.Content.Headers.ContentType!.ToString(),
            StringComparison.Ordinal);
        var page = await UiPage.FromAsync(_host, "/inbox", reply);
        var error = Assert.Single(page.Texts("[role=alert]"));
        Assert.StartsWith("Title:", error, StringComparison.Ordinal);
        Assert.Equal(changes, _host.Count("Changes"));
    }

    [Fact]
    public async Task Refusal_ForABadDate_IsShownOnThePage()
    {
        var id = await InboxItemAsync();
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync("defer", ("deferUntil", "soon"));

        var shown = await UiPage.FromAsync(_host, $"/items/{id}", reply);
        Assert.NotEmpty(shown.Texts("[role=alert]"));
    }

    // The first named M1 regression test, from the web UI: one
    // completion moves a repeating item on by exactly one occurrence,
    // however often the form is sent.
    [Fact]
    public async Task CompletingARepeatingItem_AdvancesItExactlyOnce()
    {
        var id = await WeeklyAsync();
        var next = await UiPage.OpenAsync(_host, "/next");
        var fields = next.Fields($"complete-none{id:N}");
        Assert.Equal("2026-11-13", fields["occurrence"]);

        var first = await next.SendAsync(fields);
        var again = await next.SendAsync(fields);
        var stale = await next.SendAsync(
            fields,
            ("key", Guid.NewGuid().ToString("N")));

        AssertWebChange(first, "Complete item");
        Assert.Equal(PathOf(first), PathOf(again));
        var refused = await UiPage.FromAsync(_host, "/next", stale);
        Assert.Contains(
            refused.Texts("[role=alert]"),
            text => text.Contains("not the current one", StringComparison.Ordinal));
        var item = JsonNode.Parse((await _host.ItemJsonAsync(id))!)!;
        Assert.Equal("2026-11-20", item["due"]!["date"]!.GetValue<string>());
        Assert.Equal(
            "FREQ=WEEKLY;BYDAY=FR",
            item["repeat"]!["rule"]!.GetValue<string>());
        Assert.Null(item["completedAt"]);
    }

    [Fact]
    public async Task ItemText_IsEncoded()
    {
        const string Title = "<script>alert('x')</script>";
        var id = await _host.CreateItemAsync(
            System.Text.Json.JsonSerializer.Serialize(
                new { title = Title, notes = "<b>bold</b>" }));

        foreach (var path in new[] { "/inbox", $"/items/{id}" })
        {
            var page = await UiPage.OpenAsync(_host, path);
            Assert.DoesNotContain("<script>alert", page.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("<b>bold</b>", page.Html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", page.Html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Item_ThatIsNotThere_IsNotFound()
    {
        var reply = await _host.GetAsync($"/items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, reply.StatusCode);
        Assert.Contains(
            "<h1>Not found</h1>",
            await reply.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }
}
