using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="SessionAnswer"/>. Sets the key and an index over
/// <c>(SessionId, Sequence)</c>. The relationship to the <see cref="DialogSession"/> is configured in
/// <see cref="DialogSessionConfiguration"/>; <see cref="SessionAnswer.QuestionId"/>
/// stays a deliberately navigation-less GUID reference. Deliberately NO unique index over
/// <c>(SessionId, QuestionId)</c>: loop iterations allow multiple answers per question.
/// </summary>
internal sealed class SessionAnswerConfiguration : IEntityTypeConfiguration<SessionAnswer>
{
    public void Configure(EntityTypeBuilder<SessionAnswer> builder)
    {
        builder.HasKey(answer => answer.Id);

        // Answers discoverable per session in order (not unique).
        builder.HasIndex(answer => new { answer.SessionId, answer.Sequence });

        // Value carries application-side serialized JSON -> unbounded, required text column,
        // deliberately without MaxLength.
    }
}
