namespace Nudge.Core;

/// <summary>
/// One thing to do. It may have a due date, and it may repeat; the
/// two are kept apart so that changing the due date of one occurrence
/// can never change or remove the repeat rule.
/// </summary>
public sealed class Item
{
    private readonly HashSet<Context> _contexts = [];
    private readonly List<Reminder> _reminders = [];
    private string _title;
    private ItemState _state = ItemState.Inbox;
    private Priority _priority;

    public Item(string title)
    {
        Id = Guid.CreateVersion7();
        _title = Name.Clean(title, nameof(title));
    }

    public Guid Id { get; }

    public string Title
    {
        get => _title;
        set => _title = Name.Clean(value, nameof(value));
    }

    public string Notes { get; set; } = "";

    /// <exception cref="ArgumentOutOfRangeException">The value is
    /// not one of the lists. A number can be cast to any enum, and
    /// an item in a list that does not exist would be shown nowhere.
    /// </exception>
    public ItemState State
    {
        get => _state;
        set => _state = Defined(value, nameof(value));
    }

    /// <exception cref="ArgumentOutOfRangeException">The value is
    /// not one of the priorities.</exception>
    public Priority Priority
    {
        get => _priority;
        set => _priority = Defined(value, nameof(value));
    }

    /// <summary>The project this item belongs to, if any.</summary>
    public Project? Project { get; set; }

    /// <summary>Hidden from action lists until this day.</summary>
    public DateOnly? DeferUntil { get; set; }

    /// <summary>Tells the planning agent to leave the due date alone.
    /// </summary>
    public bool DoNotAutoReschedule { get; set; }

    public IReadOnlyCollection<Context> Contexts => _contexts;

    public IReadOnlyList<Reminder> Reminders => _reminders;

    /// <summary>The day the current occurrence is due. Moving it
    /// reschedules this occurrence only.</summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>Time of day it is due; null means any time that day.
    /// A repeating item keeps it from one occurrence to the next.
    /// </summary>
    public TimeOnly? DueTime { get; private set; }

    /// <summary>How the item repeats, or null if it does not.</summary>
    public Recurrence? Recurrence { get; private set; }

    /// <summary>
    /// For a repeating item, the day the rule placed the current
    /// occurrence on. It equals <see cref="DueDate"/> until the
    /// occurrence is rescheduled, and it is what the next occurrence
    /// is worked out from. Null if the item does not repeat.
    /// </summary>
    public DateOnly? OccurrenceDate { get; private set; }

    /// <summary>When the item was finished for good. Completing one
    /// occurrence of a repeating item does not set this.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    public void AddContext(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _contexts.Add(context);
    }

    public void RemoveContext(Context context)
    {
        _contexts.Remove(context);
    }

    public void AddReminder(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        if (!_reminders.Contains(reminder))
        {
            _reminders.Add(reminder);
        }
    }

    public void RemoveReminder(Reminder reminder)
    {
        _reminders.Remove(reminder);
    }

    /// <summary>
    /// Sets or moves the due date. On a repeating item this
    /// reschedules the current occurrence only: the repeat rule, the
    /// rule's own day for this occurrence and the reminders are left
    /// exactly as they were.
    /// </summary>
    public void SetDue(
        DateOnly date,
        TimeOnly? time = null)
    {
        DueDate = date;
        DueTime = time;
    }

    /// <summary>Removes the due date and time.</summary>
    /// <exception cref="InvalidOperationException">The item repeats.
    /// A repeating item always has a due date; call
    /// <see cref="StopRepeating"/> first if that is what is wanted.
    /// </exception>
    public void ClearDue()
    {
        if (Recurrence is not null)
        {
            throw new InvalidOperationException(
                "A repeating item must have a due date.");
        }

        DueDate = null;
        DueTime = null;
    }

    /// <summary>
    /// Makes the item repeat, replacing any earlier rule. The due
    /// date becomes the first occurrence: for a fixed schedule, the
    /// first day on or after <paramref name="start"/> that the rule
    /// allows; for after-completion, <paramref name="start"/> itself.
    /// The due time is kept.
    /// </summary>
    /// <exception cref="ArgumentException">A fixed-schedule rule has
    /// no occurrence on or after <paramref name="start"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the modes.</exception>
    public void Repeat(
        RecurrenceRule rule,
        RecurrenceMode mode,
        DateOnly start)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Defined(mode, nameof(mode));

        var first = mode == RecurrenceMode.FixedSchedule
            ? rule.NextOnOrAfter(start, start)
            : start;
        if (first is not { } firstDay)
        {
            throw new ArgumentException(
                "The rule has no occurrence on or after the start day.",
                nameof(rule));
        }

        Recurrence = new Recurrence(rule, mode, start);
        OccurrenceDate = firstDay;
        DueDate = firstDay;
    }

    /// <summary>Removes the repeat rule. The due date stays.</summary>
    public void StopRepeating()
    {
        Recurrence = null;
        OccurrenceDate = null;
    }

    /// <summary>
    /// Completes the item. A repeating item moves on by exactly one
    /// occurrence and stays open; if the series has ended, or the
    /// item does not repeat, it is finished for good.
    /// </summary>
    /// <param name="at">When it was completed.</param>
    /// <param name="zone">The owner's time zone, which decides what
    /// day that was.</param>
    /// <exception cref="InvalidOperationException">The item is
    /// already finished.</exception>
    public void Complete(
        DateTimeOffset at,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (IsCompleted)
        {
            throw new InvalidOperationException(
                "The item is already completed.");
        }

        if (Recurrence is null || OccurrenceDate is not { } occurrence)
        {
            CompletedAt = at;
            return;
        }

        var completedOn = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(at, zone).DateTime);
        var next = Recurrence.NextAfterCompletion(occurrence, completedOn);
        if (next is not { } nextDay)
        {
            CompletedAt = at;
            return;
        }

        DeferUntil = NextDeferDate(occurrence, nextDay);
        OccurrenceDate = nextDay;
        DueDate = nextDay;
    }

    /// <summary>The value, if the enum has a member for it.</summary>
    private static TEnum Defined<TEnum>(
        TEnum value,
        string paramName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Not one of the {typeof(TEnum).Name} values.");
        }

        return value;
    }

    /// <summary>
    /// The defer date for the next occurrence: the same number of
    /// days ahead of it as the old one was ahead of the occurrence
    /// just completed. A defer date that was after that occurrence
    /// has done its job and is dropped.
    /// </summary>
    private DateOnly? NextDeferDate(
        DateOnly occurrence,
        DateOnly next)
    {
        if (DeferUntil is not { } defer || defer > occurrence)
        {
            return null;
        }

        return next.AddDays(defer.DayNumber - occurrence.DayNumber);
    }
}
