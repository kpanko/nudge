using Microsoft.EntityFrameworkCore;
using Nudge.Core;

namespace Nudge.Web.Data;

/// <summary>
/// The Nudge database: how the types in <c>Nudge.Core</c> are stored
/// in SQLite. Tables and relationships are set up by the
/// <c>...Configuration</c> classes next to this file; how single
/// values are stored is set up in <see cref="ConfigureConventions"/>.
/// </summary>
/// <remarks>
/// After changing anything that affects the schema, add a migration
/// and commit it with the change:
/// <code>
/// dotnet tool restore
/// dotnet ef migrations add NameOfTheChange --project src/Nudge.Web
/// </code>
/// Migrations are applied when the app starts. A test fails if the
/// model and the migrations disagree.
/// <para>
/// Ids are made by the entities, not by the database, so an entity
/// this context has not read is taken to be new when it is saved.
/// Before giving an item a project or context that already exists,
/// read it through the same context; otherwise saving tries to
/// insert it a second time and fails.
/// </para>
/// <para>
/// Every save is recorded in the change log, in the same
/// transaction: who made it (<see cref="Source"/>, which has to be
/// set first) and what each row held before and after. That is what
/// <see cref="UndoAsync"/> works from. Only saving through this
/// context is recorded, so never write to these tables with
/// <c>ExecuteUpdate</c>, <c>ExecuteDelete</c> or SQL of your own.
/// </para>
/// <para>
/// Items, projects and contexts are deleted by calling their
/// <c>Delete</c> method, which marks the row; removing one is
/// refused. Queries leave deleted rows out. To see them, as a view
/// of what has been deleted would, add <c>IgnoreQueryFilters()</c>.
/// </para>
/// </remarks>
public sealed class NudgeDbContext(
    DbContextOptions<NudgeDbContext> options,
    TimeProvider? clock = null)
    : DbContext(options)
{
    /// <summary>The column, in the tables whose rows are never
    /// removed, that says when a row was deleted.</summary>
    internal const string DeletedAt = nameof(Item.DeletedAt);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// Items always come with their project, contexts and reminders,
    /// so an item read from here is never half-loaded. The project
    /// comes without its own parents; see <see cref="Projects"/>.
    /// </summary>
    public DbSet<Item> Items => Set<Item>();

    /// <summary>
    /// A project only knows its parent if the parent has been read
    /// too; one that has not shows up as <c>Parent == null</c>, which
    /// looks like a top-level project. Read all projects (there are
    /// few) before relying on <see cref="Project.Ancestors"/> or
    /// calling <see cref="Project.MoveUnder"/>, which cannot see a
    /// loop through a parent it was never given.
    /// </summary>
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Context> Contexts => Set<Context>();

    /// <summary>The change log, oldest first by
    /// <see cref="Change.Id"/>. It is written by saving, never
    /// directly.</summary>
    public DbSet<Change> Changes => Set<Change>();

    /// <summary>Write requests that came with an idempotency key,
    /// and how they were answered. Not part of the change log.
    /// </summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords =>
        Set<IdempotencyRecord>();

    /// <summary>The tokens the API accepts, and the revoked ones.
    /// Not part of the change log.</summary>
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    /// <summary>
    /// Who is making the changes saved through this context: for
    /// example "web", or the name of an API token. It is recorded
    /// with every change, and saving is refused until it is set; a
    /// log that cannot say who did something is of little use when
    /// both the owner and an agent can write.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>What the changes are for, in a few words, such as
    /// "Complete item". Recorded with every change; may be left
    /// empty.</summary>
    public string Description { get; set; } = "";

    /// <summary>The change recorded by the last save through this
    /// context that changed anything, or null if none has.</summary>
    public Change? LastChange { get; private set; }

    internal DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>
    /// Saves, and records what was saved as one entry in the change
    /// log. The number returned counts the rows of that entry too.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Source"/> is not set; or the save would remove the
    /// row of an item, project or context, or alter the change log.
    /// </exception>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var change = Record();
        try
        {
            var rows = base.SaveChanges(acceptAllChangesOnSuccess);
            LastChange = change ?? LastChange;
            return rows;
        }
        catch
        {
            Forget(change);
            throw;
        }
    }

    /// <inheritdoc cref="SaveChanges(bool)"/>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var change = Record();
        try
        {
            var rows = await base.SaveChangesAsync(
                acceptAllChangesOnSuccess,
                cancellationToken);
            LastChange = change ?? LastChange;
            return rows;
        }
        catch
        {
            Forget(change);
            throw;
        }
    }

    /// <summary>
    /// Takes a change back: every row it touched is returned to what
    /// it was before, in one transaction, and that is recorded as a
    /// new change. An undo can itself be undone. Afterwards this
    /// context has forgotten everything it had read, because the
    /// rows were changed underneath it.
    /// </summary>
    /// <param name="changeId">The number of the change, its
    /// <see cref="Change.Id"/>.</param>
    /// <returns>The change that records the undo, or null if there
    /// is no change with that number.</returns>
    /// <exception cref="UndoRefusedException">Something the change
    /// touched has been changed again since, or putting it back
    /// would leave the data inconsistent. Nothing was altered.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Source"/> is not set, or this context has unsaved
    /// changes or an open transaction.</exception>
    public Task<Change?> UndoAsync(
        long changeId,
        CancellationToken cancellationToken = default)
    {
        return new ChangeUndo(this).RunAsync(changeId, cancellationToken);
    }

    /// <summary>
    /// Like <see cref="UndoAsync"/>, but inside the transaction that
    /// is already open on this context, so that the caller decides
    /// whether it is committed and can write more in it. If the undo
    /// is refused, what it had written is rolled back to a savepoint
    /// and the transaction is left as it was before.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Source"/> is not set, this context has unsaved
    /// changes, or no transaction is open.</exception>
    public Task<Change?> UndoWithinAsync(
        long changeId,
        CancellationToken cancellationToken = default)
    {
        return new ChangeUndo(this).RunWithinAsync(
            changeId,
            cancellationToken);
    }

    /// <summary>
    /// Marks a project deleted together with everything under it:
    /// the projects below it at any depth, and the items in any of
    /// them. Nothing is written until the changes are saved, so it
    /// all lands in one change and one undo brings it all back.
    /// What was already deleted is left as it is, so that undo
    /// brings back exactly what this took away.
    /// </summary>
    /// <returns>False if there is no such project, or it is already
    /// deleted.</returns>
    public async Task<bool> DeleteProjectAsync(
        Guid id,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        // All of them, so that each one knows its ancestors.
        var projects = await Projects.ToListAsync(cancellationToken);
        var project = projects.SingleOrDefault(p => p.Id == id);
        if (project is null)
        {
            return false;
        }

        var doomed = projects
            .Where(p => p == project || p.Ancestors().Contains(project))
            .ToList();
        var ids = doomed.Select(p => p.Id).ToList();
        var items = await Items
            .Where(i => i.Project != null && ids.Contains(i.Project.Id))
            .ToListAsync(cancellationToken);

        foreach (var each in doomed)
        {
            each.Delete(at);
        }

        foreach (var item in items)
        {
            item.Delete(at);
        }

        return true;
    }

    internal string RequiredSource()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            throw new InvalidOperationException(
                "Set Source before saving. Every change is recorded"
                + " with who made it.");
        }

        return Source.Trim();
    }

    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder)
    {
        // An item is read together with its reminders and contexts
        // in one query, so it is always seen as it was at a single
        // moment. This is EF Core's default; choosing it here stops
        // the warning that it had to choose for us. Whoever creates
        // the context only supplies the connection.
        optionsBuilder.UseSqlite(
            sqlite => sqlite.UseQuerySplittingBehavior(
                QuerySplittingBehavior.SingleQuery));
    }

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        // Instants are stored as UTC text that sorts in time order.
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<InstantConverter>();

        // Lists and modes are stored by name, so the database stays
        // readable and reordering an enum cannot change what a row
        // means. Priority is left as a number because it is a scale:
        // sorting and comparing it has to work in SQL.
        configurationBuilder
            .Properties<ItemState>()
            .HaveConversion<string>();
        configurationBuilder
            .Properties<RecurrenceMode>()
            .HaveConversion<string>();
        configurationBuilder
            .Properties<ChangeKind>()
            .HaveConversion<string>();

        // Rules are stored as their RRULE text.
        configurationBuilder
            .Properties<RecurrenceRule>()
            .HaveConversion<RecurrenceRuleConverter, RecurrenceRuleComparer>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ItemConfiguration());
        modelBuilder.ApplyConfiguration(new ReminderConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new ContextConfiguration());
        modelBuilder.ApplyConfiguration(new ChangeConfiguration());
        modelBuilder.ApplyConfiguration(new ChangeEntryConfiguration());
        modelBuilder.ApplyConfiguration(
            new IdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ApiTokenConfiguration());
    }

    /// <summary>
    /// Adds to what is about to be saved the change that describes
    /// it, so that both are written together or not at all. Returns
    /// null if there is nothing to record.
    /// </summary>
    private Change? Record()
    {
        var entries = ChangeRecorder.Capture(this);
        if (entries.Count == 0)
        {
            return null;
        }

        var change = new Change(
            Now,
            RequiredSource(),
            Description,
            undoes: null,
            entries);
        Changes.Add(change);
        return change;
    }

    /// <summary>
    /// Drops a change whose save failed. Left in place, it would be
    /// written by the next save, next to a second description of the
    /// same rows.
    /// </summary>
    private void Forget(Change? change)
    {
        if (change is null)
        {
            return;
        }

        // A copy: detaching an entry takes it out of the list.
        foreach (var entry in change.Entries.ToArray())
        {
            Entry(entry).State = EntityState.Detached;
        }

        Entry(change).State = EntityState.Detached;
    }
}
