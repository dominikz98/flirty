using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für <see cref="AnswerOption"/>. Legt Schlüssel und den eindeutigen Index
/// über <c>(QuestionId, Key)</c> fest. Die Beziehung zur <see cref="Question"/> wird in
/// <see cref="QuestionConfiguration"/> konfiguriert.
/// </summary>
internal sealed class AnswerOptionConfiguration : IEntityTypeConfiguration<AnswerOption>
{
    public void Configure(EntityTypeBuilder<AnswerOption> builder)
    {
        builder.HasKey(option => option.Id);

        builder.Property(option => option.Key)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        // Optionsschlüssel je Frage eindeutig.
        builder.HasIndex(option => new { option.QuestionId, option.Key })
            .IsUnique();
    }
}
