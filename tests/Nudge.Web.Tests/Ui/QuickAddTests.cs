using System.Net;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// The quick-add in the signed-in header: where it is shown, and that
/// a capture through it is the inbox's write, from whichever page.
/// </summary>
public sealed class QuickAddTests : IAsyncLifetime
{
    private const string Form = "quick-capture";

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

    private static string PathOf(HttpResponseMessage reply)
    {
        return new Uri(new Uri("https://localhost"), reply.Headers.Location!)
            .PathAndQuery;
    }

    private Task<Guid> ItemAsync()
    {
        return _host.CreateItemAsync("""{"title":"Oil the hinges"}""");
    }

    private async Task<List<string>> InboxAsync()
    {
        return (await UiPage.OpenAsync(_host, "/inbox")).Texts("li.item > a");
    }

    [Theory]
    [InlineData("/next")]
    [InlineData("/projects")]
    [InlineData("/waiting")]
    [InlineData("/someday")]
    [InlineData("/review")]
    [InlineData("/missed")]
    [InlineData("/changes")]
    public async Task EveryOtherView_HasIt_NotAutofocused(string path)
    {
        var page = await UiPage.OpenAsync(_host, path);

        Assert.True(page.HasForm(Form));
        Assert.Empty(page.Document.QuerySelectorAll("[autofocus]"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/inbox")]
    [InlineData("/INBOX")]
    [InlineData("/inbox/")]
    public async Task Inbox_HasOnlyItsOwnCapture(string path)
    {
        var page = await UiPage.OpenAsync(_host, path);

        Assert.True(page.HasForm("capture"));
        Assert.False(page.HasForm(Form));
        Assert.Single(page.Document.QuerySelectorAll("input[name=title]"));
        Assert.Single(page.Document.QuerySelectorAll("[autofocus]"));
    }

    [Theory]
    [InlineData("/nothing-here")]
    [InlineData("/not-found")]
    public async Task NotFound_DoesNotHaveIt(string path)
    {
        var reply = await _host.GetAsync(path);
        var html = await reply.Content.ReadAsStringAsync();

        Assert.Contains("<h1>Not found</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Form, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingItemAndProject_DoNotHaveIt()
    {
        foreach (var kind in new[] { "items", "projects" })
        {
            var reply = await _host.GetAsync($"/{kind}/{Guid.NewGuid()}");
            var html = await reply.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, reply.StatusCode);
            Assert.Contains("<h1>Not found</h1>", html, StringComparison.Ordinal);
            Assert.DoesNotContain(Form, html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("/access-denied")]
    [InlineData("/signed-out")]
    [InlineData("/sign-in-unavailable")]
    public async Task PagesForAnyone_DoNotHaveIt(string path)
    {
        foreach (var withCookies in new[] { false, true })
        {
            var reply = await _host.GetAsync(path, withCookies: withCookies);
            var html = await reply.Content.ReadAsStringAsync();

            Assert.Contains("<h1>", html, StringComparison.Ordinal);
            Assert.DoesNotContain(Form, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FromAStaleMissingItemPage_IsNotLost()
    {
        var id = await ItemAsync();
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        var fields = page.Fields(Form);
        await _host.ApiAsync("DELETE", $"/api/items/{id}", null);

        var reply = await page.SendAsync(fields, ("title", "Late thought"));

        Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);
        Assert.StartsWith(
            "/inbox?change=",
            PathOf(reply),
            StringComparison.Ordinal);
        Assert.Equal(["Late thought"], await InboxAsync());
        var after = await UiPage.OpenAsync(_host, PathOf(reply));
        Assert.True(after.HasForm("undo-notice"));
    }

    // The repeat goes by the form's own target, the page now gone, so
    // it lands on not-found; what matters is that it writes nothing.
    [Fact]
    public async Task SentTwiceFromAStaleMissingItemPage_IsCarriedOutOnce()
    {
        var id = await ItemAsync();
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        var fields = page.Fields(Form);
        fields["title"] = "Late thought";
        await _host.ApiAsync("DELETE", $"/api/items/{id}", null);

        await page.SendAsync(fields);
        var changes = _host.Count("Changes");
        var again = await page.SendAsync(fields);

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal(changes, _host.Count("Changes"));
        Assert.Equal(["Late thought"], await InboxAsync());
    }

    [Fact]
    public async Task RefusedFromAStaleMissingItemPage_ShowsTheError()
    {
        var id = await ItemAsync();
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        var fields = page.Fields(Form);
        await _host.ApiAsync("DELETE", $"/api/items/{id}", null);
        var changes = _host.Count("Changes");

        var reply = await page.SendAsync(fields, ("title", "   "));

        var shown = await UiPage.FromAsync(_host, page.Path, reply);
        Assert.StartsWith(
            "Title:",
            Assert.Single(shown.Texts("header [role=alert]")),
            StringComparison.Ordinal);
        Assert.Equal(changes, _host.Count("Changes"));
    }

    // Only the capture is let through on a missing page: the page's
    // own forms are gone with it, and are refused as before.
    [Fact]
    public async Task OtherFormFromAStaleMissingItemPage_IsRefused()
    {
        var id = await ItemAsync();
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        var fields = page.Fields("delete");
        await _host.ApiAsync("DELETE", $"/api/items/{id}", null);
        var changes = _host.Count("Changes");

        var reply = await page.SendAsync(fields);

        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Equal(changes, _host.Count("Changes"));
    }

    [Theory]
    [InlineData("/next")]
    [InlineData("/projects")]
    [InlineData("/changes")]
    public async Task Capture_StaysOnThePage_AndUndoTakesItOut(string path)
    {
        var page = await UiPage.OpenAsync(_host, path);

        var reply = await page.SubmitAsync(Form, ("title", "  Buy stamps "));

        Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);
        Assert.StartsWith($"{path}?change=", PathOf(reply), StringComparison.Ordinal);
        Assert.Equal(["Buy stamps"], await InboxAsync());
        Assert.Equal("web", _host.LastChange()!.Value.Source);
        Assert.Equal("Create item", _host.LastChange()!.Value.Description);

        var after = await UiPage.OpenAsync(_host, PathOf(reply));
        Assert.Equal(
            HttpStatusCode.Redirect,
            (await after.SubmitAsync("undo-notice")).StatusCode);
        Assert.Empty(await InboxAsync());
    }

    [Fact]
    public async Task Capture_FromAPageWithAChange_ReplacesTheChange()
    {
        var first = await UiPage.OpenAsync(_host, "/next");
        var landed = PathOf(
            await first.SubmitAsync(Form, ("title", "First thought")));
        var page = await UiPage.OpenAsync(_host, landed);

        var reply = await page.SubmitAsync(Form, ("title", "Second thought"));

        var id = _host.LastChange()!.Value.Id;
        Assert.Equal($"/next?change={id}", PathOf(reply));
        Assert.Equal(2, (await InboxAsync()).Count);
    }

    [Fact]
    public async Task SentTwice_FromAnotherView_IsCarriedOutOnce()
    {
        var page = await UiPage.OpenAsync(_host, "/next");
        var fields = page.Fields(Form);
        fields["title"] = "Buy stamps";

        var first = await page.SendAsync(fields);
        var changes = _host.Count("Changes");
        var second = await page.SendAsync(fields);

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal(PathOf(first), PathOf(second));
        Assert.Equal(changes, _host.Count("Changes"));
        Assert.Equal(["Buy stamps"], await InboxAsync());
    }

    [Fact]
    public async Task OnTheItemPage_LeavesTheItemAndItsFormsAlone()
    {
        var id = await ItemAsync();
        var before = await _host.ItemJsonAsync(id);
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");

        var reply = await page.SubmitAsync(Form, ("title", "Buy stamps"));

        Assert.StartsWith(
            $"/items/{id}?change=",
            PathOf(reply),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await _host.ItemJsonAsync(id));
        var after = await UiPage.OpenAsync(_host, PathOf(reply));
        Assert.True(after.HasForm("delete"));
        Assert.True(after.HasForm(Form));
        var names = after.Document
            .QuerySelectorAll("input[name=_handler]")
            .Select(input => input.GetAttribute("value"))
            .ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public async Task Refused_ShowsTheError_AndKeepsThePage()
    {
        var id = await ItemAsync();
        var page = await UiPage.OpenAsync(_host, $"/items/{id}");
        var changes = _host.Count("Changes");

        var reply = await page.SubmitAsync(Form, ("title", "   "));

        var shown = await UiPage.FromAsync(_host, page.Path, reply);
        Assert.StartsWith(
            "Title:",
            Assert.Single(shown.Texts("header [role=alert]")),
            StringComparison.Ordinal);
        Assert.Equal(["Oil the hinges"], shown.Texts("h1"));
        Assert.True(shown.HasForm("delete"));
        Assert.Equal(changes, _host.Count("Changes"));
    }
}
