using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF-Core-Konfiguration für das Konfigurations-Aggregat-Root <see cref="Dialog"/>. Legt Schlüssel,
/// den eindeutigen Index über <c>(Key, Version)</c> sowie die kaskadierenden Beziehungen zu den
/// Kind-Entities (Fragen, Übergänge, Schleifen, Trigger) fest.
/// </summary>
internal sealed class DialogConfiguration : IEntityTypeConfiguration<Dialog>
{
    public void Configure(EntityTypeBuilder<Dialog> builder)
    {
        builder.HasKey(dialog => dialog.Id);

        // Indizierte Key-Spalte: begrenzte Länge, damit sie über alle Provider indizierbar ist
        // (SQL Server lässt nvarchar(max) nicht als Indexschlüssel zu).
        builder.Property(dialog => dialog.Key)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        // Fachlicher Schlüssel je Version genau einmal (mehrere Versionen desselben Key sind erlaubt).
        builder.HasIndex(dialog => new { dialog.Key, dialog.Version })
            .IsUnique();

        // Aggregat-interne Beziehungen: explizite FK-Bindung (verhindert Shadow-FKs) + Cascade-Delete.
        builder.HasMany(dialog => dialog.Questions)
            .WithOne(question => question.Dialog)
            .HasForeignKey(question => question.DialogId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(dialog => dialog.Transitions)
            .WithOne(transition => transition.Dialog)
            .HasForeignKey(transition => transition.DialogId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(dialog => dialog.Loops)
            .WithOne(loop => loop.Dialog)
            .HasForeignKey(loop => loop.DialogId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(dialog => dialog.Triggers)
            .WithOne(trigger => trigger.Dialog)
            .HasForeignKey(trigger => trigger.DialogId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
