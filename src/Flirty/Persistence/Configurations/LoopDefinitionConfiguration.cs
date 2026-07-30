using Flirty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flirty.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="LoopDefinition"/>. Sets the key. The relationship
/// to the <see cref="Dialog"/> is configured in <see cref="DialogConfiguration"/>;
/// <see cref="LoopDefinition.EntryQuestionId"/> and <see cref="LoopDefinition.BreakingQuestionId"/>
/// deliberately stay navigation-less GUID references (no foreign key).
/// </summary>
internal sealed class LoopDefinitionConfiguration : IEntityTypeConfiguration<LoopDefinition>
{
    public void Configure(EntityTypeBuilder<LoopDefinition> builder)
    {
        builder.HasKey(loop => loop.Id);
    }
}
