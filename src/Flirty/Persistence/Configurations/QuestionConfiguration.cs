using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="Question"/>. Sets the key, the enum mapping for
/// <see cref="QuestionType"/>, the unique index over <c>(DialogId, Key)</c> and the
/// cascading relationship to the answer options. The relationship to the <see cref="Dialog"/>
/// is configured in <see cref="DialogConfiguration"/>.
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

        // Question key unique per dialog.
        builder.HasIndex(question => new { question.DialogId, question.Key })
            .IsUnique();

        // ValidationRules carries application-side serialized JSON -> unbounded text column,
        // deliberately without MaxLength (thus it never ends up in an index).

        builder.HasMany(question => question.Options)
            .WithOne(option => option.Question)
            .HasForeignKey(option => option.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
