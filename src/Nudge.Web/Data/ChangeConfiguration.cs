using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nudge.Web.Data;

internal sealed class ChangeConfiguration : IEntityTypeConfiguration<Change>
{
    public void Configure(EntityTypeBuilder<Change> builder)
    {
        builder.ToTable("Changes");

        // The one id in the database that the database makes: a
        // number that goes up with every change, so the order of the
        // log is the order things were saved in.
        builder.HasKey(change => change.Id);

        builder
            .HasOne<Change>()
            .WithMany()
            .HasForeignKey(change => change.Undoes)
            .OnDelete(DeleteBehavior.Restrict);

        // A change is read whole, like an item. The log is only ever
        // added to, so nothing here goes when something else goes.
        builder
            .HasMany(change => change.Entries)
            .WithOne()
            .HasForeignKey("ChangeId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(change => change.Entries).AutoInclude();
    }
}
