using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Nudge.Core;
using Nudge.Web.Data;
using static Microsoft.Playwright.Assertions;

namespace Nudge.Web.UiTests;

/// <summary>
/// The capture box shows which words it reads as a date, and a
/// backspace right after them keeps them as text.
/// </summary>
public sealed class CaptureHighlightTests
{
    private const string Label = "Capture to inbox";

    private static async Task<Item> ItemAsync(
        BrowserHost host,
        string title)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        return await db.Items.SingleAsync(i => i.Title == title);
    }

    [PlaywrightFact]
    public async Task DateWords_AreShown_AndBackspaceDismissesThem()
    {
        await using var host = await BrowserHost.StartAsync();
        await PhoneLayoutTests.SeedAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL = host.BaseAddress,
                IgnoreHTTPSErrors = true,
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/next");
        var field = page.GetByLabel(Label);
        var date = page.Locator(".quick-add .capture-date");

        await field.PressSequentiallyAsync("every friday eat a snack");
        await Expect(date).ToHaveTextAsync("every friday");

        await field.PressAsync("Enter");
        await Expect(page.Locator(".notice"))
            .ToContainTextAsync("Create item");
        await Expect(field).ToHaveValueAsync("");
        await Expect(date).ToHaveCountAsync(0);
        var parsed = await ItemAsync(host, "eat a snack");
        Assert.NotNull(parsed.DueDate);
        Assert.NotNull(parsed.Recurrence);

        await field.PressSequentiallyAsync("friday ");
        await Expect(date).ToHaveTextAsync("friday");
        await field.PressAsync("Backspace");
        await Expect(date).ToHaveCountAsync(0);
        await Expect(field).ToHaveValueAsync("friday ");

        await field.PressSequentiallyAsync("night dinner");
        await Expect(date).ToHaveCountAsync(0);
        await field.PressAsync("Enter");
        await Expect(field).ToHaveValueAsync("");
        await Expect(page.Locator(".notice"))
            .ToContainTextAsync("Create item");
        var plain = await ItemAsync(host, "friday night dinner");
        Assert.Null(plain.DueDate);
        Assert.Null(plain.Recurrence);

        await field.PressSequentiallyAsync("tomorrow ");
        await Expect(date).ToHaveTextAsync("tomorrow");
    }
}
