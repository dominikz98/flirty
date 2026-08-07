namespace Flirty.Designer.Models;

/// <summary>
/// One entry of the designer's placeholder descriptor file: the data half of a host-declared message
/// placeholder (#140), as far as the designer can know it.
/// </summary>
/// <remarks>
/// <para>
/// The designer is a separate process and does not share the host's DI container, so it cannot see
/// <c>o.AddPlaceholder&lt;TFiller&gt;(...)</c> and – above all – it has no filler, because a filler is
/// code that lives in the host process. What it can be told is the <b>descriptor</b> – key, display name
/// and an example value – so it can show the marker while authoring and preview it with the sample while
/// testing. See <c>docs/adr/0013-message-placeholders-at-the-projection-seam.md</c>.
/// </para>
/// <para>
/// Deliberately mutable with settable properties, like <see cref="QuestionTypeDescriptor"/>: this is a
/// deserialization target for <c>placeholders.json</c>.
/// </para>
/// </remarks>
internal sealed class PlaceholderDescriptor
{
    /// <summary>
    /// The key referenced inside a <c>{{ }}</c> marker. Lowercase ASCII letters, digits and <c>-</c> only –
    /// the core enforces that, this type does not repeat the rule.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The human-readable name shown instead of the raw key.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>An optional example value the test runner previews the marker with.</summary>
    public string? Sample { get; set; }
}
