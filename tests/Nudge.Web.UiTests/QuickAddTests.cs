using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Nudge.Core;
using Nudge.Web.Data;
using static Microsoft.Playwright.Assertions;

namespace Nudge.Web.UiTests;

/// <summary>
/// The quick-add in a real Chromium: a capture stays on the view it
/// was made from, with a working Undo, and fits a phone's header.
/// </summary>
public sealed class QuickAddTests
{
    private const string Label = "Capture to inbox";

    private static readonly ViewportSize Phone =
        new() { Width = 375, Height = 812 };

    private static readonly ViewportSize Desktop =
        new() { Width = 1280, Height = 800 };

    private static async Task<IBrowserContext> ContextAsync(
        IBrowser browser,
        BrowserHost host,
        ViewportSize size,
        ColorScheme scheme = ColorScheme.Light)
    {
        var phone = size.Width < 640;
        return await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL = host.BaseAddress,
                IgnoreHTTPSErrors = true,
                ViewportSize = size,
                IsMobile = phone,
                HasTouch = phone,
                ColorScheme = scheme,
            });
    }

    private static async Task<long> InboxCountAsync(
        BrowserHost host,
        string title)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        return await db.Items.CountAsync(
            i => i.Title == title && i.State == ItemState.Inbox);
    }

    [PlaywrightFact]
    public async Task Capture_StaysOnTheView_AndUndoWorks()
    {
        await using var host = await BrowserHost.StartAsync();
        var (itemId, _) = await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await ContextAsync(browser, host, Phone);
        var page = await context.NewPageAsync();

        foreach (var path in new[] { "/next", $"/items/{itemId}" })
        {
            await page.GotoAsync(path);
            var heading = await page.Locator("h1").InnerTextAsync();
            var title = $"Invented thought from {path[1..5]}";
            var field = page.GetByLabel(Label);
            Assert.False(
                await field.EvaluateAsync<bool>(
                    "e => e === document.activeElement"),
                $"{path}: the quick-add took the focus");

            await field.FillAsync(title);
            await field.PressAsync("Enter");

            var notice = page.Locator(".notice");
            await Expect(notice).ToContainTextAsync("Create item");
            Assert.StartsWith(
                host.BaseAddress + path + "?change=",
                page.Url,
                StringComparison.OrdinalIgnoreCase);
            await Expect(page.Locator("h1")).ToHaveTextAsync(heading);
            await Expect(field).ToHaveValueAsync("");
            Assert.Equal(1, await InboxCountAsync(host, title));

            await notice.GetByRole(
                    AriaRole.Button,
                    new LocatorGetByRoleOptions { Name = "Undo", Exact = true })
                .ClickAsync();
            await Expect(notice).ToContainTextAsync("Undone");
            Assert.Equal(0, await InboxCountAsync(host, title));
        }
    }

    [PlaywrightFact]
    public async Task DoubleClick_FromAnotherView_IsCarriedOutOnce()
    {
        await using var host = await BrowserHost.StartAsync();
        await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await ContextAsync(browser, host, Desktop);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/next");
        const string Title = "Invented double click";

        await page.GetByLabel(Label).FillAsync(Title);
        await page.Locator(".quick-add button").DblClickAsync();

        await Expect(page.Locator(".notice"))
            .ToContainTextAsync("Create item");
        await Expect(page.Locator(".error")).ToHaveCountAsync(0);
        Assert.Equal(1, await InboxCountAsync(host, Title));
    }

    [PlaywrightFact]
    public async Task Refusal_IsInSight_AndKeepsTheText()
    {
        await using var host = await BrowserHost.StartAsync();
        var (itemId, _) = await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await ContextAsync(browser, host, Phone);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/items/{itemId}");
        var heading = await page.Locator("h1").InnerTextAsync();
        // Spaces pass the browser's "required" and are refused here.
        const string Blank = "   ";

        await page.GetByLabel(Label).FillAsync(Blank);
        await page.GetByLabel(Label).PressAsync("Enter");

        var error = page.Locator("header .error");
        await Expect(error).ToBeVisibleAsync();
        await Expect(page.GetByLabel(Label)).ToHaveValueAsync(Blank);
        await Expect(page.Locator("h1")).ToHaveTextAsync(heading);
        var box = await error.BoundingBoxAsync();
        Assert.True(box!.Y >= 0 && box.Y + box.Height <= Phone.Height);
        Assert.True(box.X >= 0 && box.X + box.Width <= Phone.Width);
        Assert.False(
            await page.EvaluateAsync<bool>(
                """
                () => document.documentElement.scrollWidth
                    > document.documentElement.clientWidth
                """),
            "the page scrolls sideways with the error shown");
    }

    // The item is deleted in another tab while its page is open: the
    // capture is kept, and the browser goes to the inbox with Undo.
    [PlaywrightFact]
    public async Task Capture_FromAPageSinceDeleted_LandsInTheInbox()
    {
        await using var host = await BrowserHost.StartAsync();
        var (itemId, _) = await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await ContextAsync(browser, host, Desktop);
        var stale = await context.NewPageAsync();
        await stale.GotoAsync($"/items/{itemId}");
        var other = await context.NewPageAsync();
        await other.GotoAsync($"/items/{itemId}");
        await other.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "Delete this item" })
            .ClickAsync();
        await Expect(other.Locator(".notice"))
            .ToContainTextAsync("Delete item");
        const string Title = "Invented late thought";

        await stale.GetByLabel(Label).FillAsync(Title);
        await stale.GetByLabel(Label).PressAsync("Enter");

        await Expect(stale.Locator("h1")).ToHaveTextAsync("Inbox");
        await Expect(stale.Locator(".notice"))
            .ToContainTextAsync("Create item");
        Assert.Equal(1, await InboxCountAsync(host, Title));
    }

    [PlaywrightFact]
    public async Task Inbox_HasOneCapture_WithItsFocus()
    {
        await using var host = await BrowserHost.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await ContextAsync(browser, host, Desktop);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/inbox");

        await Expect(page.GetByLabel("Capture")).ToHaveCountAsync(1);
        await Expect(page.GetByLabel("Capture")).ToBeFocusedAsync();
        await Expect(page.Locator(".quick-add")).ToHaveCountAsync(0);

        await page.GotoAsync("/nothing-here");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Not found");
        await Expect(page.Locator(".quick-add")).ToHaveCountAsync(0);
    }

    [PlaywrightFact]
    public async Task EveryWidth_DoesNotScrollSideways()
    {
        await using var host = await BrowserHost.StartAsync();
        var (itemId, _) = await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });

        foreach (var (name, size) in new[]
        {
            ("phone", Phone),
            ("narrow", new ViewportSize { Width = 320, Height = 640 }),
            ("tablet", new ViewportSize { Width = 700, Height = 900 }),
            ("desktop", Desktop),
        })
        {
            foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
            {
                await using var context = await ContextAsync(
                    browser,
                    host,
                    size,
                    scheme);
                var page = await context.NewPageAsync();
                foreach (var path in new[]
                {
                    "/next",
                    $"/items/{itemId}",
                    "/inbox",
                })
                {
                    await page.GotoAsync(path);
                    await Expect(page.Locator("h1")).ToBeVisibleAsync();
                    Assert.False(
                        await page.EvaluateAsync<bool>(
                            """
                            () => document.documentElement.scrollWidth
                                > document.documentElement.clientWidth
                            """),
                        $"{name} {path}: the page scrolls sideways");
                }
            }
        }
    }
}
