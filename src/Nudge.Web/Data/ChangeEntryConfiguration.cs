using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nudge.Web.Data;

internal sealed class ChangeEntryConfiguration
    : IEntityTypeConfiguration<ChangeEntry>
{
    // A row that was added has no values from before, and one that
    // was removed has none from after. Any other entry has both.
    private const string ValuesMatchTheKind =
        "(\"Before\" IS NULL) = (Kind = 'Added')"
        + " AND (\"After\" IS NULL) = (Kind = 'Removed')";

    public void Configure(EntityTypeBuilder<ChangeEntry> builder)
    {
        builder.ToTable(
            "ChangeEntries",
            table => table.HasCheckConstraint(
                "CK_ChangeEntries_Kind",
                ValuesMatchTheKind));

        builder.HasKey("ChangeId", nameof(ChangeEntry.Ordinal));
        builder.Property(entry => entry.Ordinal).ValueGeneratedNever();

        // Finds everything that ever happened to one row.
        builder.HasIndex(entry => new { entry.TableName, entry.RowKey });
    }
}
