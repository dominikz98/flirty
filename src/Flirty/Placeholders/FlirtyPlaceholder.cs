namespace Flirty.Placeholders;

/// <summary>
/// A message placeholder declared by the host with <c>AddPlaceholder</c>. A question text or an
/// answer-option label selects it by carrying the <c>{{key}}</c> marker; at delivery time the marker is
/// replaced with the value produced by the registered <see cref="FillerType"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Flirty.Validation.FlirtyQuestionType"/> part for part, and like it holds the filler
/// <b>type</b> rather than an instance – the concrete filler is resolved from the request scope so it may
/// take scoped dependencies. See <see cref="FlirtyPlaceholderRegistry"/>.
/// </remarks>
/// <param name="Key">
/// The stable key the placeholder is declared and referenced under, from inside the <c>{{ }}</c> marker.
/// Restricted to lowercase ASCII letters, digits and <c>-</c> (mirroring <c>AddQuestionType</c>), and
/// compared ordinally – see <see cref="FlirtyPlaceholderRegistry"/>.
/// </param>
/// <param name="DisplayName">A human-readable name, shown in the designer and reported to clients.</param>
/// <param name="FillerType">
/// The <see cref="IPlaceholderFiller"/> implementation registered for this placeholder, or
/// <see langword="null"/> when the placeholder is declared for display only (e.g. the designer, which has
/// no filler because a filler is host-process code). A marker for a filler-less placeholder degrades to its
/// raw text at delivery time.
/// </param>
/// <param name="Sample">
/// An optional example value, so the designer can preview a filled message without running host code and a
/// client can see the expected shape.
/// </param>
public sealed record FlirtyPlaceholder(
    string Key,
    string DisplayName,
    Type? FillerType,
    string? Sample);
