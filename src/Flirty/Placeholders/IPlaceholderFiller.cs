namespace Flirty.Placeholders;

/// <summary>
/// Produces the live value a message placeholder is replaced with at delivery time. A host registers an
/// implementation with <c>o.AddPlaceholder&lt;TFiller&gt;(key, displayName)</c>; the engine resolves it
/// from the request scope and calls it whenever a delivered question text or answer-option label carries
/// the matching <c>{{key}}</c> marker.
/// </summary>
/// <remarks>
/// <para>
/// <b>An interface, not a delegate, deliberately.</b> Producing live data is I/O by nature – a database
/// row, an HTTP call, a lookup keyed by the <see cref="PlaceholderContext.ExternalUserKey"/> – so a filler
/// typically needs scoped dependencies (the same <c>FlirtyDbContext</c> the handler uses, an
/// <c>IHttpClientFactory</c>, <c>IOptions</c>). A plain <c>Func&lt;&gt;</c> cannot take those, which is the
/// whole point of resolving code on demand. See ADR 0013.
/// </para>
/// <para>
/// <b>Everything here is best-effort.</b> Returning <see langword="null"/> or throwing both degrade the
/// one marker to its raw <c>{{key}}</c> text and log a warning; neither breaks
/// start/submit/resume/edit. A published dialog cannot be repaired (ADR 0005), so a misbehaving filler
/// must never turn a delivery into a failure. The value is <b>never persisted</b> – it is resolved fresh on
/// every delivery.
/// </para>
/// </remarks>
public interface IPlaceholderFiller
{
    /// <summary>
    /// Returns the live value for one occurrence of the placeholder described by <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// The placeholder key together with the running-session facts a host needs to decide how and from
    /// where to resolve the value (see <see cref="PlaceholderContext"/>).
    /// </param>
    /// <param name="cancellationToken">Propagates a request to cancel the delivery.</param>
    /// <returns>
    /// The value to substitute for the marker, or <see langword="null"/> to leave the raw marker in place
    /// (a logged, non-fatal degradation).
    /// </returns>
    ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken);
}
