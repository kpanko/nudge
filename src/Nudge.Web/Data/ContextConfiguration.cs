using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nudge.Core;

namespace Nudge.Web.Data;

internal sealed class ContextConfiguration
    : IEntityTypeConfiguration<Context>
{
    public void Configure(EntityTypeBuilder<Context> builder)
    {
        builder.ToTable("Contexts");

        builder.HasKey(context => context.Id);
        builder.Property(context => context.Id).ValueGeneratedNever();

        // Deleted contexts are left out of queries, and so out of
        // the contexts an item is read with; see the same line for
        // items.
        builder.HasQueryFilter(context => context.DeletedAt == null);

        // Two contexts cannot share a name, whatever the case of
        // its (ASCII) letters: "Phone" and "phone" are one context.
        // A deleted context does not hold on to its name.
        builder.Property(context => context.Name).UseCollation("NOCASE");
        builder
            .HasIndex(context => context.Name)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
