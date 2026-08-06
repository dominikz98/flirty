using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Declares the descriptors read from <c>question-types.json</c> against the real core registry, by
/// calling <see cref="FlirtyOptions.AddQuestionType(string, string, string)"/> for each of them.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole mechanism behind the designer's knowledge of host-declared question types
/// (<c>docs/adr/0012-designer-question-type-descriptors-at-startup.md</c>): the designer becomes a
/// <b>host</b> in the sense of ADR 0011 and reuses the seam that EPIC 14 already built, so nothing in the
/// core changes and the designer reads the same
/// <see cref="Flirty.Validation.FlirtyQuestionTypeRegistry"/> a host would.
/// </para>
/// <para>
/// The declarations deliberately carry <b>no</b> validator. That is a legitimate declaration rather than
/// a half-finished one – a validator is code and lives in the host – and it is the reason the semantic
/// delta stays open and has to be stated on screen in the test runner.
/// </para>
/// <para>
/// <b>The core stays the authority on validity.</b> Rather than re-checking the key charset, the sample
/// JSON and uniqueness here – a second rule set that would drift – each entry is simply declared and its
/// <see cref="ArgumentException"/> caught. A bad entry is skipped and reported; the rest still load.
/// Throwing would take the whole designer down over a display name.
/// </para>
/// </remarks>
internal static class DesignerQuestionTypes
{
    /// <summary>Declares every usable descriptor on the given options.</summary>
    /// <param name="options">The Flirty options being configured in <see cref="DesignerApp"/>.</param>
    /// <param name="descriptors">The descriptors read from the file, in file order.</param>
    /// <returns>
    /// Human-readable problems for the entries that were skipped, in file order. Empty when all of them
    /// were declared.
    /// </returns>
    public static IReadOnlyList<string> Declare(
        FlirtyOptions options,
        IEnumerable<QuestionTypeDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(descriptors);

        var problems = new List<string>();
        var position = 0;

        foreach (var descriptor in descriptors)
        {
            position++;

            try
            {
                // An empty "sample" in the file means "none", not "the empty string" - the latter is not
                // valid JSON and the core would rightly refuse it. This is a fact about the file format,
                // not a validity rule, so it does not belong to the core.
                var sample = string.IsNullOrWhiteSpace(descriptor.Sample) ? null : descriptor.Sample;

                options.AddQuestionType(descriptor.Key, descriptor.DisplayName, sample);
            }
            catch (ArgumentException exception)
            {
                problems.Add($"Entry {position} (\"{descriptor.Key}\") was skipped. {exception.Message}");
            }
        }

        return problems;
    }
}
