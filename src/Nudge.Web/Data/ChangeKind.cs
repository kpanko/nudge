namespace Nudge.Web.Data;

/// <summary>What one change did to one row.</summary>
public enum ChangeKind
{
    /// <summary>The row did not exist before.</summary>
    Added,

    /// <summary>
    /// Some of its columns were given new values. Deleting an item,
    /// project or context is recorded like this: the row stays and
    /// its <c>DeletedAt</c> column is filled in.
    /// </summary>
    Changed,

    /// <summary>The row is gone. Only reminders and the links
    /// between items and contexts are ever removed.</summary>
    Removed,
}
