using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für das Runtime-Aggregat-Root <see cref="DialogSession"/>. Legt Schlüssel,
/// das Enum-Mapping für <see cref="SessionStatus"/>, einen Lookup-Index über
/// <c>(DialogId, ExternalUserKey)</c> und die kaskadierende Beziehung zu den Antworten fest.
/// </summary>
internal sealed class DialogSessionConfiguration : IEntityTypeConfiguration<DialogSession>
{
    public void Configure(EntityTypeBuilder<DialogSession> builder)
    {
        builder.HasKey(session => session.Id);

        builder.Property(session => session.ExternalUserKey)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        // Status explizit als int speichern (EF-Default, aber als Guard festgehalten).
        builder.Property(session => session.Status)
            .HasConversion<int>();

        // Sessions eines Anwenders je Dialog auffindbar (nicht eindeutig: mehrere Sessions möglich).
        builder.HasIndex(session => new { session.DialogId, session.ExternalUserKey });

        // DialogId ist ein bewusst navigationsloser Verweis über die Aggregatgrenze (kein FK).
        builder.HasMany(session => session.Answers)
            .WithOne(answer => answer.Session)
            .HasForeignKey(answer => answer.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
