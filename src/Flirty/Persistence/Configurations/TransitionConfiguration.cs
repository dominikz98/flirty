using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für <see cref="Transition"/>. Legt Schlüssel und einen Index für die
/// nach <see cref="Transition.Priority"/> geordnete Auswertung je Ausgangsfrage fest. Die
/// Beziehung zum <see cref="Dialog"/> wird in <see cref="DialogConfiguration"/> konfiguriert;
/// <see cref="Transition.FromQuestionId"/> und <see cref="Transition.TargetQuestionId"/> bleiben
/// bewusst navigationslose Guid-Verweise (kein Fremdschlüssel).
/// </summary>
internal sealed class TransitionConfiguration : IEntityTypeConfiguration<Transition>
{
    public void Configure(EntityTypeBuilder<Transition> builder)
    {
        builder.HasKey(transition => transition.Id);

        // Übergänge je Ausgangsfrage in Prioritätsreihenfolge auffindbar.
        builder.HasIndex(transition => new { transition.DialogId, transition.FromQuestionId, transition.Priority });
    }
}
