using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Placeholders;

/// <summary>
/// The context handed to an <see cref="IPlaceholderFiller"/> for one placeholder occurrence: the
/// placeholder <see cref="Key"/> plus the running-session facts a host needs to resolve a live value –
/// "how and from where" is entirely the host's business.
/// </summary>
/// <remarks>
/// The <see cref="ExpressionContext"/> is the same one the branching kernel evaluates against, built by the
/// engine's single <c>SessionExpressionContextBuilder</c> – deliberately reused rather than invented a
/// second time, so a filler reads the answers so far (by <see cref="Question.Key"/>), the loop collections
/// and the iteration index exactly as a branching condition would.
/// </remarks>
/// <param name="Key">The placeholder key from the <c>{{key}}</c> marker being resolved.</param>
/// <param name="SessionId">The primary key of the running <see cref="DialogSession"/>.</param>
/// <param name="ExternalUserKey">The business user key of the host app the session belongs to.</param>
/// <param name="DialogId">The primary key of the pinned dialog version.</param>
/// <param name="DialogKey">The business, stable key of the dialog.</param>
/// <param name="QuestionKey">The business key of the question currently being delivered.</param>
/// <param name="ExpressionContext">
/// The already-built evaluation context of the session (answers by question key, loop collections,
/// iteration index, the current point in time).
/// </param>
public sealed record PlaceholderContext(
    string Key,
    Guid SessionId,
    string ExternalUserKey,
    Guid DialogId,
    string DialogKey,
    string QuestionKey,
    ExpressionContext ExpressionContext);
