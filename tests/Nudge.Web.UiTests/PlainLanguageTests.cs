using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Nudge.Core;
using Nudge.Web.Data;
using static Microsoft.Playwright.Assertions;

namespace Nudge.Web.UiTests;

/// <summary>
/// The repeat and reminder choices in a real browser at phone size,
/// on invented data and with no script of the app's: a preset saves,
/// the custom path saves, a rule that is no preset stays, and the
/// page still fits.
/// </summary>
public sealed class PlainLanguageTests
{
    private const string NoPresetRule = "FREQ=MONTHLY;BYMONTHDAY=1,15";

    private static async Task<Guid> SeedAsync(
        BrowserHost host,
        string? rule)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        db.Source = "seed";
        db.Description = "Invented data for the plain language test";
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var item = new Item("Water the ferns")
        {
            State = ItemState.NextAction,
        };
        item.SetDue(day);
        if (rule is not null)
        {
            item.Repeat(
                RecurrenceRule.Parse(rule),
                RecurrenceMode.FixedSchedule,
                day);
        }

        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static async Task<string?> StoredRuleAsync(
        BrowserHost host,
        Guid id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        var item = await db.Items.FindAsync(id);
        return item!.Recurrence?.Rule.ToString();
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
                JavaScriptEnabled = true,
            });
    }

    private static async Task ExpectFitsAsync(IPage page)
    {
        var problems = await page.EvaluateAsync<string[]>(
            PhoneLayoutTests.Problems);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
    }

    private static ILocator Button(
        IPage page,
        string name)
    {
        return page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = name, Exact = true });
    }

    [PlaywrightFact]
    public async Task Presets_AndTheCustomPath_WorkInTheBrowser()
    {
        await using var host = await BrowserHost.StartAsync();
        var id = await SeedAsync(host, null);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await PhoneAsync(browser, host);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Inbox");

        await page.GotoAsync($"/items/{id}");
        await ExpectFitsAsync(page);
        await Expect(page.Locator("input[name=rule]")).ToBeHiddenAsync();

        await page.Locator("select[name=preset]")
            .SelectOptionAsync("monthly");
        await Button(page, "Set the repeat rule").ClickAsync();
        await Expect(page.Locator(".notice")).ToBeVisibleAsync();
        Assert.Equal("FREQ=MONTHLY", await StoredRuleAsync(host, id));
        await Expect(page.Locator(".facts").First)
            .ToContainTextAsync("Every month, on a fixed schedule");

        await page.Locator("select[name=preset]")
            .SelectOptionAsync("custom");
        await page.Locator("summary", new() { HasText = "Custom rule" })
            .ClickAsync();
        await ExpectFitsAsync(page);
        await page.Locator("input[name=rule]").FillAsync(NoPresetRule);
        await Button(page, "Set the repeat rule").ClickAsync();
        await Expect(page.Locator(".facts").First)
            .ToContainTextAsync(NoPresetRule);
        Assert.Equal(NoPresetRule, await StoredRuleAsync(host, id));

        await Expect(page.Locator("select[name=preset]"))
            .ToHaveValueAsync("custom");
        await Expect(page.Locator("input[name=rule]")).ToBeVisibleAsync();
        await Expect(page.Locator("input[name=rule]"))
            .ToHaveValueAsync(NoPresetRule);

        await page.Locator("select[name=reminder]")
            .SelectOptionAsync("day-before-1800");
        await Button(page, "Add reminder").ClickAsync();
        await Expect(page.Locator("section.group li").First)
            .ToContainTextAsync("18:00 the day before");
        Assert.Equal(NoPresetRule, await StoredRuleAsync(host, id));
        await ExpectFitsAsync(page);
    }
}
