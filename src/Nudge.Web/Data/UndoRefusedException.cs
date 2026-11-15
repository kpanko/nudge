namespace Nudge.Web.Data;

/// <summary>
/// A change could not be taken back, and nothing was altered. The
/// usual reason is that something it touched has been changed again
/// since.
/// </summary>
public sealed class UndoRefusedException(IReadOnlyList<string> reasons)
    : Exception(
        "The change cannot be undone. " + string.Join(" ", reasons))
{
    /// <summary>Each thing that stands in the way, as a sentence.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; } = reasons;
}
