namespace Flirty.Validation;

/// <summary>
/// The custom question types a host declared with <c>AddQuestionType</c>, resolved by key. Registered
/// as a singleton by <c>AddFlirty()</c> – empty by default, replaced by the options overload once at
/// least one type is declared, so it is <b>always</b> resolvable and a client asking which types exist
/// gets an empty list rather than a failure.
/// </summary>
/// <remarks>
/// <para>
/// Keys are compared with <see cref="StringComparer.Ordinal"/>, deliberately <b>not</b>
/// case-insensitively. The key is a value persisted in
/// <see cref="Flirty.Domain.Question.CustomTypeKey"/>, and the engine does not control the collation of
/// the database it is stored in; ordinal comparison is the one behaviour that is identical on SQLite,
/// PostgreSQL and SQL Server. Because the declaration charset forbids uppercase outright, two declared
/// keys can never differ only by case, so nothing is lost. A question authored with a differently cased
/// key simply does not resolve – which is the documented, safe outcome (validate as plain JSON, log a
/// warning), not a silent mismatch.
/// </para>
/// <para>
/// The dictionary is populated before the instance is published and never written again, so concurrent
/// reads from the singleton are safe.
/// </para>
/// </remarks>
public sealed class FlirtyQuestionTypeRegistry
{
    private readonly Dictionary<string, FlirtyQuestionType> _types;

    /// <summary>
    /// Creates the registry from the declared types. Internal: a registry is built by
    /// <c>AddFlirty(Action&lt;FlirtyOptions&gt;)</c> out of the declarations, never by a consumer.
    /// </summary>
    /// <param name="types">The declared types, keyed by <see cref="FlirtyQuestionType.Key"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    internal FlirtyQuestionTypeRegistry(IReadOnlyDictionary<string, FlirtyQuestionType> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        _types = new Dictionary<string, FlirtyQuestionType>(types, StringComparer.Ordinal);
        Types = [.. _types.Values.OrderBy(type => type.Key, StringComparer.Ordinal)];
    }

    /// <summary>The registry of a host that declared no custom question type.</summary>
    public static FlirtyQuestionTypeRegistry Empty { get; } =
        new(new Dictionary<string, FlirtyQuestionType>(StringComparer.Ordinal));

    /// <summary>The declared types, ordered by <see cref="FlirtyQuestionType.Key"/>.</summary>
    public IReadOnlyList<FlirtyQuestionType> Types { get; }

    /// <summary>Looks up a declared type by its key.</summary>
    /// <param name="key">The key, compared ordinally.</param>
    /// <param name="type">The declared type, or <see langword="null"/> if the key is not declared.</param>
    /// <returns><see langword="true"/> if the key is declared.</returns>
    public bool TryGet(string key, out FlirtyQuestionType? type) => _types.TryGetValue(key, out type);
}
