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

        // CustomTypeKey is a business key like Key and therefore bounded, so it stays indexable on
        // every provider. It deliberately gets no index of its own: nothing queries by it - the
        // lookup happens against the in-memory registry the host declared - and a unique index over
        // a nullable column would behave differently on SQL Server than on SQLite/PostgreSQL.
        builder.Property(question => question.CustomTypeKey)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        builder.HasMany(question => question.Options)
            .WithOne(option => option.Question)
            .HasForeignKey(option => option.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
