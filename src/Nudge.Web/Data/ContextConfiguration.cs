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

        // Two contexts cannot share a name, whatever the case of
        // its (ASCII) letters: "Phone" and "phone" are one context.
        builder.Property(context => context.Name).UseCollation("NOCASE");
        builder.HasIndex(context => context.Name).IsUnique();
    }
}
