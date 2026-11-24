using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Html.Dom;
using Nudge.Web.Components;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// The pages in plain words, on invented data. The clock is at 23:30
/// on Friday 13 November 2026 in New York, the owner's zone, when it
/// is already the 14th in UTC. Form values stay ISO, and a rule that
/// is no preset survives every form.
/// </summary>
public sealed class PlainLanguagePageTests : IAsyncLifetime
{
    private const string NoPresetRule = "FREQ=MONTHLY;BYMONTHDAY=1,15";

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

    private Task<Guid> RepeatingAsync(string rule)
    {
        return _host.CreateItemAsync(
            """
            {"title":"Water the ferns","list":"nextAction",
             "due":{"date":"2026-11-13"},
             "repeat":{"rule":"RULE","mode":"fixedSchedule"}}
            """.Replace("RULE", rule, StringComparison.Ordinal));
    }

    private Task<Guid> DueAsync(string due)
    {
        return _host.CreateItemAsync(
            "{\"title\":\"Meet the builder\",\"due\":{" + due + "}}");
    }

    private async Task<JsonNode> ItemAsync(Guid id)
    {
        return JsonNode.Parse((await _host.ItemJsonAsync(id))!)!;
    }

    private async Task<string> RuleAsync(Guid id)
    {
        return (await ItemAsync(id))["repeat"]!["rule"]!.GetValue<string>();
    }

    private async Task<List<int>> OffsetsAsync(Guid id)
    {
        return (await ItemAsync(id))["reminders"]!
            .AsArray()
            .Select(r => r!["offsetMinutes"]!.GetValue<int>())
            .ToList();
    }

    private static string SelectedPreset(UiPage page)
    {
        var select = page.Document
            .QuerySelector("select[name=preset]") as IHtmlSelectElement;
        return select!.Options.First(o => o.IsSelected).Value;
    }

    private static string Value(
        UiPage page,
        string name)
    {
        var input = page.Document.QuerySelector($"input[name={name}]");
        return ((IHtmlInputElement)input!).Value;
    }

    // It is the 13th in New York and already the 14th in UTC.
    [Theory]
    [InlineData("2026-11-13", "due Today")]
    [InlineData("2026-11-14", "due Tomorrow")]
    [InlineData("2026-11-12", "due Yesterday")]
    [InlineData("2026-11-27", "due Fri 27 Nov")]
    [InlineData("2027-02-05", "due Fri 5 Feb 2027")]
    public async Task DueDates_ReadInTheOwnersDay(
        string date,
        string expected)
    {
        var id = await DueAsync($"\"date\":\"{date}\"");

        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        Assert.Contains(expected, page.Texts(".fact"));
    }

    [Fact]
    public async Task DueTime_IsShownWithTheDay()
    {
        await DueAsync("\"date\":\"2026-11-13\",\"time\":\"14:30\"");

        var page = await UiPage.OpenAsync(_host, "/inbox");

        Assert.Contains("due Today 14:30", page.Texts(".fact"));
    }

    [Fact]
    public async Task FormValues_StayIso()
    {
        var id = await RepeatingAsync("FREQ=WEEKLY;BYDAY=FR");
        await _host.ApiAsync(
            "PUT",
            $"/api/items/{id}/due",
            """{"date":"2026-11-27","time":"09:05:07"}""");
        await _host.ApiAsync(
            "PATCH",
            $"/api/items/{id}",
            """{"deferUntil":"2026-11-20"}""");

        var item = await UiPage.OpenAsync(_host, $"/items/{id}");
        var list = await UiPage.OpenAsync(_host, "/waiting");
        var next = await UiPage.OpenAsync(_host, "/next");

        Assert.Equal("2026-11-27", item.Fields("due")["date"]);
        Assert.Equal("09:05:07", item.Fields("due")["time"]);
        Assert.Equal("2026-11-20", item.Fields("defer")["deferUntil"]);
        Assert.Equal("2026-11-13", item.Fields("complete")["occurrence"]);
        Assert.Empty(list.Texts("li.item > a"));
        Assert.Empty(next.Texts("li.item > a"));
    }

    [Fact]
    public async Task ListOccurrence_StaysIso_UnderAPlainDueDate()
    {
        var id = await RepeatingAsync("FREQ=WEEKLY;BYDAY=FR");

        var next = await UiPage.OpenAsync(_host, "/next");

        Assert.Equal(
            "2026-11-13",
            next.Fields($"complete-none{id:N}")["occurrence"]);
        Assert.Contains("due Today", next.Texts(".fact"));
    }

    [Fact]
    public async Task Repeat_IsDescribed_WithItsMode()
    {
        await RepeatingAsync("FREQ=WEEKLY;INTERVAL=2;BYDAY=FR");

        var page = await UiPage.OpenAsync(_host, "/next");

        Assert.Contains(
            "Every 2 weeks on Friday, on a fixed schedule",
            page.Texts(".fact"));
    }

    [Fact]
    public async Task Repeat_ThatCannotBeDescribed_ShowsTheRawRule()
    {
        await RepeatingAsync(NoPresetRule);

        var page = await UiPage.OpenAsync(_host, "/next");

        Assert.Contains(
            $"{NoPresetRule} (on a fixed schedule)",
            page.Texts(".fact"));
    }

    [Fact]
    public async Task RuleThatIsNoPreset_ShowsAsCustom_WithTheRuleIntact()
    {
        var id = await RepeatingAsync(NoPresetRule);

        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        Assert.Equal(RepeatPresets.Custom, SelectedPreset(page));
        Assert.Equal(NoPresetRule, Value(page, "rule"));
        Assert.NotNull(page.Document.QuerySelector("details[open] input[name=rule]"));
    }

    [Fact]
    public async Task RuleThatIsNoPreset_SavedAsShown_IsUnchanged()
    {
        var id = await RepeatingAsync(NoPresetRule);
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync("repeat");

        Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);
        Assert.Equal(NoPresetRule, await RuleAsync(id));
    }

    [Fact]
    public async Task RuleThatIsNoPreset_SurvivesTheOtherForms()
    {
        var id = await RepeatingAsync(NoPresetRule);

        foreach (var (form, fields) in new (string, (string, string?)[])[]
        {
            ("edit", [("notes", "Invented note")]),
            ("defer", [("deferUntil", "2026-11-14")]),
            ("due", [("date", "2026-11-16")]),
            ("add-reminder", []),
        })
        {
            var page = await UiPage.OpenAsync(_host, $"/items/{id}");
            var reply = await page.SubmitAsync(form, fields);
            Assert.True(reply.StatusCode == HttpStatusCode.Redirect, form);
            Assert.Equal(NoPresetRule, await RuleAsync(id));
        }

        Assert.Equal(
            "2026-11-16",
            (await ItemAsync(id))["due"]!["date"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("daily", "FREQ=DAILY")]
    [InlineData("weekdays", "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR")]
    [InlineData("weekly-FR", "FREQ=WEEKLY;BYDAY=FR")]
    [InlineData("2-weeks-FR", "FREQ=WEEKLY;INTERVAL=2;BYDAY=FR")]
    [InlineData("monthly", "FREQ=MONTHLY")]
    [InlineData("3-months", "FREQ=MONTHLY;INTERVAL=3")]
    [InlineData("6-months", "FREQ=MONTHLY;INTERVAL=6")]
    [InlineData("yearly", "FREQ=YEARLY")]
    public async Task Preset_WritesItsRule(
        string preset,
        string rule)
    {
        var id = await DueAsync("\"date\":\"2026-11-13\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync("repeat", ("preset", preset));

        Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);
        Assert.Equal(rule, await RuleAsync(id));
        var after = await UiPage.OpenAsync(_host, $"/items/{id}");
        Assert.Equal(preset, SelectedPreset(after));
    }

    [Fact]
    public async Task WeekdayPresets_AreOnTheDueDatesWeekday()
    {
        // Monday 16 November.
        var id = await DueAsync("\"date\":\"2026-11-16\"");

        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        Assert.Contains("Every week on Monday", page.Texts("select[name=preset] option"));
        await page.SubmitAsync("repeat", ("preset", "weekly-MO"));
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO", await RuleAsync(id));
    }

    [Fact]
    public async Task RuleTypedUnderAPreset_IsRefused_AndNothingChanges()
    {
        var id = await RepeatingAsync("FREQ=WEEKLY;BYDAY=FR");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync("repeat", ("rule", "FREQ=DAILY"));

        var refused = await UiPage.FromAsync(_host, page.Path, reply);
        Assert.Contains(
            refused.Texts("[role=alert]"),
            text => text.Contains("Choose Custom", StringComparison.Ordinal));
        Assert.Equal("FREQ=WEEKLY;BYDAY=FR", await RuleAsync(id));
    }

    [Fact]
    public async Task NoChoice_IsRefused()
    {
        var id = await DueAsync("\"date\":\"2026-11-13\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync("repeat");

        var refused = await UiPage.FromAsync(_host, page.Path, reply);
        Assert.Contains(
            refused.Texts("[role=alert]"),
            text => text.Contains("how often", StringComparison.Ordinal));
        Assert.Null((await ItemAsync(id))["repeat"]);
    }

    [Theory]
    [InlineData("day-0900", 540, "09:00 on the due day (Fri 20 Nov 09:00)")]
    [InlineData(
        "day-before-1800",
        -360,
        "18:00 the day before (Thu 19 Nov 18:00)")]
    public async Task ReminderPreset_WithNoDueTime_CountsFromMidnight(
        string preset,
        int offset,
        string words)
    {
        var id = await DueAsync("\"date\":\"2026-11-20\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        await page.SubmitAsync("add-reminder", ("reminder", preset));

        Assert.Equal([offset], await OffsetsAsync(id));
        var after = await UiPage.OpenAsync(_host, $"/items/{id}");
        Assert.Contains(
            after.Texts("section.group li"),
            text => text.StartsWith(words, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "day-0900",
        -300,
        "5 hours before the due time (Fri 20 Nov 09:00)")]
    [InlineData(
        "day-before-1800",
        -1200,
        "20 hours before the due time (Thu 19 Nov 18:00)")]
    [InlineData("60-before", -60, "1 hour before the due time (Fri 20 Nov 13:00)")]
    public async Task ReminderPreset_WithADueTime_CountsFromIt(
        string preset,
        int offset,
        string words)
    {
        var id = await DueAsync("\"date\":\"2026-11-20\",\"time\":\"14:00\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        await page.SubmitAsync("add-reminder", ("reminder", preset));

        Assert.Equal([offset], await OffsetsAsync(id));
        var after = await UiPage.OpenAsync(_host, $"/items/{id}");
        Assert.Contains(
            after.Texts("section.group li"),
            text => text.StartsWith(words, StringComparison.Ordinal));
    }

    // The page was opened before the due time changed: the preset
    // still means what its label said.
    [Fact]
    public async Task ReminderPreset_FromAStalePage_UsesTheCurrentDueTime()
    {
        var id = await DueAsync("\"date\":\"2026-11-20\",\"time\":\"14:00\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        await _host.ApiAsync(
            "PUT",
            $"/api/items/{id}/due",
            """{"date":"2026-11-20","time":"10:00"}""");

        await page.SubmitAsync("add-reminder", ("reminder", "day-0900"));

        Assert.Equal([-60], await OffsetsAsync(id));
    }

    [Fact]
    public async Task ClockPreset_OnADueTimeWithSeconds_IsNotOffered()
    {
        var id = await DueAsync("\"date\":\"2026-11-20\",\"time\":\"14:00:30\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        Assert.DoesNotContain(
            "09:00 on the due day",
            page.Texts("select[name=reminder] option"));
        var reply = await page.SubmitAsync(
            "add-reminder",
            ("reminder", "day-0900"));

        await UiPage.FromAsync(_host, page.Path, reply);
        Assert.Empty(await OffsetsAsync(id));
    }

    [Fact]
    public async Task CustomMinutes_AreWritten()
    {
        var id = await DueAsync("\"date\":\"2026-11-20\",\"time\":\"14:00\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        await page.SubmitAsync(
            "add-reminder",
            ("reminder", ReminderPresets.Custom),
            ("offsetMinutes", "-45"));

        Assert.Equal([-45], await OffsetsAsync(id));
    }

    [Fact]
    public async Task MinutesTypedUnderAPreset_AreRefused()
    {
        var id = await DueAsync("\"date\":\"2026-11-20\"");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync(
            "add-reminder",
            ("offsetMinutes", "120"));

        var refused = await UiPage.FromAsync(_host, page.Path, reply);
        Assert.Contains(
            refused.Texts("[role=alert]"),
            text => text.Contains("Choose Custom", StringComparison.Ordinal));
        Assert.Empty(await OffsetsAsync(id));
    }

    // The second named M1 regression, through the plain-language form.
    [Fact]
    public async Task Rescheduling_KeepsTheRepeatRule()
    {
        var id = await RepeatingAsync("FREQ=WEEKLY;BYDAY=FR");
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        await page.SubmitAsync("due", ("date", "2026-11-17"));

        Assert.Equal("FREQ=WEEKLY;BYDAY=FR", await RuleAsync(id));
        Assert.Equal(
            "2026-11-17",
            (await ItemAsync(id))["due"]!["date"]!.GetValue<string>());
    }

    [Fact]
    public async Task Missed_AndChanges_ReadInWords()
    {
        await _host.CreateItemAsync(
            """
            {"title":"Fired this morning","due":{"date":"2026-11-13"},
             "reminders":[{"offsetMinutes":540}]}
            """);

        var missed = await UiPage.OpenAsync(_host, "/missed");
        var changes = await UiPage.OpenAsync(_host, "/changes");

        Assert.Contains("reminded Today 09:00", missed.Texts(".fact"));
        var when = changes.Document
            .QuerySelector("table.changes tbody td:nth-child(2)")!
            .TextContent
            .Trim();
        Assert.Equal("Today 23:30", when);
    }
}
