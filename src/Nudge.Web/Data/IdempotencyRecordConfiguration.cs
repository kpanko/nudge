using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nudge.Web.Data;

internal sealed class IdempotencyRecordConfiguration
    : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <summary>The longest key a client may send.</summary>
    public const int MaxKeyLength = 255;

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        // Keys are compared exactly, case included.
        builder.HasKey(record => record.Key);
        builder.Property(record => record.Key).HasMaxLength(MaxKeyLength);

        builder
            .HasOne<Change>()
            .WithMany()
            .HasForeignKey(record => record.ChangeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
