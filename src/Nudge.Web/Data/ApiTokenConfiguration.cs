using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nudge.Web.Data;

internal sealed class ApiTokenConfiguration
    : IEntityTypeConfiguration<ApiToken>
{
    /// <summary>The longest name a token may have.</summary>
    public const int MaxNameLength = 64;

    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("ApiTokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        // Unique among all tokens, revoked ones included, so a name
        // in the change log means one token. "Agent" and "agent" are
        // the same name.
        builder
            .Property(token => token.Name)
            .HasMaxLength(MaxNameLength)
            .UseCollation("NOCASE");
        builder.HasIndex(token => token.Name).IsUnique();

        // A token is looked up by its hash on every request.
        builder.Property(token => token.Hash).HasMaxLength(64);
        builder.HasIndex(token => token.Hash).IsUnique();
    }
}
