using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="AnswerOption"/>. Sets the key and the unique index
/// over <c>(QuestionId, Key)</c>. The relationship to the <see cref="Question"/> is configured in
/// <see cref="QuestionConfiguration"/>.
/// </summary>
internal sealed class AnswerOptionConfiguration : IEntityTypeConfiguration<AnswerOption>
{
    public void Configure(EntityTypeBuilder<AnswerOption> builder)
    {
        builder.HasKey(option => option.Id);

        builder.Property(option => option.Key)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        // Option key unique per question.
        builder.HasIndex(option => new { option.QuestionId, option.Key })
            .IsUnique();
    }
}
