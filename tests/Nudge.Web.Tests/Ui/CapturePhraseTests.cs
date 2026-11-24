using Nudge.Core;
using Nudge.Web.Components;

namespace Nudge.Web.Tests.Ui;

/// <summary>The date words a capture understands.</summary>
public sealed class CapturePhraseTests : IAsyncLifetime
{
    // A Monday.
    private static readonly DateOnly Today = new(2026, 11, 23);

    private readonly WebUiHost _host = new();

    public async Task InitializeAsync()
    {
        await _host.SignInAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("every day", "FREQ=DAILY", "2026-11-23")]
    [InlineData(
        "every weekday",
        "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR",
        "2026-11-23")]
    [InlineData("every friday", "FREQ=WEEKLY;BYDAY=FR", "2026-11-27")]
    [InlineData("EVERY  Friday", "FREQ=WEEKLY;BYDAY=FR", "2026-11-27")]
    [InlineData("every monday", "FREQ=WEEKLY;BYDAY=MO", "2026-11-23")]
    [InlineData("every sunday", "FREQ=WEEKLY;BYDAY=SU", "2026-11-29")]
    [InlineData("every week", "FREQ=WEEKLY;BYDAY=MO", "2026-11-23")]
    [InlineData(
        "every week on friday",
        "FREQ=WEEKLY;BYDAY=FR",
        "2026-11-27")]
    [InlineData(
        "every 2 weeks on friday",
        "FREQ=WEEKLY;INTERVAL=2;BYDAY=FR",
        "2026-11-27")]
    [InlineData("every month", "FREQ=MONTHLY", "2026-11-23")]
    [InlineData("every 3 months", "FREQ=MONTHLY;INTERVAL=3", "2026-11-23")]
    [InlineData("every 6 months", "FREQ=MONTHLY;INTERVAL=6", "2026-11-23")]
    [InlineData("every year", "FREQ=YEARLY", "2026-11-23")]
    public void Repeats_AreReadFromTheStart(
        string phrase,
        string rule,
        string start)
    {
        var read = CapturePhrase.Read($"{phrase} eat a snack", Today);

        Assert.Equal("eat a snack", read.Title);
        Assert.Null(read.Due);
        Assert.Equal(rule, read.Repeat!.Rule);
        Assert.Equal(RecurrenceMode.FixedSchedule, read.Repeat.Mode);
        Assert.Equal(DateOnly.Parse(start), read.Repeat.Start);
    }

    // From a Saturday the first Friday is six days on, and the two
    // weeks count from it rather than from the week of the capture.
    [Fact]
    public void EveryTwoWeeks_StartsOnTheNextOfThatDay()
    {
        var saturday = new DateOnly(2026, 11, 28);

        var read = CapturePhrase.Read(
            "every 2 weeks on friday pay rent",
            saturday);

        Assert.Equal(new DateOnly(2026, 12, 4), read.Repeat!.Start);
    }

    [Theory]
    [InlineData("today", "2026-11-23")]
    [InlineData("Tomorrow", "2026-11-24")]
    [InlineData("friday", "2026-11-27")]
    [InlineData("monday", "2026-11-23")]
    [InlineData("sunday", "2026-11-29")]
    public void OneOffDates_AreReadFromTheStart(
        string word,
        string date)
    {
        var read = CapturePhrase.Read($"{word} call the bank", Today);

        Assert.Equal("call the bank", read.Title);
        Assert.Equal(DateOnly.Parse(date), read.Due!.Date);
        Assert.Null(read.Due.Time);
        Assert.Null(read.Repeat);
    }

    [Theory]
    [InlineData("eat a snack every friday")]
    [InlineData("every friday")]
    [InlineData("friday")]
    [InlineData("today")]
    [InlineData("every")]
    [InlineData("every fridays eat a snack")]
    [InlineData("every 5 months eat a snack")]
    [InlineData("todays errands")]
    [InlineData("")]
    public void AnythingElse_IsAPlainTitle(string text)
    {
        var read = CapturePhrase.Read(text, Today);

        Assert.Equal(text, read.Title);
        Assert.Null(read.Due);
        Assert.Null(read.Repeat);
    }

    // The page highlights with this pattern, so it has to pick out
    // the same words the server takes off the title.
    [Theory]
    [InlineData("every friday eat a snack", "every friday")]
    [InlineData("every week on friday eat", "every week on friday")]
    [InlineData("every week on call", "every week")]
    [InlineData("every 2 weeks on monday pay", "every 2 weeks on monday")]
    [InlineData("every 3 months oil it", "every 3 months")]
    [InlineData("Tomorrow call the bank", "Tomorrow")]
    [InlineData("friday night dinner", "friday")]
    public void Words_AreWhatReadTakesOffTheTitle(
        string text,
        string words)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            CapturePhrase.Words,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.Equal(words, match.Value);
        Assert.Equal(
            text[words.Length..].Trim(),
            CapturePhrase.Read(text, Today).Title);
    }

    [Theory]
    [InlineData("/inbox", "capture")]
    [InlineData("/next", "quick-capture")]
    public async Task Capture_WithTheDateDismissed_IsAPlainTitle(
        string path,
        string form)
    {
        var page = await UiPage.OpenAsync(_host, path);

        await page.SubmitAsync(
            form,
            ("title", "friday night dinner"),
            (CapturePhrase.PlainField, "1"));

        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        Assert.Equal(["friday night dinner"], inbox.Texts("li.item > a"));
        Assert.Empty(inbox.Texts("li.item .fact"));
    }

    [Theory]
    [InlineData("/inbox", "capture")]
    [InlineData("/next", "quick-capture")]
    public async Task Capture_SetsTheDueDateAndRepeat(
        string path,
        string form)
    {
        var page = await UiPage.OpenAsync(_host, path);

        await page.SubmitAsync(form, ("title", "every friday eat a snack"));

        var inbox = await UiPage.OpenAsync(_host, "/inbox");
        Assert.Equal(["eat a snack"], inbox.Texts("li.item > a"));
        var facts = inbox.Texts("li.item .fact");
        Assert.Contains(
            facts,
            fact => fact.StartsWith("due ", StringComparison.Ordinal));
        Assert.Contains(
            facts,
            fact => fact.StartsWith(
                "Every week on Friday",
                StringComparison.Ordinal));
    }
}
