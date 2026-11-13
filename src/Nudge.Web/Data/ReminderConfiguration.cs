using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nudge.Core;

namespace Nudge.Web.Data;

internal sealed class ReminderConfiguration
    : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable(
            "Reminders",
            table =>
            {
                // A fixed instant or an offset from the due date,
                // never both and never neither: a reminder with
                // neither would never fire and never say so.
                table.HasCheckConstraint(
                    "CK_Reminders_Kind",
                    """("At" IS NULL) <> ("Offset" IS NULL)""");
            });

        builder.HasKey(reminder => reminder.Id);
        builder.Property(reminder => reminder.Id).ValueGeneratedNever();

        // Read-only properties are left out unless named here.
        builder.Property(reminder => reminder.At);
        builder.Property(reminder => reminder.Offset);
    }
}
