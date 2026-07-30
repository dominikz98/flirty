using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="DialogLayout"/>. Sets the key and the unique index over
/// (<c>DialogId</c>, <c>ElementKind</c>, <c>ElementId</c>) - exactly one position per element. The
/// relationship to the <see cref="Dialog"/> is configured in <see cref="DialogConfiguration"/>;
/// <see cref="DialogLayout.ElementId"/> deliberately stays a navigation-less GUID reference (no
/// foreign key), like the question references in <see cref="LoopDefinition"/>.
/// </summary>
internal sealed class DialogLayoutConfiguration : IEntityTypeConfiguration<DialogLayout>
{
    public void Configure(EntityTypeBuilder<DialogLayout> builder)
    {
        builder.HasKey(layout => layout.Id);

        builder.Property(layout => layout.ElementKind)
            .HasConversion<int>();

        // All three columns are non-nullable - the rule "no unique indexes over nullable
        // columns" (divergent null semantics across providers, docs/PERSISTENCE.md) is honored.
        builder.HasIndex(layout => new { layout.DialogId, layout.ElementKind, layout.ElementId })
            .IsUnique();
    }
}
