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
/// </remarks>
public sealed class NudgeDbContext(DbContextOptions<NudgeDbContext> options)
    : DbContext(options)
{
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
    }
}
