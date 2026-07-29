using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="Transition"/>. Sets the key and an index for the
/// evaluation ordered by <see cref="Transition.Priority"/> per source question. The
/// relationship to the <see cref="Dialog"/> is configured in <see cref="DialogConfiguration"/>;
/// <see cref="Transition.FromQuestionId"/> and <see cref="Transition.TargetQuestionId"/> stay
/// deliberately navigation-less GUID references (no foreign key).
/// </summary>
internal sealed class TransitionConfiguration : IEntityTypeConfiguration<Transition>
{
    public void Configure(EntityTypeBuilder<Transition> builder)
    {
        builder.HasKey(transition => transition.Id);

        // Transitions discoverable per source question in priority order.
        builder.HasIndex(transition => new { transition.DialogId, transition.FromQuestionId, transition.Priority });
    }
}
