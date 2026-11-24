using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Nudge.Core;
using Nudge.Web.Data;
using static Microsoft.Playwright.Assertions;

namespace Nudge.Web.UiTests;

/// <summary>
/// Completing an item low on a long list at phone size: the notice
/// and its Undo are in sight, the rows can still be reached, and
/// Undo brings the item back.
/// </summary>
public sealed class VisibleUndoTests
{
    private const string Prefix = "Invented errand";

    /// <summary>Whether the element is wholly in the viewport.
    /// </summary>
    private const string InViewport = """
        e => {
          const box = e.getBoundingClientRect();
          return box.top >= 0
            && box.left >= 0
            && box.bottom <= window.innerHeight
            && box.right <= document.documentElement.clientWidth;
        }
        """;

    /// <summary>Scrolls to the end of the page, then how far the last
    /// row's bottom is above the notice's top.</summary>
    private const string LastRowClearance = """
        () => {
          window.scrollTo(0, document.documentElement.scrollHeight);
          const rows = document.querySelectorAll('li.item');
          const row = rows[rows.length - 1].getBoundingClientRect();
          const notice = document.querySelector('.notice')
            .getBoundingClientRect();
          return notice.top - row.bottom;
        }
        """;

    [PlaywrightFact]
    public async Task CompletingLowOnAList_KeepsUndoInSight()
    {
        await using var host = await BrowserHost.StartAsync();
        await PhoneLayoutTests.SeedAsync(host);
        await SeedLongListAsync(host);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL = host.BaseAddress,
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = 375, Height = 812 },
                IsMobile = true,
                HasTouch = true,
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/next");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Next actions");

        var errands = page.Locator(
            "li.item",
            new PageLocatorOptions { HasText = Prefix });
        var last = errands.Last;
        var title = (await last.Locator("a").First.InnerTextAsync()).Trim();
        var row = page.Locator(
            "li.item",
            new PageLocatorOptions { HasText = title });
        await Expect(row).ToHaveCountAsync(1);

        await last.ScrollIntoViewIfNeededAsync();
        await row.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "Complete" })
            .ClickAsync();
        var notice = page.Locator(".notice");
        await Expect(notice).ToContainTextAsync("Complete item");
        await Expect(row).ToHaveCountAsync(0);
        Assert.True(
            await page.EvaluateAsync<double>("() => window.scrollY") > 0,
            "the page should still be scrolled down");

        var undo = notice.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Undo", Exact = true });
        Assert.True(
            await notice.EvaluateAsync<bool>(InViewport),
            "the notice is outside the viewport");
        Assert.True(
            await undo.EvaluateAsync<bool>(InViewport),
            "Undo is outside the viewport");
        var size = await undo.BoundingBoxAsync();
        Assert.True(size!.Height >= 44, $"Undo is {size.Height}px high");

        var clearance = await page.EvaluateAsync<double>(LastRowClearance);
        Assert.True(
            clearance >= 0,
            $"the notice covers the last row by {-clearance}px");

        // A trial click fails if anything else would receive the tap.
        await page.Locator("li.item").Last
            .GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "Complete" })
            .ClickAsync(new LocatorClickOptions { Trial = true });

        await undo.ClickAsync();
        await Expect(notice).ToContainTextAsync("Undone");
        await Expect(row).ToHaveCountAsync(1);
    }

    /// <summary>Gives the notice text long enough to reach its cap
    /// and wrap Undo below it, then the notice's height, with its
    /// sticky offset, and the page's scroll padding at the bottom.
    /// </summary>
    private const string TallestNotice = """
        () => {
          const notice = document.querySelector('.notice');
          notice.querySelector('span').textContent =
            'Invented notice text '.repeat(200);
          const offset = parseFloat(getComputedStyle(notice).bottom);
          const reserve = parseFloat(
            getComputedStyle(document.documentElement)
              .scrollPaddingBottom);
          return [notice.getBoundingClientRect().height + offset, reserve];
        }
        """;

    // At 320 px with a larger root font, a long notice wraps Undo
    // onto a row of its own: the scroll padding still covers it, so
    // a control scrolled to by keyboard is not under the notice. Its
    // text can be scrolled by keyboard, being focusable.
    [PlaywrightFact]
    public async Task TallestNotice_IsCoveredByTheScrollPadding()
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
                ViewportSize = new ViewportSize { Width = 320, Height = 600 },
                IsMobile = true,
                HasTouch = true,
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/next?change=1");
        var notice = page.Locator(".notice");
        await Expect(notice).ToBeVisibleAsync();
        await page.EvaluateAsync(
            "() => document.documentElement.style.fontSize = '20px'");

        var sizes = await page.EvaluateAsync<double[]>(TallestNotice);
        var undo = notice.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Undo", Exact = true });
        var text = await notice.Locator("span").BoundingBoxAsync();
        Assert.True(
            (await undo.BoundingBoxAsync())!.Y >= text!.Y + text.Height,
            "Undo did not wrap below the text");
        Assert.True(
            sizes[1] >= sizes[0],
            $"the notice takes {sizes[0]}px; the padding is {sizes[1]}px");

        await notice.Locator("span").FocusAsync();
        Assert.True(
            await notice.Locator("span").EvaluateAsync<bool>(
                "e => document.activeElement === e && e.tabIndex === 0"),
            "the notice text cannot take keyboard focus");
    }

    /// <summary>Enough invented next actions to scroll a phone.
    /// </summary>
    private static async Task SeedLongListAsync(BrowserHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        db.Source = "seed";
        db.Description = "Invented data for the visible undo test";
        for (var i = 1; i <= 24; i++)
        {
            db.Items.Add(
                new Item($"{Prefix} {i:00}")
                {
                    State = ItemState.NextAction,
                });
        }

        await db.SaveChangesAsync();
    }
}
