namespace Nudge.Web.Data;

/// <summary>
/// One entry in the change log: everything that one save wrote, with
/// who made it and when. A change is written in the same transaction
/// as the rows it describes and is never altered afterwards; taking
/// it back adds another change.
/// </summary>
public sealed class Change
{
    private readonly List<ChangeEntry> _entries = [];

    internal Change(
        DateTimeOffset at,
        string source,
        string description,
        long? undoes,
        IEnumerable<ChangeEntry> entries)
    {
        At = at;
        Source = source;
        Description = description;
        Undoes = undoes;
        foreach (var entry in entries)
        {
            entry.Ordinal = _entries.Count;
            _entries.Add(entry);
        }
    }

    // For the persistence layer.
    private Change()
    {
        Source = "";
        Description = "";
    }

    /// <summary>
    /// The number of the change. The database hands these out, so
    /// they run in the order the changes were saved, which neither
    /// a clock nor a random id can be trusted to do.
    /// </summary>
    public long Id { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>Who made the change, as given in
    /// <see cref="NudgeDbContext.Source"/>.</summary>
    public string Source { get; private set; }

    /// <summary>What it was for, in a few words; may be empty.
    /// </summary>
    public string Description { get; private set; }

    /// <summary>The change this one took back, if it is an undo.
    /// </summary>
    public long? Undoes { get; private set; }

    /// <summary>The rows it touched, one entry for each. Sort by
    /// <see cref="ChangeEntry.Ordinal"/> for the order they were
    /// recorded in.</summary>
    public IReadOnlyList<ChangeEntry> Entries => _entries;
}
