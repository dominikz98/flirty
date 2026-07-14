using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für <see cref="Question"/>. Legt Schlüssel, das Enum-Mapping für
/// <see cref="QuestionType"/>, den eindeutigen Index über <c>(DialogId, Key)</c> und die
/// kaskadierende Beziehung zu den Antwortoptionen fest. Die Beziehung zum <see cref="Dialog"/>
/// wird in <see cref="DialogConfiguration"/> konfiguriert.
/// </summary>
internal sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.HasKey(question => question.Id);

        builder.Property(question => question.Key)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        builder.Property(question => question.Type)
            .HasConversion<int>();

        // Fragenschlüssel je Dialog eindeutig.
        builder.HasIndex(question => new { question.DialogId, question.Key })
            .IsUnique();

        // ValidationRules trägt anwendungsseitig serialisiertes JSON -> unbegrenzte Textspalte,
        // bewusst ohne MaxLength (gerät so nie in einen Index).

        builder.HasMany(question => question.Options)
            .WithOne(option => option.Question)
            .HasForeignKey(option => option.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
