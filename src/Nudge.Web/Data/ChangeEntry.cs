namespace Nudge.Web.Data;

/// <summary>
/// What a change did to one row. Values are written as JSON objects
/// keyed by column name, each value in the form the column holds it
/// in, so an entry can be read next to the table it describes:
/// <c>{"DueDate":"2026-11-13","DueTime":null}</c>.
/// </summary>
public sealed class ChangeEntry
{
    private ChangeEntry(
        string tableName,
        string rowKey,
        ChangeKind kind,
        string? before,
        string? after)
    {
        TableName = tableName;
        RowKey = rowKey;
        Kind = kind;
        Before = before;
        After = after;
    }

    /// <summary>Position within its change, from zero.</summary>
    public int Ordinal { get; internal set; }

    public string TableName { get; private set; }

    /// <summary>The row's primary key, for example
    /// <c>{"Id":"019A..."}</c>.</summary>
    public string RowKey { get; private set; }

    public ChangeKind Kind { get; private set; }

    /// <summary>
    /// The values before the change: the columns that changed for
    /// <see cref="ChangeKind.Changed"/>, the whole row for
    /// <see cref="ChangeKind.Removed"/>, and null for
    /// <see cref="ChangeKind.Added"/>.
    /// </summary>
    public string? Before { get; private set; }

    /// <summary>
    /// The values after the change: the columns that changed for
    /// <see cref="ChangeKind.Changed"/>, the whole row for
    /// <see cref="ChangeKind.Added"/>, and null for
    /// <see cref="ChangeKind.Removed"/>.
    /// </summary>
    public string? After { get; private set; }

    internal static ChangeEntry Added(
        string tableName,
        string rowKey,
        string after)
    {
        return new ChangeEntry(
            tableName,
            rowKey,
            ChangeKind.Added,
            null,
            after);
    }

    internal static ChangeEntry Changed(
        string tableName,
        string rowKey,
        string before,
        string after)
    {
        return new ChangeEntry(
            tableName,
            rowKey,
            ChangeKind.Changed,
            before,
            after);
    }

    internal static ChangeEntry Removed(
        string tableName,
        string rowKey,
        string before)
    {
        return new ChangeEntry(
            tableName,
            rowKey,
            ChangeKind.Removed,
            before,
            null);
    }
}
