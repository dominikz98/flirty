using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TriggerDefinition"/>. Sets the key and the enum mapping
/// for <see cref="TriggerScope"/> and <see cref="TriggerKind"/>. The relationship to the
/// <see cref="Dialog"/> is configured in <see cref="DialogConfiguration"/>;
/// <see cref="TriggerDefinition.QuestionId"/> stays a deliberately navigation-less GUID reference.
/// </summary>
internal sealed class TriggerDefinitionConfiguration : IEntityTypeConfiguration<TriggerDefinition>
{
    public void Configure(EntityTypeBuilder<TriggerDefinition> builder)
    {
        builder.HasKey(trigger => trigger.Id);

        builder.Property(trigger => trigger.Scope)
            .HasConversion<int>();

        builder.Property(trigger => trigger.Kind)
            .HasConversion<int>();

        // Config carries application-side serialized JSON -> unbounded, required text column,
        // deliberately without MaxLength. (Being required is already derived from the non-nullable property.)
    }
}
