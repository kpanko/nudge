namespace Nudge.Core;

/// <summary>How the next occurrence of a repeating item is chosen.</summary>
public enum RecurrenceMode
{
    /// <summary>
    /// Occurrences follow the rule regardless of completion date,
    /// e.g. "every two weeks on Friday".
    /// </summary>
    FixedSchedule,

    /// <summary>
    /// The next occurrence is counted from the day the item was
    /// completed, e.g. "every six months".
    /// </summary>
    AfterCompletion,
}
