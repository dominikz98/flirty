using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für <see cref="DialogLayout"/>. Legt Schlüssel und den eindeutigen Index über
/// (<c>DialogId</c>, <c>ElementKind</c>, <c>ElementId</c>) fest – je Element genau eine Position. Die
/// Beziehung zum <see cref="Dialog"/> wird in <see cref="DialogConfiguration"/> konfiguriert;
/// <see cref="DialogLayout.ElementId"/> bleibt bewusst ein navigationsloser Guid-Verweis (kein
/// Fremdschlüssel), wie die Frage-Verweise in <see cref="LoopDefinition"/>.
/// </summary>
internal sealed class DialogLayoutConfiguration : IEntityTypeConfiguration<DialogLayout>
{
    public void Configure(EntityTypeBuilder<DialogLayout> builder)
    {
        builder.HasKey(layout => layout.Id);

        builder.Property(layout => layout.ElementKind)
            .HasConversion<int>();

        // Alle drei Spalten sind non-nullable – die Regel „keine Unique-Indizes über null-fähige
        // Spalten" (divergente Null-Semantik der Provider, docs/PERSISTENCE.md) ist eingehalten.
        builder.HasIndex(layout => new { layout.DialogId, layout.ElementKind, layout.ElementId })
            .IsUnique();
    }
}
