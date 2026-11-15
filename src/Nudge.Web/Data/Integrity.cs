using Microsoft.EntityFrameworkCore;

namespace Nudge.Web.Data;

/// <summary>
/// Rules about the data as a whole that no single row can break and
/// no constraint in the database can state. The entities keep them
/// when they are used as intended; this looks at what is actually
/// stored.
/// </summary>
internal static class Integrity
{
    /// <summary>What is wrong, one sentence for each row that breaks
    /// a rule; empty if nothing does.</summary>
    public static async Task<List<string>> ProblemsAsync(
        NudgeDbContext db,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        // Something that is not deleted never belongs to something
        // that is. Otherwise it would be read without its project,
        // or its parent, and look as if it had none.
        var items = await db.Items
            .IgnoreQueryFilters()
            .Where(
                item => item.DeletedAt == null
                    && item.Project!.DeletedAt != null)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        problems.AddRange(
            items.Select(
                id => $"Item {Text(id)} is not deleted, but its"
                    + " project is."));

        var projects = await db.Projects
            .IgnoreQueryFilters()
            .Where(
                project => project.DeletedAt == null
                    && project.Parent!.DeletedAt != null)
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);
        problems.AddRange(
            projects.Select(
                id => $"Project {Text(id)} is not deleted, but the"
                    + " project it is under is."));

        // No project is its own ancestor.
        var parents = await db.Projects
            .IgnoreQueryFilters()
            .Select(
                project => new
                {
                    project.Id,
                    ParentId = EF.Property<Guid?>(project, "ParentId"),
                })
            .ToDictionaryAsync(
                project => project.Id,
                project => project.ParentId,
                cancellationToken);
        problems.AddRange(
            parents.Keys
                .Where(id => IsUnderItself(id, parents))
                .Select(id => $"Project {Text(id)} is under itself."));

        problems.Sort(StringComparer.Ordinal);
        return problems;
    }

    /// <summary>Whether going up from a project, parent by parent,
    /// comes back to it.</summary>
    private static bool IsUnderItself(
        Guid project,
        Dictionary<Guid, Guid?> parents)
    {
        var seen = new HashSet<Guid>();
        for (var next = parents[project];
            next is { } id;
            next = parents.GetValueOrDefault(id))
        {
            if (id == project)
            {
                return true;
            }

            if (!seen.Add(id))
            {
                // A loop further up that this project is not on.
                return false;
            }
        }

        return false;
    }

    /// <summary>An id as the database writes it.</summary>
    private static string Text(Guid id)
    {
        return id.ToString().ToUpperInvariant();
    }
}
