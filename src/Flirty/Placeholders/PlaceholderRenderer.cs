using System.Text;
using System.Text.RegularExpressions;
using Flirty.Domain;
using Flirty.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flirty.Placeholders;

/// <summary>
/// The one seam where a delivered <see cref="QuestionView"/> is produced with its <c>{{key}}</c> markers
/// replaced by live values. Wraps the unchanged <see cref="QuestionProjection.ResolveQuestion"/> and fills
/// the markers in the question text and every answer-option label, resolving each declared
/// <see cref="IPlaceholderFiller"/> from the request scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async and session-aware, and gated by absence.</b> The projection helper is static and sync and has
/// no session, but a live-data filler is I/O by nature and needs the running session as context – so the
/// replacement step lives here instead. When no placeholder is declared, <see cref="RenderAsync"/> returns
/// the projected view untouched: no scanning, no context build, no filler resolution. A dialog without
/// placeholders is therefore byte-for-byte what it was before this feature. See ADR 0013.
/// </para>
/// <para>
/// <b>Best-effort.</b> An unknown key, a filler that throws or one that returns <see langword="null"/> all
/// degrade the single marker to its raw <c>{{key}}</c> text and log a warning; one misbehaving placeholder
/// never poisons the rest of the text, and nothing here breaks start/submit/resume/edit (ADR 0005). Only
/// an <see cref="OperationCanceledException"/> is allowed to propagate – that is a genuine cancellation of
/// the delivery, not a placeholder failure.
/// </para>
/// <para>
/// <b>One level of recursion only.</b> A filled value is not re-scanned for markers. Within a single render
/// a key is resolved at most once (the value is cached), so the same marker used in the text and in an
/// option label is consistent and costs one filler call.
/// </para>
/// </remarks>
internal sealed class PlaceholderRenderer
{
    // {{key}}, key restricted to the AddPlaceholder charset so a marker overlaps nothing in the branching
    // sandbox (which runs raw C# where { } [ ] $ all carry meaning). A token with any other character
    // simply does not match and is left verbatim - it was never a marker. See ADR 0013.
    private static readonly Regex MarkerPattern =
        new(@"\{\{([a-z0-9-]+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly FlirtyPlaceholderRegistry _registry;
    private readonly IServiceProvider _scope;
    private readonly ILogger<PlaceholderRenderer> _logger;

    /// <summary>Creates the renderer over the declared placeholders and the request scope.</summary>
    /// <param name="registry">The placeholders the host declared.</param>
    /// <param name="scope">
    /// The request scope a filler is resolved from – injected into a <b>scoped</b> service, so it shares the
    /// <c>FlirtyDbContext</c> with the handler (the same reasoning as the custom-question-type decorator).
    /// </param>
    /// <param name="logger">Logger for the best-effort degradation paths.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public PlaceholderRenderer(
        FlirtyPlaceholderRegistry registry,
        IServiceProvider scope,
        ILogger<PlaceholderRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _scope = scope;
        _logger = logger;
    }

    /// <summary>
    /// A renderer with no placeholders declared: the gated-by-absence pass-through. Used where a caller has
    /// no DI scope to inject (the handler unit tests) and wants the plain projection explicitly.
    /// </summary>
    public static PlaceholderRenderer Disabled { get; } = new(
        FlirtyPlaceholderRegistry.Empty,
        EmptyServiceProvider.Instance,
        NullLogger<PlaceholderRenderer>.Instance);

    /// <summary>
    /// Resolves the question via <see cref="QuestionProjection.ResolveQuestion"/> and fills the
    /// <c>{{key}}</c> markers in its text and option labels with live values.
    /// </summary>
    /// <param name="dialog">The loaded dialog graph (incl. questions and options).</param>
    /// <param name="session">The running session that gives a filler its context.</param>
    /// <param name="questionId">The id of the question to resolve and render.</param>
    /// <param name="cancellationToken">Propagates a request to cancel the delivery.</param>
    /// <returns>The projected <see cref="QuestionView"/>, with markers filled where possible.</returns>
    /// <exception cref="InvalidOperationException">
    /// The question does not belong to the dialog graph (from <see cref="QuestionProjection.ResolveQuestion"/>).
    /// </exception>
    public async ValueTask<QuestionView> RenderAsync(
        Dialog dialog, DialogSession session, Guid? questionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(session);

        var view = QuestionProjection.ResolveQuestion(dialog, questionId);

        // Gated by absence: nothing declared -> nothing to do, and no async work either.
        if (_registry.Placeholders.Count == 0)
        {
            return view;
        }

        var textHasMarker = ContainsMarker(view.Text);
        var optionsHaveMarker = view.Options.Any(option => ContainsMarker(option.Label));
        if (!textHasMarker && !optionsHaveMarker)
        {
            return view;
        }

        // Built once, lazily, and only when there is at least one marker to fill. Reuses the engine's single
        // context source rather than inventing a second one.
        var expressionContext = SessionExpressionContextBuilder.Build(dialog, session, questionId);
        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);

        var text = textHasMarker
            ? await FillAsync(view.Text, dialog, session, view.Key, expressionContext, cache, cancellationToken)
            : view.Text;

        IReadOnlyList<AnswerOptionView> options = view.Options;
        if (optionsHaveMarker)
        {
            var filled = new List<AnswerOptionView>(view.Options.Count);
            foreach (var option in view.Options)
            {
                var label = ContainsMarker(option.Label)
                    ? await FillAsync(
                        option.Label, dialog, session, view.Key, expressionContext, cache, cancellationToken)
                    : option.Label;
                filled.Add(option with { Label = label });
            }

            options = filled;
        }

        return view with { Text = text, Options = options };
    }

    private static bool ContainsMarker(string text) => MarkerPattern.IsMatch(text);

    /// <summary>
    /// Replaces every <c>{{key}}</c> marker in <paramref name="source"/> with the resolved value, leaving a
    /// marker raw where the value degrades. Only the original text is scanned – a filled value is never
    /// re-scanned (one level of recursion).
    /// </summary>
    private async ValueTask<string> FillAsync(
        string source,
        Dialog dialog,
        DialogSession session,
        string questionKey,
        Expressions.ExpressionContext expressionContext,
        Dictionary<string, string?> cache,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(source.Length);
        var cursor = 0;

        foreach (Match match in MarkerPattern.Matches(source))
        {
            builder.Append(source, cursor, match.Index - cursor);

            var key = match.Groups[1].Value;
            var value = await ResolveAsync(
                key, dialog, session, questionKey, expressionContext, cache, cancellationToken);

            // A degraded value (null) leaves the raw marker in place, so the failure stays visible to the
            // author rather than being silently swallowed.
            builder.Append(value ?? match.Value);
            cursor = match.Index + match.Length;
        }

        builder.Append(source, cursor, source.Length - cursor);
        return builder.ToString();
    }

    private async ValueTask<string?> ResolveAsync(
        string key,
        Dialog dialog,
        DialogSession session,
        string questionKey,
        Expressions.ExpressionContext expressionContext,
        Dictionary<string, string?> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var value = await ResolveUncachedAsync(
            key, dialog, session, questionKey, expressionContext, cancellationToken);
        cache[key] = value;
        return value;
    }

    private async ValueTask<string?> ResolveUncachedAsync(
        string key,
        Dialog dialog,
        DialogSession session,
        string questionKey,
        Expressions.ExpressionContext expressionContext,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGet(key, out var placeholder))
        {
            _logger.LogWarning(
                "A message of dialog '{DialogKey}' references the placeholder '{PlaceholderKey}', which "
                + "this host has not declared with AddPlaceholder. The raw marker was left in place.",
                dialog.Key,
                key);
            return null;
        }

        if (placeholder!.FillerType is null)
        {
            // Declared for display only (e.g. the designer, which has no filler): there is nothing to
            // resolve at runtime, so the marker degrades to raw exactly as an unknown key would.
            _logger.LogWarning(
                "The placeholder '{PlaceholderKey}' referenced by dialog '{DialogKey}' was declared without "
                + "a filler, so no live value could be produced. The raw marker was left in place.",
                key,
                dialog.Key);
            return null;
        }

        try
        {
            var filler = (IPlaceholderFiller)_scope.GetRequiredService(placeholder.FillerType);
            var context = new PlaceholderContext(
                key, session.Id, session.ExternalUserKey, dialog.Id, dialog.Key, questionKey, expressionContext);

            var value = await filler.FillAsync(context, cancellationToken);
            if (value is null)
            {
                // A null return is a deliberate "no value" from the filler; it degrades exactly like a
                // failure, and is logged so the gap is findable rather than silently swallowed.
                _logger.LogWarning(
                    "The filler for placeholder '{PlaceholderKey}' (dialog '{DialogKey}') returned no value. "
                    + "The raw marker was left in place.",
                    key,
                    dialog.Key);
            }

            return value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The filler for placeholder '{PlaceholderKey}' (dialog '{DialogKey}') failed. The raw marker "
                + "was left in place.",
                key,
                dialog.Key);
            return null;
        }
    }

    /// <summary>An <see cref="IServiceProvider"/> that resolves nothing, for the <see cref="Disabled"/> renderer.</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
