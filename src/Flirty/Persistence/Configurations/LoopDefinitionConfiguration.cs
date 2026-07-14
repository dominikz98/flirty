using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für <see cref="LoopDefinition"/>. Legt den Schlüssel fest. Die Beziehung
/// zum <see cref="Dialog"/> wird in <see cref="DialogConfiguration"/> konfiguriert;
/// <see cref="LoopDefinition.EntryQuestionId"/> und <see cref="LoopDefinition.BreakingQuestionId"/>
/// bleiben bewusst navigationslose Guid-Verweise (kein Fremdschlüssel).
/// </summary>
internal sealed class LoopDefinitionConfiguration : IEntityTypeConfiguration<LoopDefinition>
{
    public void Configure(EntityTypeBuilder<LoopDefinition> builder)
    {
        builder.HasKey(loop => loop.Id);
    }
}
