using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Nudge.Web.UiTests;

/// <summary>
/// The web UI in a real Chromium: sign in, capture, process, add a
/// context, complete from the next actions, undo, and open every
/// view. Navigation is by clicking links and forms are sent with
/// their buttons, so Blazor's enhanced navigation and enhanced forms
/// are used throughout: a value set on the window at the start must
/// still be there at the end, which a full page load would lose.
/// On failure a trace and a screenshot are saved to
/// <c>playwright-artifacts</c> next to the test assembly.
/// </summary>
public sealed class SmokeTests
{
    private const string Title = "Call the plumber";

    /// <summary>The views, by the name of their link and their
    /// heading.</summary>
    private static readonly (string Link, string Heading)[] Views =
    [
        ("Inbox", "Inbox"),
        ("Next", "Next actions"),
        ("Projects", "Projects"),
        ("Waiting", "Waiting for"),
        ("Someday", "Someday/maybe"),
        ("Review", "Weekly review"),
        ("Missed", "Missed reminders"),
        ("Changes", "Changes"),
    ];

    private static string Artifacts =>
        Path.Combine(AppContext.BaseDirectory, "playwright-artifacts");

    private static ILocator Nav(
        IPage page,
        string name)
    {
        return page
            .Locator("nav.views")
            .GetByRole(
                AriaRole.Link,
                new LocatorGetByRoleOptions { Name = name, Exact = true });
    }

    private static ILocator Button(
        ILocator scope,
        string name)
    {
        return scope.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = name, Exact = true });
    }

    private static async Task GoAsync(
        IPage page,
        string link,
        string heading)
    {
        await Nav(page, link).ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync(heading);
        await Expect(page.Locator("h1")).Not.ToHaveTextAsync("Not found");
    }

    private static async Task ExpectNoticeAsync(
        IPage page,
        string text)
    {
        await Expect(page.Locator(".notice")).ToContainTextAsync(text);
    }

    [PlaywrightFact]
    public async Task OwnersDay_InTheBrowser()
    {
        await using var host = await BrowserHost.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL = host.BaseAddress,

                // The certificate is made for the run; see BrowserHost.
                IgnoreHTTPSErrors = true,
            });
        await context.Tracing.StartAsync(
            new TracingStartOptions { Screenshots = true, Snapshots = true });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                errors.Add(message.Text);
            }
        };
        page.PageError += (_, error) => errors.Add(error);

        try
        {
            await RunAsync(page);
            Assert.Empty(errors);
            await context.Tracing.StopAsync();
        }
        catch
        {
            Directory.CreateDirectory(Artifacts);
            await page.ScreenshotAsync(
                new PageScreenshotOptions
                {
                    Path = Path.Combine(Artifacts, "smoke.png"),
                    FullPage = true,
                });
            await context.Tracing.StopAsync(
                new TracingStopOptions
                {
                    Path = Path.Combine(Artifacts, "smoke-trace.zip"),
                });
            File.WriteAllLines(
                Path.Combine(Artifacts, "console-errors.txt"),
                errors);
            File.WriteAllLines(
                Path.Combine(Artifacts, "server-log.txt"),
                host.Log.Lines);
            throw;
        }
    }

    private static async Task RunAsync(IPage page)
    {
        // Signing in: the app sends the browser to the fake Google,
        // which sends it straight back, signed in, to the inbox.
        await page.GotoAsync("/");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Inbox");
        await page.EvaluateAsync("() => { window.nudgeSmoke = 'kept'; }");

        // A context to give the item.
        await GoAsync(page, "Next", "Next actions");
        await page.GetByLabel("New context").FillAsync("phone");
        await Button(page.Locator("body"), "Add context").ClickAsync();
        await ExpectNoticeAsync(page, "Create context");

        // Capture.
        await GoAsync(page, "Inbox", "Inbox");
        await page.GetByLabel("Capture").FillAsync(Title);
        await Button(page.Locator("body"), "Add to inbox").ClickAsync();
        await ExpectNoticeAsync(page, "Create item");
        var row = page.Locator("li.item", new PageLocatorOptions { HasText = Title });
        await Expect(row).ToHaveCountAsync(1);

        // Process it to a next action, with a double click: the form
        // is gone after the first, and the second must still be
        // answered as the first, with no error shown.
        await row.GetByLabel("List").SelectOptionAsync("NextAction");
        await Button(row, "Process").DblClickAsync();
        await ExpectNoticeAsync(page, "Edit item");
        await Expect(page.Locator(".error")).ToHaveCountAsync(0);
        await Expect(page.Locator(".empty")).ToHaveTextAsync("The inbox is empty.");

        // Give it the context, on its own page.
        await GoAsync(page, "Next", "Next actions");
        await page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions { Name = Title, Exact = true })
            .ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync(Title);
        await page.GetByLabel("Context", new PageGetByLabelOptions { Exact = true })
            .SelectOptionAsync(new SelectOptionValue { Label = "phone" });
        await Button(page.Locator("body"), "Add context").ClickAsync();
        await ExpectNoticeAsync(page, "Add context to item");

        // On the next actions, under the context; complete it there.
        await GoAsync(page, "Next", "Next actions");
        var group = page.Locator(
            "section.group",
            new PageLocatorOptions { HasText = "phone" });
        var next = group.Locator(
            "li.item",
            new LocatorLocatorOptions { HasText = Title });
        await Expect(next).ToHaveCountAsync(1);
        await Button(next, "Complete").ClickAsync();
        await ExpectNoticeAsync(page, "Complete item");
        await Expect(page.Locator("li.item", new PageLocatorOptions { HasText = Title }))
            .ToHaveCountAsync(0);

        // Undo from the notice brings it back.
        await Button(page.Locator(".notice"), "Undo").ClickAsync();
        await ExpectNoticeAsync(page, "Undone");
        await Expect(next).ToHaveCountAsync(1);

        // Every view opens, with no error shown.
        foreach (var (link, heading) in Views)
        {
            await GoAsync(page, link, heading);
            await Expect(page.Locator(".error")).ToHaveCountAsync(0);
        }

        var seeded = page.Locator(
            "li.item",
            new PageLocatorOptions { HasText = "Learn to sail" });
        await Expect(seeded).ToHaveCountAsync(0);
        await GoAsync(page, "Someday", "Someday/maybe");
        await Expect(seeded).ToHaveCountAsync(1);

        // Still the document the test started on: every step above
        // was an enhanced navigation or an enhanced form post.
        Assert.Equal(
            "kept",
            await page.EvaluateAsync<string>("() => window.nudgeSmoke"));
    }
}
