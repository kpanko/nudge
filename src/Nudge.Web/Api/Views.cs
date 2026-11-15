using System.ComponentModel;
using System.Text.Json.Nodes;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Api;

/// <summary>What every write returns.</summary>
/// <param name="DryRun">True if nothing was saved: the request was
/// carried out and rolled back.</param>
/// <param name="Change">What was saved, or would have been, as the
/// change log holds it; null if the write changed nothing.</param>
/// <param name="Data">The resource after the write; null after a
/// delete.</param>
[Description(
    "What every write returns. On a dry run (dryRun true) nothing was"
    + " saved, change.id is null, and ids of anything created are not"
    + " real.")]
public sealed record WriteResult<T>(
    bool DryRun,
    ChangeView? Change,
    T? Data);

/// <summary>One change from the change log.</summary>
/// <param name="Id">The number of the change, which undo takes; null
/// on a dry run, where nothing was saved.</param>
/// <param name="Undoes">The change this one took back, if it is an
/// undo.</param>
public sealed record ChangeView(
    long? Id,
    DateTimeOffset At,
    string Source,
    string Description,
    long? Undoes,
    IReadOnlyList<EntryView> Entries)
{
    public static ChangeView Of(
        Change change,
        bool dryRun = false)
    {
        return new ChangeView(
            dryRun ? null : change.Id,
            change.At,
            change.Source,
            change.Description,
            change.Undoes,
            change.Entries
                .OrderBy(entry => entry.Ordinal)
                .Select(EntryView.Of)
                .ToList());
    }
}

/// <summary>What a change did to one row, with values as the
/// database holds them, keyed by column name.</summary>
/// <param name="Table">The table the row is in.</param>
/// <param name="Key">The row's primary key.</param>
/// <param name="Before">The values before: the columns that changed,
/// or the whole row if it was removed; null if it was added.</param>
/// <param name="After">The values after: the columns that changed,
/// or the whole row if it was added; null if it was removed.</param>
public sealed record EntryView(
    string Table,
    JsonObject Key,
    ChangeKind Kind,
    JsonObject? Before,
    JsonObject? After)
{
    public static EntryView Of(ChangeEntry entry)
    {
        return new EntryView(
            entry.TableName,
            StoredJson.Parse(entry.RowKey),
            entry.Kind,
            entry.Before is null ? null : StoredJson.Parse(entry.Before),
            entry.After is null ? null : StoredJson.Parse(entry.After));
    }
}

/// <param name="List">The GTD list the item is on.</param>
/// <param name="DeferUntil">Hidden from action lists until this day.
/// </param>
/// <param name="DoNotAutoReschedule">Tells the planning agent to leave
/// the due date alone.</param>
/// <param name="Due">When the current occurrence is due; null if the
/// item has no due date.</param>
/// <param name="Repeat">How the item repeats; null if it does not.
/// </param>
/// <param name="CompletedAt">When the item was finished for good.
/// Completing one occurrence of a repeating item does not set it.
/// </param>
public sealed record ItemView(
    Guid Id,
    string Title,
    string Notes,
    ItemState List,
    Priority Priority,
    Guid? ProjectId,
    IReadOnlyList<Guid> ContextIds,
    DateOnly? DeferUntil,
    bool DoNotAutoReschedule,
    DueView? Due,
    RepeatView? Repeat,
    IReadOnlyList<ReminderView> Reminders,
    DateTimeOffset? CompletedAt)
{
    public static ItemView Of(Item item)
    {
        return new ItemView(
            item.Id,
            item.Title,
            item.Notes,
            item.State,
            item.Priority,
            item.Project?.Id,
            item.Contexts
                .Select(context => context.Id)
                .OrderBy(id => id.ToString(), StringComparer.Ordinal)
                .ToList(),
            item.DeferUntil,
            item.DoNotAutoReschedule,
            item.DueDate is { } date ? new DueView(date, item.DueTime) : null,
            item.Recurrence is { } recurrence
                && item.OccurrenceDate is { } occurrence
                ? new RepeatView(
                    recurrence.Rule.ToString(),
                    recurrence.Mode,
                    recurrence.Anchor,
                    occurrence)
                : null,
            item.Reminders
                .OrderBy(
                    reminder => reminder.Id.ToString(),
                    StringComparer.Ordinal)
                .Select(ReminderView.Of)
                .ToList(),
            item.CompletedAt);
    }
}

/// <param name="Time">The time of day; null means any time that day.
/// </param>
public sealed record DueView(
    DateOnly Date,
    TimeOnly? Time);

/// <param name="Rule">The RFC 5545 RRULE value, in canonical form,
/// such as FREQ=WEEKLY;BYDAY=FR.</param>
/// <param name="Start">The day the series starts.</param>
/// <param name="Occurrence">The day the rule placed the current
/// occurrence on. It differs from the due date when that occurrence
/// was rescheduled, and it is what completing it requires.</param>
public sealed record RepeatView(
    string Rule,
    RecurrenceMode Mode,
    DateOnly Start,
    DateOnly Occurrence);

/// <param name="At">A fixed instant; null for a relative reminder.
/// </param>
/// <param name="OffsetMinutes">Minutes from the due date and time,
/// negative before; a due date without a time counts as 00:00. Null
/// for a fixed instant.</param>
public sealed record ReminderView(
    Guid Id,
    DateTimeOffset? At,
    int? OffsetMinutes)
{
    public static ReminderView Of(Reminder reminder)
    {
        return new ReminderView(
            reminder.Id,
            reminder.At,
            reminder.Offset is { } offset ? (int)offset.TotalMinutes : null);
    }
}

/// <param name="ParentId">The project this one is under; null at the
/// top level.</param>
public sealed record ProjectView(
    Guid Id,
    string Name,
    Guid? ParentId)
{
    public static ProjectView Of(Project project)
    {
        return new ProjectView(project.Id, project.Name, project.Parent?.Id);
    }
}

public sealed record ContextView(
    Guid Id,
    string Name)
{
    public static ContextView Of(Context context)
    {
        return new ContextView(context.Id, context.Name);
    }
}
