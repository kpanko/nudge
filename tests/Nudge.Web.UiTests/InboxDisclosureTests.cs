using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Nudge.Web.UiTests;

/// <summary>
/// The inbox at phone size (375x812), on invented data: each row's
/// processing controls stay hidden until that row is opened, work
/// from the keyboard, move the item, and a refusal stays in sight.
/// </summary>
public sealed class InboxDisclosureTests
{
    private const string First = "Idea for the garden shed";
    private const string Second = "Look up bike tyre sizes";

    private static ILocator Row(
        IPage page,
        string title)
    {
        return page.Locator(
            "li.item",
            new PageLocatorOptions { HasText = title });
    }

    private static ILocator Summary(ILocator row)
    {
        return row.Locator("details.organize > summary");
    }

    private static ILocator Process(ILocator row)
    {
        return row.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Process", Exact = true });
    }

    private static async Task ExpectClosedAsync(ILocator row)
    {
        await Expect(row.Locator("select[name=list]")).ToBeHiddenAsync();
        await Expect(row.Locator("select[name=projectId]")).ToBeHiddenAsync();
        await Expect(row.Locator("input[name=deferUntil]")).ToBeHiddenAsync();
        await Expect(row.Locator("button:text-is('Process')"))
            .ToBeHiddenAsync();
    }

    private static async Task ExpectOpenAsync(ILocator row)
    {
        await Expect(row.GetByLabel("List")).ToBeVisibleAsync();
        await Expect(row.GetByLabel("Project")).ToBeVisibleAsync();
        await Expect(row.GetByLabel("Defer until")).ToBeVisibleAsync();
        await Expect(Process(row)).ToBeVisibleAsync();
    }

    private static async Task ExpectFitsAsync(IPage page)
    {
        var problems = await page.EvaluateAsync<string[]>(
            PhoneLayoutTests.Problems);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
    }

    private static async Task<IBrowserContext> PhoneAsync(
        IBrowser browser,
        BrowserHost host)
    {
        return await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL = host.BaseAddress,
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = 375, Height = 812 },
                IsMobile = true,
                HasTouch = true,
            });
    }

    [PlaywrightFact]
    public async Task Controls_OpenOneRowAtATime_AndFitAPhone()
    {
        await using var host = await BrowserHost.StartAsync();
        await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await PhoneAsync(browser, host);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/inbox");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Inbox");

        var first = Row(page, First);
        var second = Row(page, Second);
        await ExpectClosedAsync(first);
        await ExpectClosedAsync(second);
        await Expect(first.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "Complete" }))
            .ToBeVisibleAsync();
        await Expect(first.GetByRole(
                AriaRole.Link,
                new LocatorGetByRoleOptions { Name = First }))
            .ToBeVisibleAsync();
        await ExpectFitsAsync(page);

        // By keyboard: Tab from the title reaches the summary, with a
        // visible focus ring, and Enter opens it.
        await first.GetByRole(
                AriaRole.Link,
                new LocatorGetByRoleOptions { Name = First })
            .FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        var summary = Summary(first);
        await Expect(summary).ToBeFocusedAsync();
        Assert.True(
            await summary.EvaluateAsync<bool>(
                """
                e => e.matches(':focus-visible')
                  && getComputedStyle(e).outlineStyle === 'solid'
                  && parseFloat(getComputedStyle(e).outlineWidth) >= 2
                """),
            "the focused summary shows no focus ring");
        await page.Keyboard.PressAsync("Enter");

        await ExpectOpenAsync(first);
        await ExpectClosedAsync(second);
        await ExpectFitsAsync(page);

        await page.Keyboard.PressAsync("Enter");
        await ExpectClosedAsync(first);
    }

    [PlaywrightFact]
    public async Task Processing_MovesTheItem()
    {
        await using var host = await BrowserHost.StartAsync();
        await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await PhoneAsync(browser, host);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/inbox");

        var row = Row(page, First);
        await Summary(row).ClickAsync();
        await row.GetByLabel("List").SelectOptionAsync("Someday");
        await Process(row).ClickAsync();

        await Expect(page.Locator(".notice")).ToContainTextAsync("Edit item");
        await Expect(row).ToHaveCountAsync(0);
        await Expect(Row(page, Second)).ToHaveCountAsync(1);

        await page.GotoAsync("/someday");
        await Expect(Row(page, First)).ToHaveCountAsync(1);
    }

    [PlaywrightFact]
    public async Task RefusedProcess_StaysInSight_EvenWhenClosed()
    {
        await using var host = await BrowserHost.StartAsync();
        await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await PhoneAsync(browser, host);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/inbox");

        var row = Row(page, First);
        await Summary(row).ClickAsync();

        // A date field cannot hold a bad date, so make it a text field.
        await row.Locator("input[name=deferUntil]").EvaluateAsync(
            "e => { e.type = 'text'; e.value = 'soon'; }");
        await Process(row).ClickAsync();

        var error = row.Locator(".error");
        await Expect(error).ToHaveTextAsync("Choose a list, and a valid date.");
        await Expect(error).ToBeVisibleAsync();
        await Expect(row).ToHaveCountAsync(1);

        // Left open to correct, and only this row.
        await ExpectOpenAsync(row);
        await ExpectClosedAsync(Row(page, Second));

        // Closing the controls does not hide the refusal.
        await Summary(row).ClickAsync();
        await ExpectClosedAsync(row);
        await Expect(error).ToBeVisibleAsync();
    }
}
