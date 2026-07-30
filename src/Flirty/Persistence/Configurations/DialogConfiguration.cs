using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the configuration aggregate root <see cref="Dialog"/>. Sets the key,
/// the unique index over <c>(Key, Version)</c> as well as the cascading relationships to the
/// child entities (questions, transitions, loops, triggers, layout).
/// </summary>
internal sealed class DialogConfiguration : IEntityTypeConfiguration<Dialog>
{
    public void Configure(EntityTypeBuilder<Dialog> builder)
    {
        builder.HasKey(dialog => dialog.Id);

        // Indexed key column: bounded length so it is indexable across all providers
        // (SQL Server does not allow nvarchar(max) as an index key).
        builder.Property(dialog => dialog.Key)
            .HasMaxLength(PersistenceConstants.KeyMaxLength);

        // Business key exactly once per version (multiple versions of the same key are allowed).
        builder.HasIndex(dialog => new { dialog.Key, dialog.Version })
            .IsUnique();

        // Aggregate-internal relationships: explicit FK binding (prevents shadow FKs) + cascade delete.
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

        builder.HasMany(dialog => dialog.Layout)
            .WithOne(layout => layout.Dialog)
            .HasForeignKey(layout => layout.DialogId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
