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

        // A project that still has projects under it cannot be
        // deleted.
        builder
            .HasOne(project => project.Parent)
            .WithMany()
            .HasForeignKey("ParentId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
