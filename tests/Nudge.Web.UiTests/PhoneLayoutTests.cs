using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Nudge.Core;
using Nudge.Web.Data;
using static Microsoft.Playwright.Assertions;

namespace Nudge.Web.UiTests;

/// <summary>
/// Every view at phone size (375x812), on invented data that includes
/// a long unbroken title: nothing scrolls sideways, the header is
/// compact, and buttons and fields are large enough to touch.
/// </summary>
public sealed class PhoneLayoutTests
{
    /// <summary>What is wrong with the page, one line each.</summary>
    internal const string Problems = """
        () => {
          const problems = [];
          const root = document.documentElement;
          if (root.scrollWidth > root.clientWidth) {
            problems.push(
              `page is ${root.scrollWidth}px wide in ${root.clientWidth}`);
          }

          const box = e => e.getBoundingClientRect();
          const middle = e => (box(e).top + box(e).bottom) / 2;
          const brand = document.querySelector('header .brand');
          const out = document.querySelector('header .sign-out button');
          if (Math.abs(middle(brand) - middle(out)) > 2) {
            problems.push('brand and sign out are on different rows');
          }

          const nav = document.querySelector('nav.views');
          const links = [...nav.querySelectorAll('a')];
          if (links.length !== 8) {
            problems.push(`${links.length} view links`);
          }
          if (new Set(links.map(a => Math.round(box(a).top))).size !== 1) {
            problems.push('view links wrap');
          }
          if (nav.scrollWidth <= nav.clientWidth) {
            problems.push('view links do not scroll');
          }
          if (box(nav).top < box(brand).bottom - 2) {
            problems.push('view links overlap the brand');
          }
          if (box(document.querySelector('header.top')).height > 104) {
            problems.push('header is over 104px high');
          }

          const controls = document.querySelectorAll(
            'button, select, textarea, summary, nav.views a,'
            + ' .facts > a.fact, input:not([type=hidden])');
          for (const control of controls) {
            if (!control.getClientRects().length) {
              continue;
            }
            const target = control.type === 'checkbox'
              ? control.closest('label')
              : control;
            if (box(target).height < 44) {
              const name = control.name || control.textContent.trim();
              problems.push(
                `${control.tagName} ${name}: ${box(target).height}px high`);
            }
          }
          return problems;
        }
        """;

    /// <summary>Whether the current view's link is wholly within the
    /// scrolling row after a full page load.</summary>
    private const string ActiveInSight = """
        () => {
          const nav = document.querySelector('nav.views')
            .getBoundingClientRect();
          const link = document.querySelector('nav.views a.active')
            .getBoundingClientRect();
          return link.left >= nav.left - 1 && link.right <= nav.right + 1;
        }
        """;

    private static readonly (string Path, string Active)[] Views =
    [
        ("/", "Inbox"),
        ("/inbox", "Inbox"),
        ("/next", "Next"),
        ("/projects", "Projects"),
        ("/waiting", "Waiting"),
        ("/someday", "Someday"),
        ("/review", "Review"),
        ("/missed", "Missed"),
        ("/changes", "Changes"),
    ];

    [PlaywrightFact]
    public async Task EveryView_FitsAPhone()
    {
        await using var host = await BrowserHost.StartAsync();
        var (itemId, projectId) = await SeedAsync(host);
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
        await page.GotoAsync("/");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Inbox");

        foreach (var (path, active) in Views)
        {
            await page.GotoAsync(path);
            await Expect(page.Locator("nav.views a.active"))
                .ToHaveTextAsync(active);
            await ExpectFitsAsync(page, path);
            Assert.True(
                await page.EvaluateAsync<bool>(ActiveInSight),
                $"{path}: the active view link is out of sight");
        }

        foreach (var path in new[]
        {
            $"/items/{itemId}",
            $"/projects/{projectId}",
            "/next?change=1",
        })
        {
            await page.GotoAsync(path);
            await ExpectFitsAsync(page, path);
        }

        await Expect(page.Locator(".notice")).ToBeVisibleAsync();
    }

    // The phone layout shows each change as a card, without the
    // header row; a screen reader must still hear each column's name.
    [PlaywrightFact]
    public async Task Changes_KeepTheirColumnNames_ForAssistiveTechnology()
    {
        await using var host = await BrowserHost.StartAsync();
        await SeedAsync(host);
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
        await page.GotoAsync("/changes");

        var table = page.GetByRole(AriaRole.Table);
        await Expect(table).ToHaveCountAsync(1);
        foreach (var name in new[] { "When", "What", "By" })
        {
            await Expect(
                    table.GetByRole(
                        AriaRole.Columnheader,
                        new() { Name = name, Exact = true }))
                .ToHaveCountAsync(1);
        }

        await Expect(table.GetByRole(AriaRole.Cell).First)
            .ToBeVisibleAsync();
        Assert.True(
            await page.EvaluateAsync<bool>(
                "() => document.querySelector('.changes thead')"
                + ".getBoundingClientRect().height <= 1"),
            "the header row is in sight");
    }

    private static async Task ExpectFitsAsync(
        IPage page,
        string path)
    {
        var problems = await page.EvaluateAsync<string[]>(Problems);
        Assert.True(
            problems.Length == 0,
            $"{path}: {string.Join("; ", problems)}");
    }

    /// <summary>Invented items of every kind the views show.</summary>
    internal static async Task<(Guid Item, Guid Project)> SeedAsync(
        BrowserHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        db.Source = "seed";
        db.Description = "Invented data for the phone layout test";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var home = new Project("Home repairs");
        var kitchen = new Project("Repaint the kitchen", home);
        var trip = new Project("Plan the lake trip");
        db.Projects.AddRange(home, kitchen, trip);

        var calls = new Context("calls");
        var errands = new Context("errands");
        db.Contexts.AddRange(calls, errands);

        db.Items.Add(new Item("Idea for the garden shed"));
        db.Items.Add(new Item("Look up bike tyre sizes"));
        db.Items.Add(new Item("Unbroken".PadRight(90, 'x')));

        var main = new Item("Call the dentist to move the check-up")
        {
            State = ItemState.NextAction,
            Project = kitchen,
            Priority = Priority.High,
            Notes = "Ask for a morning slot.",
        };
        main.AddContext(calls);
        main.AddContext(errands);
        main.SetDue(today.AddDays(-2));
        main.AddReminder(Reminder.FromDue(TimeSpan.FromHours(9)));
        db.Items.Add(main);

        var bins = new Item("Put the bins out")
        {
            State = ItemState.NextAction,
        };
        bins.SetDue(today);
        bins.Repeat(
            RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=FR"),
            RecurrenceMode.FixedSchedule,
            today);
        db.Items.Add(bins);

        db.Items.Add(
            new Item("Hear from the insurer")
            {
                State = ItemState.WaitingFor,
                Project = home,
            });
        db.Items.Add(
            new Item("Try pottery classes")
            {
                State = ItemState.Someday,
            });
        await db.SaveChangesAsync();
        return (main.Id, kitchen.Id);
    }
}
