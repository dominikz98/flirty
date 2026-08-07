namespace Flirty.Placeholders;

/// <summary>
/// The message placeholders a host declared with <c>AddPlaceholder</c>, resolved by key. Registered as a
/// singleton by <c>AddFlirty()</c> – empty by default, replaced by the options overload once at least one
/// placeholder is declared, so it is <b>always</b> resolvable and a client asking which placeholders exist
/// gets an empty list rather than a failure.
/// </summary>
/// <remarks>
/// <para>
/// Keys are compared with <see cref="StringComparer.Ordinal"/>, deliberately <b>not</b> case-insensitively –
/// the same reasoning as <see cref="Flirty.Validation.FlirtyQuestionTypeRegistry"/>: the declaration charset
/// forbids uppercase, so two declared keys can never differ only by case, and a marker whose key is cased
/// differently simply does not resolve (a logged, safe degradation rather than a silent match).
/// </para>
/// <para>
/// The dictionary is populated before the instance is published and never written again, so concurrent
/// reads from the singleton are safe.
/// </para>
/// </remarks>
public sealed class FlirtyPlaceholderRegistry
{
    private readonly Dictionary<string, FlirtyPlaceholder> _placeholders;

    /// <summary>
    /// Creates the registry from the declared placeholders. Internal: a registry is built by
    /// <c>AddFlirty(Action&lt;FlirtyOptions&gt;)</c> out of the declarations, never by a consumer.
    /// </summary>
    /// <param name="placeholders">The declared placeholders, keyed by <see cref="FlirtyPlaceholder.Key"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="placeholders"/> is <see langword="null"/>.</exception>
    internal FlirtyPlaceholderRegistry(IReadOnlyDictionary<string, FlirtyPlaceholder> placeholders)
    {
        ArgumentNullException.ThrowIfNull(placeholders);

        _placeholders = new Dictionary<string, FlirtyPlaceholder>(placeholders, StringComparer.Ordinal);
        Placeholders = [.. _placeholders.Values.OrderBy(placeholder => placeholder.Key, StringComparer.Ordinal)];
    }

    /// <summary>The registry of a host that declared no message placeholder.</summary>
    public static FlirtyPlaceholderRegistry Empty { get; } =
        new(new Dictionary<string, FlirtyPlaceholder>(StringComparer.Ordinal));

    /// <summary>The declared placeholders, ordered by <see cref="FlirtyPlaceholder.Key"/>.</summary>
    public IReadOnlyList<FlirtyPlaceholder> Placeholders { get; }

    /// <summary>Looks up a declared placeholder by its key.</summary>
    /// <param name="key">The key, compared ordinally.</param>
    /// <param name="placeholder">The declared placeholder, or <see langword="null"/> if the key is not declared.</param>
    /// <returns><see langword="true"/> if the key is declared.</returns>
    public bool TryGet(string key, out FlirtyPlaceholder? placeholder)
        => _placeholders.TryGetValue(key, out placeholder);
}
