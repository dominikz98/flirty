using System.Collections.ObjectModel;
using Flirty.Domain;

namespace Flirty.Expressions;

/// <summary>
/// The immutable evaluation context against which an <see cref="IExpressionEvaluator"/> evaluates a
/// condition expression. Bundles the data visible at evaluation time of a
/// running <see cref="DialogSession"/>: the previous answers (by question key), the loop
/// collections gathered per iteration (by <c>CollectionKey</c>), the current
/// iteration index, the point in time and the session itself.
/// </summary>
/// <remarks>
/// The values are deliberately kept as <b>raw JSON text</b> (as stored in <see cref="SessionAnswer.Value"/>);
/// the typed deserialization per question type is the responsibility of the concrete
/// engine (issue #23). The building blocks map the five context elements described in
/// <c>docs/ARCHITECTURE.md</c> §7: <c>answers</c>, loop collections, <c>iterationIndex</c>, <c>now</c>,
/// <c>session</c>.
/// </remarks>
public sealed class ExpressionContext
{
    /// <summary>
    /// Creates a new evaluation context. Collections that are not provided are initialized as empty,
    /// non-<see langword="null"/> collections.
    /// </summary>
    /// <param name="session">The running session whose conditions are evaluated.</param>
    /// <param name="now">The evaluation point in time (e.g. for time-based expressions).</param>
    /// <param name="answers">
    /// The previous answers, indexed by the domain question key (<see cref="Question.Key"/>);
    /// values are raw JSON text (<see cref="SessionAnswer.Value"/>). <see langword="null"/> ⇒ empty.
    /// </param>
    /// <param name="collections">
    /// The loop answers gathered per iteration, indexed by the <see cref="LoopDefinition.CollectionKey"/>;
    /// one raw-JSON entry per iteration. <see langword="null"/> ⇒ empty.
    /// </param>
    /// <param name="iterationIndex">
    /// The zero-based iteration index within a loop, or <see langword="null"/> outside
    /// a loop (cf. <see cref="SessionAnswer.IterationIndex"/>).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public ExpressionContext(
        DialogSession session,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string?>? answers = null,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? collections = null,
        int? iterationIndex = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        Now = now;
        Answers = answers ?? ReadOnlyDictionary<string, string?>.Empty;
        Collections = collections ?? ReadOnlyDictionary<string, IReadOnlyList<string?>>.Empty;
        IterationIndex = iterationIndex;
    }

    /// <summary>The running session in whose context the expression is evaluated.</summary>
    public DialogSession Session { get; }

    /// <summary>The evaluation point in time.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>
    /// The previous answers of the session, indexed by the domain question key
    /// (<see cref="Question.Key"/>). The value is the raw JSON text of the answer
    /// (<see cref="SessionAnswer.Value"/>).
    /// </summary>
    public IReadOnlyDictionary<string, string?> Answers { get; }

    /// <summary>
    /// The answers of the loops gathered per iteration, indexed by the
    /// <see cref="LoopDefinition.CollectionKey"/> (e.g. <c>positions</c> for <c>positions.Count &gt; 0</c>).
    /// Each list entry stands for one iteration and carries raw JSON text.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string?>> Collections { get; }

    /// <summary>
    /// The zero-based iteration index within a loop, or <see langword="null"/>
    /// outside a loop.
    /// </summary>
    public int? IterationIndex { get; }
}
