using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Declares the descriptors read from <c>placeholders.json</c> against the real core registry, by calling
/// <see cref="FlirtyOptions.AddPlaceholder(string, string, string)"/> for each of them.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole mechanism behind the designer's knowledge of host-declared placeholders
/// (<c>docs/adr/0013-message-placeholders-at-the-projection-seam.md</c>): the designer becomes a
/// <b>host</b> and reuses the seam the core already built, so nothing in the core changes and the designer
/// reads the same <see cref="Flirty.Placeholders.FlirtyPlaceholderRegistry"/> a host would – exactly the
/// #137 pattern for question types.
/// </para>
/// <para>
/// The declarations deliberately carry <b>no</b> filler (the filler-less <c>AddPlaceholder</c> overload).
/// A filler is code and lives in the host, so the designer cannot produce a live value; it previews the
/// declared sample instead, and the test runner states that on screen.
/// </para>
/// <para>
/// <b>The core stays the authority on validity.</b> Rather than re-checking the key charset and
/// uniqueness here – a second rule set that would drift – each entry is simply declared and its
/// <see cref="ArgumentException"/> caught. A bad entry is skipped and reported; the rest still load.
/// </para>
/// </remarks>
internal static class DesignerPlaceholders
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
        IEnumerable<PlaceholderDescriptor> descriptors)
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
                // An empty "sample" in the file means "none", not "the empty string" - the preview then
                // simply keeps the raw marker. This is a fact about the file format, not a validity rule,
                // so it does not belong to the core.
                var sample = string.IsNullOrWhiteSpace(descriptor.Sample) ? null : descriptor.Sample;

                options.AddPlaceholder(descriptor.Key, descriptor.DisplayName, sample);
            }
            catch (ArgumentException exception)
            {
                problems.Add($"Entry {position} (\"{descriptor.Key}\") was skipped. {exception.Message}");
            }
        }

        return problems;
    }
}
