using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für <see cref="TriggerDefinition"/>. Legt Schlüssel und das Enum-Mapping
/// für <see cref="TriggerScope"/> und <see cref="TriggerKind"/> fest. Die Beziehung zum
/// <see cref="Dialog"/> wird in <see cref="DialogConfiguration"/> konfiguriert;
/// <see cref="TriggerDefinition.QuestionId"/> bleibt ein bewusst navigationsloser Guid-Verweis.
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

        // Config trägt anwendungsseitig serialisiertes JSON -> unbegrenzte, erforderliche Textspalte,
        // bewusst ohne MaxLength. (Erforderlich wird bereits aus der Non-Nullable-Property abgeleitet.)
    }
}
