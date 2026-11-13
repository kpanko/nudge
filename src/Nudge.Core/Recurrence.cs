namespace Nudge.Core;

/// <summary>
/// How an item repeats: the rule, how the next occurrence is chosen,
/// and the day the series starts. None of these change when a single
/// occurrence is completed or moved to another day.
/// </summary>
public sealed record Recurrence(
    RecurrenceRule Rule,
    RecurrenceMode Mode,
    DateOnly Anchor)
{
    /// <summary>
    /// The occurrence that replaces the one just completed, or null
    /// if the series has ended. Calling this once advances the item
    /// by exactly one occurrence.
    /// </summary>
    /// <param name="occurrence">The day the rule placed the completed
    /// occurrence on. This is not the due date if the occurrence was
    /// moved; using the rule's own day is what stops an occurrence
    /// that was moved earlier from coming round a second time.
    /// </param>
    /// <param name="completedOn">The owner's local date when the item
    /// was completed.</param>
    public DateOnly? NextAfterCompletion(
        DateOnly occurrence,
        DateOnly completedOn)
    {
        if (Mode == RecurrenceMode.AfterCompletion)
        {
            return Rule.NextCountedFrom(completedOn);
        }

        // Late completion skips the occurrences that were missed
        // rather than queueing them up; early completion still moves
        // past the occurrence being completed.
        var after = completedOn > occurrence ? completedOn : occurrence;
        return Rule.NextAfter(Anchor, after);
    }
}
