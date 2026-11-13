using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nudge.Core;

namespace Nudge.Web.Data;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    // A repeating item has a rule, a mode, a start day and the day
    // of its current occurrence; an item that does not repeat has
    // none of the four.
    private const string RepeatsFullyOrNotAtAll =
        "(RecurrenceRule IS NULL) = (RecurrenceMode IS NULL)"
        + " AND (RecurrenceRule IS NULL) = (RecurrenceAnchor IS NULL)"
        + " AND (RecurrenceRule IS NULL) = (OccurrenceDate IS NULL)";

    // Without a due date there is no due time, and the item does
    // not repeat.
    private const string NoDueDateMeansNoTimeAndNoRule =
        "DueDate IS NOT NULL"
        + " OR (DueTime IS NULL AND RecurrenceRule IS NULL)";

    public void Configure(EntityTypeBuilder<Item> builder)
    {
        // The checks repeat rules that Item already keeps, so that a
        // row which breaks them cannot be written by any other route.
        builder.ToTable(
            "Items",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Items_Recurrence",
                    RepeatsFullyOrNotAtAll);
                table.HasCheckConstraint(
                    "CK_Items_Due",
                    NoDueDateMeansNoTimeAndNoRule);
            });

        // Ids are made by the entity, never by the database. Saying
        // so is what lets EF Core see that a reminder or context
        // added to an item it already tracks is new.
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.ComplexProperty(
            item => item.Recurrence,
            recurrence =>
            {
                recurrence
                    .Property(r => r.Rule)
                    .HasColumnName("RecurrenceRule");
                recurrence
                    .Property(r => r.Mode)
                    .HasColumnName("RecurrenceMode");
                recurrence
                    .Property(r => r.Anchor)
                    .HasColumnName("RecurrenceAnchor");
            });

        // A project that still has items cannot be deleted.
        builder
            .HasOne(item => item.Project)
            .WithMany()
            .HasForeignKey("ProjectId")
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasMany(item => item.Contexts)
            .WithMany()
            .UsingEntity(
                "ItemContexts",
                right => right
                    .HasOne(typeof(Context))
                    .WithMany()
                    .HasForeignKey("ContextId"),
                left => left
                    .HasOne(typeof(Item))
                    .WithMany()
                    .HasForeignKey("ItemId"),
                join => join.HasKey("ItemId", "ContextId"));

        // A reminder belongs to one item and goes when the item goes.
        builder
            .HasMany(item => item.Reminders)
            .WithOne()
            .HasForeignKey("ItemId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(item => item.Project).AutoInclude();
        builder.Navigation(item => item.Contexts).AutoInclude();
        builder.Navigation(item => item.Reminders).AutoInclude();
    }
}
