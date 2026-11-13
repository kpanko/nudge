namespace Nudge.Core;

/// <summary>
/// When to remind the owner about an item. A reminder is either a
/// fixed instant or an offset from the item's due date; the second
/// kind follows the item when it is rescheduled or repeats, without
/// the reminder itself changing.
/// </summary>
public sealed class Reminder
{
    /// <summary>Largest offset either side of the due date.</summary>
    public static readonly TimeSpan MaxOffset = TimeSpan.FromDays(366);

    private Reminder(
        DateTimeOffset? at,
        TimeSpan? offset)
    {
        Id = Guid.CreateVersion7();
        At = at;
        Offset = offset;
    }

    public Guid Id { get; }

    /// <summary>The fixed instant, or null for a relative reminder.
    /// </summary>
    public DateTimeOffset? At { get; }

    /// <summary>
    /// For a relative reminder, the wall-clock time from the due date
    /// to the reminder: negative is before, positive after. An item
    /// with a due date but no due time counts as due at 00:00, so
    /// +9 hours means 09:00 on the due day and -6 hours means 18:00
    /// the day before.
    /// </summary>
    public TimeSpan? Offset { get; }

    /// <summary>A reminder at one fixed instant.</summary>
    public static Reminder AtInstant(DateTimeOffset at)
    {
        return new Reminder(at, null);
    }

    /// <summary>A reminder placed relative to the due date; see
    /// <see cref="Offset"/>.</summary>
    public static Reminder FromDue(TimeSpan offset)
    {
        if (offset.Duration() > MaxOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "A reminder must be within 366 days of the due date.");
        }

        return new Reminder(null, offset);
    }

    /// <summary>
    /// The instant this reminder fires for <paramref name="item"/>,
    /// or null if it does not: the item is completed, or the reminder
    /// is relative and the item has no due date to count from.
    /// </summary>
    public DateTimeOffset? FireTime(
        Item item,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(zone);

        if (item.IsCompleted)
        {
            return null;
        }

        if (At is not null)
        {
            return At;
        }

        if (item.DueDate is not { } dueDate || Offset is not { } offset)
        {
            return null;
        }

        // Add on the wall clock, then resolve, so that "09:00 on the
        // due day" stays 09:00 on a day the clocks change.
        var local = dueDate.ToDateTime(item.DueTime ?? TimeOnly.MinValue)
            + offset;
        return LocalTimeResolver.ToInstant(
            DateOnly.FromDateTime(local),
            TimeOnly.FromDateTime(local),
            zone);
    }
}
