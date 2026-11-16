using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Components;

/// <summary>A context and its next actions; a null context is the
/// group of next actions without one.</summary>
public sealed record ContextGroup(
    Context? Context,
    IReadOnlyList<Item> Items);

/// <summary>A project with its place in the tree.</summary>
/// <param name="Depth">0 for a project at the top.</param>
/// <param name="OpenItems">Its open items, not those of the projects
/// under it.</param>
/// <param name="HasNextAction">Whether one of those is a next
/// action.</param>
public sealed record ProjectLine(
    Project Project,
    int Depth,
    int OpenItems,
    bool HasNextAction);

/// <summary>A reminder that has fired, on an item still open.
/// </summary>
public sealed record MissedReminder(
    Item Item,
    Reminder Reminder,
    DateTimeOffset FiredAt);

/// <summary>What the weekly review goes through.</summary>
public sealed record Review(
    int InboxCount,
    IReadOnlyList<Project> ProjectsWithoutNextAction,
    IReadOnlyList<Item> Waiting,
    IReadOnlyList<Item> Someday,
    IReadOnlyList<Item> Overdue,
    IReadOnlyList<Item> NextSevenDays);

/// <summary>
/// What the web UI's pages show, read without tracking so that
/// nothing read here is part of a write. Open items are those not
/// completed for good (deleted ones are never read). "Today" is the
/// owner's, in their time zone.
/// </summary>
public sealed class UiQueries(
    NudgeDbContext db,
    TimeProvider clock,
    OwnerTimeZone zone)
{
    /// <summary>How many recent changes the change page shows.
    /// </summary>
    public const int RecentChanges = 50;

    public TimeZoneInfo Zone => zone.Zone;

    /// <summary>The owner's date now.</summary>
    public DateOnly Today()
    {
        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone.Zone).DateTime);
    }

    public DateTimeOffset Now()
    {
        return clock.GetUtcNow();
    }

    /// <summary>Whether the item is put off until after today.
    /// </summary>
    public bool IsDeferred(Item item)
    {
        return item.DeferUntil is { } until && until > Today();
    }

    public async Task<IReadOnlyList<Item>> ListAsync(
        ItemState state,
        CancellationToken cancellationToken = default)
    {
        return Ordered(
            (await OpenItemsAsync(cancellationToken))
                .Where(item => item.State == state));
    }

    /// <summary>
    /// The next actions that are not deferred past today, under each
    /// of their contexts (so one with two contexts is under both),
    /// contexts by name, and those without a context last.
    /// </summary>
    public async Task<IReadOnlyList<ContextGroup>> NextActionsAsync(
        CancellationToken cancellationToken = default)
    {
        var items = (await OpenItemsAsync(cancellationToken))
            .Where(item => item.State == ItemState.NextAction)
            .Where(item => !IsDeferred(item))
            .ToList();
        var groups = items
            .SelectMany(item => item.Contexts.Select(c => (c, item)))
            .GroupBy(pair => pair.c.Id)
            .Select(
                group => new ContextGroup(
                    group.First().c,
                    Ordered(group.Select(pair => pair.item))))
            .OrderBy(g => g.Context!.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var none = items.Where(item => item.Contexts.Count == 0).ToList();
        if (none.Count > 0)
        {
            groups.Add(new ContextGroup(null, Ordered(none)));
        }

        return groups;
    }

    /// <summary>Every project, as a tree in name order.</summary>
    public async Task<IReadOnlyList<ProjectLine>> ProjectTreeAsync(
        CancellationToken cancellationToken = default)
    {
        var projects = await ProjectsAsync(cancellationToken);
        var open = await OpenItemsAsync(cancellationToken);
        var lines = new List<ProjectLine>();

        void Add(
            Project? parent,
            int depth)
        {
            foreach (var project in projects
                .Where(p => p.Parent?.Id == parent?.Id)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var items = open
                    .Where(item => item.Project?.Id == project.Id)
                    .ToList();
                lines.Add(
                    new ProjectLine(
                        project,
                        depth,
                        items.Count,
                        items.Any(i => i.State == ItemState.NextAction)));
                Add(project, depth + 1);
            }
        }

        Add(null, 0);
        return lines;
    }

    public async Task<IReadOnlyList<Project>> ProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.Projects
            .AsNoTracking()
            .Include(p => p.Parent)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Project?> ProjectAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return (await ProjectsAsync(cancellationToken))
            .SingleOrDefault(p => p.Id == id);
    }

    /// <summary>The open items directly in a project.</summary>
    public async Task<IReadOnlyList<Item>> ProjectItemsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return Ordered(
            (await OpenItemsAsync(cancellationToken))
                .Where(item => item.Project?.Id == projectId));
    }

    public async Task<IReadOnlyList<Context>> ContextsAsync(
        CancellationToken cancellationToken = default)
    {
        return (await db.Contexts
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>An item, open or finished; null if there is none or
    /// it was deleted.</summary>
    public async Task<Item?> ItemAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await db.Items
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    /// <summary>
    /// Reminders that have fired on items that are still open: each
    /// reminder's fire time for the item as it stands (so for the
    /// current occurrence of a repeating item) is in the past.
    /// Provisional: until the phone records acknowledgements (M5),
    /// a reminder stays here until the item is completed,
    /// rescheduled, or the reminder removed.
    /// </summary>
    public async Task<IReadOnlyList<MissedReminder>> MissedAsync(
        CancellationToken cancellationToken = default)
    {
        var now = Now();
        return (await OpenItemsAsync(cancellationToken))
            .SelectMany(
                item => item.Reminders.Select(
                    reminder => (
                        item,
                        reminder,
                        at: reminder.FireTime(item, zone.Zone))))
            .Where(r => r.at is { } at && at <= now)
            .Select(r => new MissedReminder(r.item, r.reminder, r.at!.Value))
            .OrderBy(r => r.FiredAt)
            .ToList();
    }

    /// <summary>
    /// The weekly review: the inbox to empty, projects that have no
    /// next action of their own, waiting and someday to look over,
    /// open items overdue (due before today) and those due in the
    /// next seven days (today and the six after it).
    /// </summary>
    public async Task<Review> ReviewAsync(
        CancellationToken cancellationToken = default)
    {
        var open = await OpenItemsAsync(cancellationToken);
        var today = Today();
        var projects = await ProjectTreeAsync(cancellationToken);
        return new Review(
            open.Count(item => item.State == ItemState.Inbox),
            projects
                .Where(line => !line.HasNextAction)
                .Select(line => line.Project)
                .ToList(),
            Ordered(open.Where(item => item.State == ItemState.WaitingFor)),
            Ordered(open.Where(item => item.State == ItemState.Someday)),
            Ordered(open.Where(item => item.DueDate < today)),
            Ordered(
                open.Where(
                    item => item.DueDate >= today
                        && item.DueDate < today.AddDays(7))));
    }

    public async Task<Change?> ChangeAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return await db.Changes
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Change>> ChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.Changes
            .AsNoTracking()
            .OrderByDescending(c => c.Id)
            .Take(RecentChanges)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Item>> OpenItemsAsync(
        CancellationToken cancellationToken)
    {
        return (await db.Items
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(item => !item.IsCompleted)
            .ToList();
    }

    /// <summary>Items with a due date first, earliest first, then
    /// by title.</summary>
    private static List<Item> Ordered(IEnumerable<Item> items)
    {
        return items
            .OrderBy(item => item.DueDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id)
            .ToList();
    }
}
