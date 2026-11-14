using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Nudge.Web.Data;

/// <summary>
/// Works out, from what a context is about to save, the entries that
/// describe it in the change log: one for each row that will be
/// added, changed or removed. It looks at rows and columns, not at
/// the entities, so a table or column added to the model later is
/// logged without anything here having to change.
/// </summary>
internal static class ChangeRecorder
{
    /// <exception cref="InvalidOperationException">The save would
    /// remove the row of an item, project or context, or alter the
    /// change log itself.</exception>
    public static List<ChangeEntry> Capture(DbContext context)
    {
        context.ChangeTracker.DetectChanges();

        var entries = new List<ChangeEntry>();
        foreach (var tracked in context.ChangeTracker.Entries())
        {
            if (tracked.Entity is Change or ChangeEntry)
            {
                if (tracked.State is EntityState.Modified
                    or EntityState.Deleted)
                {
                    throw new InvalidOperationException(
                        "The change log is only ever added to.");
                }

                continue;
            }

            // Bookkeeping about requests, not the owner's data. If it
            // were logged, undoing a change would remove the record
            // of the request that made it, and a retry of that
            // request would be carried out a second time.
            if (tracked.Entity is IdempotencyRecord)
            {
                continue;
            }

            var entry = tracked.State switch
            {
                EntityState.Added => Added(tracked),
                EntityState.Modified => Changed(tracked),
                EntityState.Deleted => Removed(tracked),
                _ => null,
            };
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static ChangeEntry Added(EntityEntry tracked)
    {
        var values = tracked.CurrentValues;
        return ChangeEntry.Added(
            TableName(tracked),
            Key(tracked, values),
            StoredJson.ToText(Row(tracked, values)));
    }

    /// <summary>The columns whose values differ, or null if none do.
    /// </summary>
    private static ChangeEntry? Changed(EntityEntry tracked)
    {
        var before = new JsonObject();
        var after = new JsonObject();
        foreach (var property in tracked.Metadata.GetFlattenedProperties())
        {
            var old = StoredJson.FromValue(
                property,
                Read(tracked.OriginalValues, property));
            var now = StoredJson.FromValue(
                property,
                Read(tracked.CurrentValues, property));
            if (JsonNode.DeepEquals(old, now))
            {
                continue;
            }

            var column = property.GetColumnName();
            before[column] = old;
            after[column] = now;
        }

        if (before.Count == 0)
        {
            return null;
        }

        return ChangeEntry.Changed(
            TableName(tracked),
            Key(tracked, tracked.CurrentValues),
            StoredJson.ToText(before),
            StoredJson.ToText(after));
    }

    private static ChangeEntry Removed(EntityEntry tracked)
    {
        var table = TableName(tracked);
        if (tracked.Metadata.FindProperty(NudgeDbContext.DeletedAt)
            is not null)
        {
            throw new InvalidOperationException(
                $"Rows of {table} are never removed. Call Delete on"
                + " the object instead: that marks it as deleted,"
                + " and can be undone.");
        }

        // What the database holds, not what the object was changed
        // to before it was removed.
        var values = tracked.OriginalValues;
        return ChangeEntry.Removed(
            table,
            Key(tracked, values),
            StoredJson.ToText(Row(tracked, values)));
    }

    private static string TableName(EntityEntry tracked)
    {
        return tracked.Metadata.GetTableName()
            ?? throw new NotSupportedException(
                $"{tracked.Metadata.DisplayName()} has no table, so"
                + " the change log cannot record it.");
    }

    private static string Key(
        EntityEntry tracked,
        PropertyValues values)
    {
        var key = new JsonObject();
        foreach (var property in
            tracked.Metadata.FindPrimaryKey()!.Properties)
        {
            key[property.GetColumnName()] = StoredJson.FromValue(
                property,
                values[property]);
        }

        return StoredJson.ToText(key);
    }

    /// <summary>Every column of the row.</summary>
    private static JsonObject Row(
        EntityEntry tracked,
        PropertyValues values)
    {
        var row = new JsonObject();
        foreach (var property in tracked.Metadata.GetFlattenedProperties())
        {
            row[property.GetColumnName()] = StoredJson.FromValue(
                property,
                Read(values, property));
        }

        return row;
    }

    private static object? Read(
        PropertyValues values,
        IProperty property)
    {
        if (property.DeclaringType is IComplexType complexType)
        {
            var whole = complexType.ComplexProperty;
            if (whole.IsCollection || whole.DeclaringType is not IEntityType)
            {
                throw new NotSupportedException(
                    $"The change log cannot record {whole.Name}: it"
                    + " handles a single value held directly by an"
                    + " entity, like an item's Recurrence.");
            }

            // Where the whole value is null, as the Recurrence of an
            // item that does not repeat, EF Core still reports a
            // value for each of its parts: the first mode, the year
            // 1. The columns hold null, so that is what is logged.
            if (values[whole.Name] is null)
            {
                return null;
            }
        }

        return values[property];
    }
}
