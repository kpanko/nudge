using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Nudge.Web.Data;

/// <summary>
/// Takes a change back by returning every row it touched to what the
/// change log says it was: changed columns get their old values, a
/// removed row is put back, an added row goes. It writes SQL built
/// from the log rather than going through the entities, so that what
/// comes back is exactly what was stored, and it is recorded as a
/// change of its own, so an undo can be undone.
/// </summary>
/// <remarks>
/// It is all or nothing, in one transaction. It is refused, and
/// nothing is altered, when a value it would put back has been
/// changed again since, when the database's own constraints object,
/// or when the result would break one of the rules in
/// <see cref="Integrity"/> that was not broken already.
/// </remarks>
internal sealed class ChangeUndo(NudgeDbContext db)
{
    private const string Savepoint = "undo";

    private readonly ISqlGenerationHelper _sql =
        db.GetService<ISqlGenerationHelper>();

    /// <returns>The change that records the undo, or null if there
    /// is no change with that number.</returns>
    public async Task<Change?> RunAsync(
        long changeId,
        CancellationToken cancellationToken)
    {
        var source = db.RequiredSource();
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "An undo runs in a transaction of its own. Finish the"
                + " one in progress first.");
        }

        RequireNothingUnsaved();
        try
        {
            // Holds the write lock from here on, so nothing can
            // change between the checks and the writes.
            await using var transaction = await db.Database
                .BeginTransactionAsync(cancellationToken);
            var undo = await UndoAsync(changeId, source, cancellationToken);
            if (undo is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return undo;
        }
        finally
        {
            // The rows were written underneath anything this context
            // had read, so it must not go on using its copies. There
            // was nothing unsaved to lose: that was checked above.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The same, inside the transaction already open on the context,
    /// which the caller commits or rolls back. A refused undo is
    /// rolled back to a savepoint taken first, so it leaves the
    /// transaction as it found it.
    /// </summary>
    public async Task<Change?> RunWithinAsync(
        long changeId,
        CancellationToken cancellationToken)
    {
        var source = db.RequiredSource();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Open a transaction first, or use UndoAsync, which"
                + " opens its own.");
        RequireNothingUnsaved();
        try
        {
            await transaction.CreateSavepointAsync(
                Savepoint,
                cancellationToken);
            try
            {
                var undo = await UndoAsync(
                    changeId,
                    source,
                    cancellationToken);
                await transaction.ReleaseSavepointAsync(
                    Savepoint,
                    cancellationToken);
                return undo;
            }
            catch
            {
                await transaction.RollbackToSavepointAsync(
                    Savepoint,
                    CancellationToken.None);
                throw;
            }
        }
        finally
        {
            // As above.
            db.ChangeTracker.Clear();
        }
    }

    private void RequireNothingUnsaved()
    {
        if (db.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "Save or discard what was changed through this"
                + " context before undoing.");
        }
    }

    /// <summary>
    /// Checks and writes the undo, in the transaction that is open,
    /// and saves the change that records it.
    /// </summary>
    private async Task<Change?> UndoAsync(
        long changeId,
        string source,
        CancellationToken cancellationToken)
    {
        var at = db.Now;

        var change = await db.Changes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == changeId,
                cancellationToken);
        if (change is null)
        {
            return null;
        }

        if (await StandingUndoAsync(changeId, cancellationToken)
            is { } undoneBy)
        {
            throw new UndoRefusedException(
            [
                $"Change {changeId} has already been undone, by"
                + $" change {undoneBy}.",
            ]);
        }

        var steps = new List<Step>();
        var problems = new List<string>();
        foreach (var entry in change.Entries.OrderBy(e => e.Ordinal))
        {
            var step = await PlanAsync(
                entry,
                at,
                problems,
                cancellationToken);
            if (step is not null)
            {
                steps.Add(step);
            }
        }

        if (problems.Count > 0)
        {
            throw new UndoRefusedException(problems);
        }

        var wrongBefore = await Integrity.ProblemsAsync(
            db,
            cancellationToken);
        try
        {
            foreach (var step in steps)
            {
                await step.Apply(cancellationToken);
            }
        }
        catch (DbException e)
        {
            // A constraint: for example, the name of a context
            // that would come back has been taken since.
            throw new UndoRefusedException(
                [$"The database refused it: {e.Message}"]);
        }

        // Only what this undo would break counts against it. If
        // something was wrong already, refusing every undo would
        // not put that right.
        var wrongAfter = await Integrity.ProblemsAsync(
            db,
            cancellationToken);
        var broken = wrongAfter.Except(wrongBefore).ToList();
        if (broken.Count > 0)
        {
            throw new UndoRefusedException(
                broken
                    .Select(
                        problem =>
                            $"Afterwards this would be wrong: {problem}")
                    .ToList());
        }

        var undo = new Change(
            at,
            source,
            db.Description,
            changeId,
            steps.Select(step => step.Inverse));
        db.Changes.Add(undo);
        await db.SaveChangesAsync(cancellationToken);
        return undo;
    }

    /// <summary>
    /// The undo of this change that still stands, if there is one.
    /// An undo that has itself been undone no longer counts: the
    /// change is in force again and can be taken back once more.
    /// </summary>
    private async Task<long?> StandingUndoAsync(
        long changeId,
        CancellationToken cancellationToken)
    {
        var undos = await db.Changes
            .AsNoTracking()
            .Where(change => change.Undoes == changeId)
            .OrderBy(change => change.Id)
            .Select(change => change.Id)
            .ToListAsync(cancellationToken);
        foreach (var undo in undos)
        {
            if (await StandingUndoAsync(undo, cancellationToken) is null)
            {
                return undo;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks that one entry can be taken back, adding to
    /// <paramref name="problems"/> if not, and returns how to do it.
    /// Nothing is written here.
    /// </summary>
    private async Task<Step?> PlanAsync(
        ChangeEntry entry,
        DateTimeOffset at,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var row = $"{entry.TableName} {entry.RowKey}";
        var key = StoredJson.Parse(entry.RowKey);
        var before = entry.Before is null
            ? null
            : StoredJson.Parse(entry.Before);
        var after = entry.After is null
            ? null
            : StoredJson.Parse(entry.After);

        // Table and column names from the log go into SQL, so they
        // are only used once the model has been found to have them.
        // An entry from before a column was renamed or dropped, or
        // before the way a value is stored was changed, stops here.
        var table = FindTable(entry.TableName);
        if (table is null || !table.Has(key, before, after))
        {
            problems.Add(
                $"{row} was recorded when the database had tables or"
                + " columns that it no longer has.");
            return null;
        }

        if (!table.CanRead(key, before, after))
        {
            problems.Add(
                $"{row} was recorded with a value that can no longer"
                + " be read.");
            return null;
        }

        switch (entry.Kind)
        {
            case ChangeKind.Changed:
                Compare(
                    row,
                    await DifferencesAsync(
                        table,
                        key,
                        after!,
                        cancellationToken),
                    problems);
                return new Step(
                    token => UpdateAsync(table, key, before!, token),
                    ChangeEntry.Changed(
                        entry.TableName,
                        entry.RowKey,
                        entry.After!,
                        entry.Before!));

            // The row of an item, project or context is never
            // removed, not even to take back its creation: other
            // rows may have come to depend on it. It is marked
            // deleted, as if by hand.
            case ChangeKind.Added when table.DeletedAt is { } deletedAt:
                var live = new JsonObject
                {
                    [NudgeDbContext.DeletedAt] = null,
                };
                var deleted = new JsonObject
                {
                    [NudgeDbContext.DeletedAt] =
                        StoredJson.FromValue(deletedAt, at),
                };
                var differences = await DifferencesAsync(
                    table,
                    key,
                    live,
                    cancellationToken);
                if (differences is null)
                {
                    problems.Add($"{row} no longer exists.");
                }
                else if (differences.Count > 0)
                {
                    problems.Add($"{row} has been deleted since.");
                }

                return new Step(
                    token => UpdateAsync(table, key, deleted, token),
                    ChangeEntry.Changed(
                        entry.TableName,
                        entry.RowKey,
                        StoredJson.ToText(live),
                        StoredJson.ToText(deleted)));

            case ChangeKind.Added:
                Compare(
                    row,
                    await DifferencesAsync(
                        table,
                        key,
                        after!,
                        cancellationToken),
                    problems);
                return new Step(
                    token => DeleteAsync(table, key, token),
                    ChangeEntry.Removed(
                        entry.TableName,
                        entry.RowKey,
                        entry.After!));

            case ChangeKind.Removed:
                if (await DifferencesAsync(
                        table,
                        key,
                        new JsonObject(),
                        cancellationToken)
                    is not null)
                {
                    problems.Add($"{row} exists again.");
                }

                return new Step(
                    token => InsertAsync(table, before!, token),
                    ChangeEntry.Added(
                        entry.TableName,
                        entry.RowKey,
                        entry.Before!));

            default:
                throw new NotSupportedException(
                    $"Unknown kind of change: {entry.Kind}.");
        }
    }

    private static void Compare(
        string row,
        List<string>? differences,
        List<string> problems)
    {
        if (differences is null)
        {
            problems.Add($"{row} no longer exists.");
        }
        else if (differences.Count > 0)
        {
            problems.Add(
                $"{row} has been changed since:"
                + $" {string.Join(", ", differences)}.");
        }
    }

    private Table? FindTable(string name)
    {
        var type = db.Model
            .GetEntityTypes()
            .SingleOrDefault(t => t.GetTableName() == name);
        if (type is null
            || type.ClrType == typeof(Change)
            || type.ClrType == typeof(ChangeEntry)
            || type.ClrType == typeof(IdempotencyRecord)
            || type.ClrType == typeof(ApiToken))
        {
            return null;
        }

        return new Table(
            name,
            type.FindPrimaryKey()!.Properties,
            type.GetFlattenedProperties()
                .ToDictionary(property => property.GetColumnName()));
    }

    /// <summary>
    /// The columns of a row that do not hold what is expected:
    /// none if all do, null if there is no such row.
    /// </summary>
    private async Task<List<string>?> DifferencesAsync(
        Table table,
        JsonObject key,
        JsonObject expected,
        CancellationToken cancellationToken)
    {
        await using var command = NewCommand();
        var columns = new List<string>();
        var checks = new List<string>();
        foreach (var (column, value) in expected)
        {
            // IS, unlike =, also holds when both sides are null.
            var parameter = Bind(command, table.Columns[column], value);
            columns.Add(column);
            checks.Add($"{Quote(column)} IS {parameter}");
        }

        command.CommandText =
            $"SELECT {(checks.Count == 0 ? "1" : string.Join(", ", checks))}"
            + $" FROM {Quote(table.Name)}"
            + $" WHERE {Where(command, table, key)};";
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return columns
            .Where((_, index) => reader.GetInt64(index) == 0)
            .ToList();
    }

    private async Task UpdateAsync(
        Table table,
        JsonObject key,
        JsonObject values,
        CancellationToken cancellationToken)
    {
        await using var command = NewCommand();
        var assignments = new List<string>();
        foreach (var (column, value) in values)
        {
            var parameter = Bind(command, table.Columns[column], value);
            assignments.Add($"{Quote(column)} = {parameter}");
        }

        command.CommandText =
            $"UPDATE {Quote(table.Name)}"
            + $" SET {string.Join(", ", assignments)}"
            + $" WHERE {Where(command, table, key)};";
        await ExecuteAsync(command, cancellationToken);
    }

    private async Task InsertAsync(
        Table table,
        JsonObject values,
        CancellationToken cancellationToken)
    {
        await using var command = NewCommand();
        var columns = new List<string>();
        var parameters = new List<string>();
        foreach (var (column, value) in values)
        {
            columns.Add(Quote(column));
            parameters.Add(Bind(command, table.Columns[column], value));
        }

        command.CommandText =
            $"INSERT INTO {Quote(table.Name)}"
            + $" ({string.Join(", ", columns)})"
            + $" VALUES ({string.Join(", ", parameters)});";
        await ExecuteAsync(command, cancellationToken);
    }

    private async Task DeleteAsync(
        Table table,
        JsonObject key,
        CancellationToken cancellationToken)
    {
        await using var command = NewCommand();
        command.CommandText =
            $"DELETE FROM {Quote(table.Name)}"
            + $" WHERE {Where(command, table, key)};";
        await ExecuteAsync(command, cancellationToken);
    }

    private static async Task ExecuteAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
        {
            // The checks found exactly one row, in this transaction.
            throw new InvalidOperationException(
                $"Expected to write one row, not {rows}:"
                + $" {command.CommandText}");
        }
    }

    private DbCommand NewCommand()
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction =
            db.Database.CurrentTransaction!.GetDbTransaction();
        return command;
    }

    private string Where(
        DbCommand command,
        Table table,
        JsonObject key)
    {
        var conditions = new List<string>();
        foreach (var property in table.Key)
        {
            var column = property.GetColumnName();
            var parameter = Bind(command, property, key[column]);
            conditions.Add($"{Quote(column)} = {parameter}");
        }

        return string.Join(" AND ", conditions);
    }

    /// <summary>
    /// Adds a value to the command as a parameter and returns the
    /// parameter's name. The value goes through the same mapping
    /// that the context writes the column with, so it is compared
    /// and stored in exactly the form the column holds.
    /// </summary>
    private static string Bind(
        DbCommand command,
        IProperty property,
        JsonNode? value)
    {
        var name = "@p" + command.Parameters.Count;
        command.Parameters.Add(
            property.GetRelationalTypeMapping().CreateParameter(
                command,
                name,
                StoredJson.ToValue(property, value),
                nullable: true));
        return name;
    }

    private string Quote(string identifier)
    {
        return _sql.DelimitIdentifier(identifier);
    }

    /// <summary>How to take one entry back, and the entry that
    /// records having done so.</summary>
    private sealed record Step(
        Func<CancellationToken, Task> Apply,
        ChangeEntry Inverse);

    /// <summary>A table as the model describes it now.</summary>
    private sealed record Table(
        string Name,
        IReadOnlyList<IProperty> Key,
        IReadOnlyDictionary<string, IProperty> Columns)
    {
        /// <summary>The column that marks a row deleted, in the
        /// tables whose rows are never removed.</summary>
        public IProperty? DeletedAt =>
            Columns.GetValueOrDefault(NudgeDbContext.DeletedAt);

        /// <summary>Whether the key names exactly this table's key
        /// columns, and the values only columns it has.</summary>
        public bool Has(
            JsonObject key,
            params JsonObject?[] values)
        {
            return key.Count == Key.Count
                && Key.All(p => key[p.GetColumnName()] is not null)
                && values.All(
                    row => row is null
                        || row.All(pair => Columns.ContainsKey(pair.Key)));
        }

        /// <summary>Whether every value can still be turned back
        /// into what its column holds. Call <see cref="Has"/> first.
        /// </summary>
        public bool CanRead(params JsonObject?[] rows)
        {
            try
            {
                foreach (var row in rows.OfType<JsonObject>())
                {
                    foreach (var (column, value) in row)
                    {
                        StoredJson.ToValue(Columns[column], value);
                    }
                }

                return true;
            }
            catch (Exception e) when (e
                is JsonException
                or FormatException
                or ArgumentException
                or InvalidOperationException)
            {
                // Not the JSON type the column takes, or text that
                // no longer names a list, a mode or a rule.
                return false;
            }
        }
    }
}
