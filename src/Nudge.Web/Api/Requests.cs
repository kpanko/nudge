using Nudge.Core;

namespace Nudge.Web.Api;

/// <summary>
/// A new item, whole: everything in it is saved as one change, so a
/// repeating item with its reminders either exists completely or
/// not at all.
/// </summary>
/// <remarks>
/// With <c>repeat</c> but no <c>repeat.start</c>, the series starts
/// on <c>due.date</c>, and the first occurrence is the first day on
/// or after it that the rule allows. With <c>repeat.start</c> too,
/// <c>due</c> moves that first occurrence to another day, as
/// rescheduling would.
/// </remarks>
public sealed class ItemCreate
{
    public required string Title { get; init; }

    public string? Notes { get; init; }

    /// <summary>The GTD list; inbox if not given.</summary>
    public ItemState? List { get; init; }

    public Priority? Priority { get; init; }

    public Guid? ProjectId { get; init; }

    public DateOnly? DeferUntil { get; init; }

    public DueInput? Due { get; init; }

    public RepeatInput? Repeat { get; init; }

    public IReadOnlyList<Guid>? ContextIds { get; init; }

    public IReadOnlyList<ReminderInput>? Reminders { get; init; }

    public bool? DoNotAutoReschedule { get; init; }
}

/// <summary>
/// Changes to an item. A property left out is left as it is; one
/// given is set, and null clears <c>projectId</c> or
/// <c>deferUntil</c>. The due date, the repeat rule, contexts,
/// reminders and completion each have endpoints of their own, and
/// are refused here.
/// </summary>
public sealed class ItemPatch
{
    public string? Title { get; init; }

    public string? Notes { get; init; }

    public ItemState? List { get; init; }

    public Priority? Priority { get; init; }

    public Guid? ProjectId { get; init; }

    public DateOnly? DeferUntil { get; init; }

    public bool? DoNotAutoReschedule { get; init; }
}

/// <summary>When the current occurrence is due.</summary>
public sealed class DueInput
{
    public required DateOnly Date { get; init; }

    /// <summary>Time of day; null or left out means any time that day.
    /// </summary>
    public TimeOnly? Time { get; init; }
}

/// <summary>How an item repeats.</summary>
public sealed class RepeatInput
{
    /// <summary>An RFC 5545 RRULE value, such as
    /// FREQ=WEEKLY;INTERVAL=2;BYDAY=FR. FREQ (DAILY, WEEKLY, MONTHLY,
    /// YEARLY), INTERVAL, BYDAY, BYMONTHDAY, BYMONTH, UNTIL and WKST
    /// are supported.</summary>
    public required string Rule { get; init; }

    public required RecurrenceMode Mode { get; init; }

    /// <summary>The day the series starts. Left out, it is the due
    /// date.</summary>
    public DateOnly? Start { get; init; }
}

/// <summary>
/// Completing an item. For a repeating item <c>occurrence</c> is
/// required: the day the rule placed the occurrence being completed
/// on (<c>repeat.occurrence</c> of the item). If it is not the
/// current one, the request is refused, so that a request sent twice
/// cannot complete two occurrences.
/// </summary>
public sealed class CompleteRequest
{
    public DateOnly? Occurrence { get; init; }
}

/// <summary>A reminder: exactly one of <c>at</c> and
/// <c>offsetMinutes</c>.</summary>
public sealed class ReminderInput
{
    public DateTimeOffset? At { get; init; }

    /// <summary>Minutes from the due date and time, negative before.
    /// A due date without a time counts as 00:00, so 540 is 09:00 on
    /// the day. At most 366 days either way.</summary>
    public int? OffsetMinutes { get; init; }
}

public sealed class ProjectCreate
{
    public required string Name { get; init; }

    public Guid? ParentId { get; init; }
}

/// <summary>Changes to a project. A property left out is left as it
/// is; <c>parentId</c> null moves it to the top level.</summary>
public sealed class ProjectPatch
{
    public string? Name { get; init; }

    public Guid? ParentId { get; init; }
}

public sealed class ContextCreate
{
    public required string Name { get; init; }
}

public sealed class ContextPatch
{
    public string? Name { get; init; }
}

/// <summary>For endpoints that take no body.</summary>
public sealed class NoBody
{
    public static NoBody Value { get; } = new();
}
