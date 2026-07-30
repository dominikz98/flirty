namespace Flirty.Expressions;

/// <summary>
/// Interchangeable expression engine that evaluates the boolean condition expressions of branching –
/// in particular <c>Transition.Expression</c> (which transition applies) and
/// <c>TriggerDefinition.Expression</c> (whether a trigger fires). The evaluation runs
/// against the <see cref="ExpressionContext"/> (answers by question key, loop collections
/// gathered per iteration, iteration index, evaluation point in time, session).
/// </summary>
/// <remarks>
/// This interface from issue #22 only defines the abstraction. The sandboxed default implementation
/// <c>DynamicExpressoExpressionEvaluator</c> (member whitelist, no raw C# <c>eval</c>) follows in
/// issue #23, the compile/validation path for the designer in issue #24. The engine is registered and
/// made interchangeable via <c>o.UseExpressionEvaluator&lt;T&gt;()</c> in issue #34.
/// </remarks>
public interface IExpressionEvaluator
{
    /// <summary>
    /// Evaluates the boolean condition expression <paramref name="expression"/> against the
    /// given <paramref name="context"/>.
    /// </summary>
    /// <param name="expression">
    /// The condition expression to evaluate. Implementations may expect a non-empty expression;
    /// treating a <see langword="null"/>/empty expression as "unconditionally
    /// matching" is the responsibility of the calling runtime, not the evaluator.
    /// </param>
    /// <param name="context">The evaluation context with answers, loop collections, iteration index, point in time and session.</param>
    /// <returns><see langword="true"/> if the expression matches, otherwise <see langword="false"/>.</returns>
    bool Evaluate(string expression, ExpressionContext context);

    /// <summary>
    /// Validates the condition expression <paramref name="expression"/> against the <paramref name="context"/>
    /// by <b>compiling but not executing</b> it (compile check). Intended for the designer,
    /// to check expressions on save and report errors without an exception being thrown.
    /// </summary>
    /// <remarks>
    /// The context provides the available expression variables (and their types) for the check – so
    /// besides syntax errors, unknown identifiers, non-whitelisted types/members
    /// (injection defense) and a non-boolean result are also detected. A <see langword="null"/>/empty
    /// expression counts as valid ("unconditionally matching", consistent with the runtime semantics).
    /// </remarks>
    /// <param name="expression">The condition expression to check; <see langword="null"/>/empty is valid.</param>
    /// <param name="context">The context that provides the available variables for the check.</param>
    /// <returns>
    /// An <see cref="ExpressionValidationResult"/> with <see cref="ExpressionValidationResult.IsValid"/>
    /// and – on errors – <see cref="ExpressionValidationResult.Error"/> and
    /// <see cref="ExpressionValidationResult.ErrorPosition"/>.
    /// </returns>
    ExpressionValidationResult Validate(string expression, ExpressionContext context);
}
