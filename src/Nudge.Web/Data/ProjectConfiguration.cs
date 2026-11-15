using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nudge.Core;

namespace Nudge.Web.Data;

internal sealed class ProjectConfiguration
    : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();

        // Deleted projects are left out of queries; see the same
        // line for items.
        builder.HasQueryFilter(project => project.DeletedAt == null);

        // The row of a project that still has projects under it
        // cannot be removed. ClientNoAction, for the reason given for
        // an item's project.
        builder
            .HasOne(project => project.Parent)
            .WithMany()
            .HasForeignKey("ParentId")
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}
