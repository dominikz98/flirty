using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the runtime aggregate root <see cref="DialogSession"/>. Sets the key,
/// the enum mapping for <see cref="SessionStatus"/>, a lookup index over
/// <c>(DialogId, ExternalUserKey)</c> and the cascading relationship to the answers.
/// </summary>
internal sealed class DialogSessionConfiguration : IEntityTypeConfiguration<DialogSession>
{
    public void Configure(EntityTypeBuilder<DialogSession> builder)
    {
        builder.HasKey(session => session.Id);

        builder.Property(session => session.ExternalUserKey)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        // Store the status explicitly as int (EF default, but recorded as a guard).
        builder.Property(session => session.Status)
            .HasConversion<int>();

        // A user's sessions discoverable per dialog (not unique: multiple sessions possible).
        builder.HasIndex(session => new { session.DialogId, session.ExternalUserKey });

        // DialogId is a deliberately navigation-less reference across the aggregate boundary (no FK).
        builder.HasMany(session => session.Answers)
            .WithOne(answer => answer.Session)
            .HasForeignKey(answer => answer.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
