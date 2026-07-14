using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für <see cref="SessionAnswer"/>. Legt Schlüssel und einen Index über
/// <c>(SessionId, Sequence)</c> fest. Die Beziehung zur <see cref="DialogSession"/> wird in
/// <see cref="DialogSessionConfiguration"/> konfiguriert; <see cref="SessionAnswer.QuestionId"/>
/// bleibt ein bewusst navigationsloser Guid-Verweis. Bewusst KEIN eindeutiger Index über
/// <c>(SessionId, QuestionId)</c>: Loop-Iterationen erlauben mehrere Antworten pro Frage.
/// </summary>
internal sealed class SessionAnswerConfiguration : IEntityTypeConfiguration<SessionAnswer>
{
    public void Configure(EntityTypeBuilder<SessionAnswer> builder)
    {
        builder.HasKey(answer => answer.Id);

        // Antworten je Session in Reihenfolge auffindbar (nicht eindeutig).
        builder.HasIndex(answer => new { answer.SessionId, answer.Sequence });

        // Value trägt anwendungsseitig serialisiertes JSON -> unbegrenzte, erforderliche Textspalte,
        // bewusst ohne MaxLength.
    }
}
