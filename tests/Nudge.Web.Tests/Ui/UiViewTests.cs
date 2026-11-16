using System.Net;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// What each view of the web UI shows, with invented data. The clock
/// is at 23:30 on Friday 13 November 2026 in New York, the owner's
/// zone, when it is already the 14th in UTC: "today" is the 13th.
/// </summary>
public sealed class UiViewTests : IAsyncLifetime
{
    /// <summary>04:30 UTC on the 14th: 23:30 on the 13th in New York.
    /// </summary>
    private static readonly DateTimeOffset Now =
        new(2026, 11, 14, 4, 30, 0, TimeSpan.Zero);

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

    private Task<Guid> ItemAsync(
        string title,
        string extra = "")
    {
        return _host.CreateItemAsync(
            "{\"title\":\"" + title + "\"" + extra + "}");
    }

    private async Task<List<string>> TitlesAsync(string path)
    {
        return (await UiPage.OpenAsync(_host, path)).Texts("li.item > a");
    }

    [Fact]
    public async Task Inbox_ShowsOnlyOpenInboxItems()
    {
        await ItemAsync("Captured");
        await ItemAsync("Acted on", ""","list":"nextAction" """);
        var done = await ItemAsync("Done already");
        await _host.ApiAsync("POST", $"/api/items/{done}/complete");

        Assert.Equal(["Captured"], await TitlesAsync("/inbox"));
        Assert.Equal(["Captured"], await TitlesAsync("/"));
    }

    [Fact]
    public async Task NextActions_AreByContext_WithNoContextLast()
    {
        var calls = await _host.CreateAsync("/api/contexts", "calls");
        var errands = await _host.CreateAsync("/api/contexts", "Errands");
        await ItemAsync(
            "Ring the bank",
            $$""","list":"nextAction","contextIds":["{{calls}}"]""");
        await ItemAsync(
            "Post the parcel",
            $$""","list":"nextAction","contextIds":["{{calls}}","{{errands}}"]""");
        await ItemAsync("Sort the shed", ""","list":"nextAction" """);
        await ItemAsync("Still in the inbox");
        await ItemAsync("Waiting on it", ""","list":"waitingFor" """);

        var page = await UiPage.OpenAsync(_host, "/next");

        Assert.Equal(
            ["calls", "Errands", "No context"],
            page.Texts("section.group > h2"));
        var groups = page.Document.QuerySelectorAll("section.group")
            .Select(
                section => section
                    .QuerySelectorAll("li.item > a")
                    .Select(a => a.TextContent.Trim())
                    .ToList())
            .ToList();
        Assert.Equal(["Post the parcel", "Ring the bank"], groups[0]);
        Assert.Equal(["Post the parcel"], groups[1]);
        Assert.Equal(["Sort the shed"], groups[2]);
    }

    // It is the 13th in New York and the 14th in UTC.
    [Fact]
    public async Task NextActions_HideWhatIsDeferredPastTheOwnersToday()
    {
        await ItemAsync(
            "Deferred to today",
            ""","list":"nextAction","deferUntil":"2026-11-13" """);
        await ItemAsync(
            "Deferred to tomorrow",
            ""","list":"nextAction","deferUntil":"2026-11-14" """);
        await ItemAsync(
            "Deferred before",
            ""","list":"nextAction","deferUntil":"2026-11-01" """);

        Assert.Equal(
            ["Deferred before", "Deferred to today"],
            await TitlesAsync("/next"));
    }

    [Fact]
    public async Task WaitingAndSomeday_ShowTheirOwnLists()
    {
        await ItemAsync("Refund from the shop", ""","list":"waitingFor" """);
        await ItemAsync("Learn the cello", ""","list":"someday" """);
        await ItemAsync("Captured");

        Assert.Equal(["Refund from the shop"], await TitlesAsync("/waiting"));
        Assert.Equal(["Learn the cello"], await TitlesAsync("/someday"));
    }

    [Fact]
    public async Task Projects_ShowTheTree_AndThoseWithoutANextAction()
    {
        var house = await _host.CreateAsync("/api/projects", "House");
        var (_, kitchen) = await _host.ApiAsync(
            "POST",
            "/api/projects",
            $$"""{"name":"Kitchen","parentId":"{{house}}"}""");
        var kitchenId = Guid.Parse(kitchen!["data"]!["id"]!.GetValue<string>());
        await _host.CreateAsync("/api/projects", "Garden");
        await ItemAsync(
            "Order the worktop",
            $$""","list":"nextAction","projectId":"{{kitchenId}}" """);
        await ItemAsync(
            "Ask about the fence",
            $$""","list":"waitingFor","projectId":"{{house}}" """);

        var page = await UiPage.OpenAsync(_host, "/projects");

        Assert.Equal(
            ["Garden", "House", "Kitchen"],
            page.Texts("ul.projects > li > a"));
        var warned = page.Document.QuerySelectorAll("ul.projects > li")
            .Where(li => li.QuerySelector(".warn") is not null)
            .Select(li => li.QuerySelector("a")!.TextContent.Trim());
        Assert.Equal(["Garden", "House"], warned);

        var housePage = await UiPage.OpenAsync(_host, $"/projects/{house}");
        Assert.Equal(["Ask about the fence"], housePage.Texts("li.item > a"));
        Assert.Contains("Kitchen", housePage.Texts("ul.projects a"));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _host.GetAsync($"/projects/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Review_GoesThroughEveryList()
    {
        await _host.CreateAsync("/api/projects", "Garden");
        await ItemAsync("Captured one");
        await ItemAsync("Captured two");
        await ItemAsync("Refund from the shop", ""","list":"waitingFor" """);
        await ItemAsync("Learn the cello", ""","list":"someday" """);
        await ItemAsync(
            "Overdue",
            ""","list":"nextAction","due":{"date":"2026-11-12"}""");
        await ItemAsync(
            "Due today",
            ""","list":"nextAction","due":{"date":"2026-11-13"}""");
        await ItemAsync(
            "Due on the sixth day",
            ""","list":"nextAction","due":{"date":"2026-11-19"}""");
        await ItemAsync(
            "Due in a week",
            ""","list":"nextAction","due":{"date":"2026-11-20"}""");

        var page = await UiPage.OpenAsync(_host, "/review");

        Assert.Contains(
            page.Texts("section.group p a"),
            text => text.StartsWith("2", StringComparison.Ordinal)
                && text.Contains("items in the inbox", StringComparison.Ordinal));
        var sections = page.Document.QuerySelectorAll("section.group")
            .ToDictionary(
                s => s.QuerySelector("h2")!.TextContent.Trim(),
                s => s.QuerySelectorAll("li a")
                    .Select(a => a.TextContent.Trim())
                    .ToList());
        Assert.Equal(["Garden"], sections["Projects without a next action"]);
        Assert.Equal(["Overdue"], sections["Overdue"]);
        Assert.Equal(
            ["Due today", "Due on the sixth day"],
            sections["Due in the next seven days"]);
        Assert.Equal(["Refund from the shop"], sections["Waiting for"]);
        Assert.Equal(["Learn the cello"], sections["Someday/maybe"]);
        Assert.Empty(page.Document.QuerySelectorAll("form[method=post] .complete"));
    }

    [Fact]
    public async Task Missed_ShowsRemindersThatHaveFired_OnOpenItems()
    {
        // 09:00 in New York on the 13th, which has passed.
        await ItemAsync(
            "Fired this morning",
            ""","due":{"date":"2026-11-13"},"reminders":[{"offsetMinutes":540}]""");
        // 00:00 on the 14th in New York: 05:00 UTC, still to come.
        await ItemAsync(
            "Fires at midnight",
            ""","due":{"date":"2026-11-14"},"reminders":[{"offsetMinutes":0}]""");
        await ItemAsync(
            "Fixed time passed",
            ""","reminders":[{"at":"2026-11-14T04:00:00Z"}]""");
        await ItemAsync(
            "No due date",
            ""","reminders":[{"offsetMinutes":0}]""");
        var done = await ItemAsync(
            "Finished",
            ""","due":{"date":"2026-11-12"},"reminders":[{"offsetMinutes":0}]""");
        await _host.ApiAsync("POST", $"/api/items/{done}/complete");

        var page = await UiPage.OpenAsync(_host, "/missed");

        Assert.Equal(
            ["Fired this morning", "Fixed time passed"],
            page.Texts("li.item > a"));
        Assert.Contains(
            "until the phone app records",
            page.Html,
            StringComparison.Ordinal);
    }

    // The current occurrence counts: once a repeating item has moved
    // on, its reminder is for the next one, which has not fired.
    [Fact]
    public async Task Missed_IsForTheCurrentOccurrence()
    {
        await ItemAsync(
            "Water the ferns",
            """
            ,"list":"nextAction","due":{"date":"2026-11-13"},
            "repeat":{"rule":"FREQ=WEEKLY;BYDAY=FR","mode":"fixedSchedule"},
            "reminders":[{"offsetMinutes":540}]
            """);
        Assert.Equal(["Water the ferns"], await TitlesAsync("/missed"));

        var missed = await UiPage.OpenAsync(_host, "/missed");
        var form = missed.Document.Forms
            .Select(f => f.Elements
                .OfType<AngleSharp.Html.Dom.IHtmlInputElement>()
                .FirstOrDefault(i => i.Name == "_handler")?.Value)
            .Single(
                name => name?.StartsWith(
                    "complete-",
                    StringComparison.Ordinal) == true)!;
        await missed.SubmitAsync(form);

        Assert.Empty(await TitlesAsync("/missed"));
        Assert.Equal(["Water the ferns"], await TitlesAsync("/next"));
    }

    [Fact]
    public async Task Changes_ListsTheLatestWithTheirSource()
    {
        await ItemAsync("From the agent");
        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        await inbox.SubmitAsync("capture", ("title", "From the browser"));

        var page = await UiPage.OpenAsync(_host, "/changes");
        var rows = page.Document.QuerySelectorAll("table.changes tbody tr")
            .Select(
                tr => tr.QuerySelectorAll("td")
                    .Select(td => td.TextContent.Trim())
                    .ToList())
            .ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("web", rows[0][3]);
        Assert.StartsWith("api:", rows[1][3], StringComparison.Ordinal);
        Assert.Equal("Create item", rows[0][2]);
    }
}
