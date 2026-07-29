namespace Flirty.Expressions;

/// <summary>
/// Thrown when an <see cref="IExpressionEvaluator"/> cannot successfully evaluate a condition
/// expression to a boolean result – for example on syntax errors, unknown
/// identifiers, types/members not on the member whitelist, or a non-boolean
/// result. Wraps the engine-specific cause (e.g. DynamicExpresso) in
/// <see cref="System.Exception.InnerException"/>, so that the interchangeable engine implementation
/// does not leak outward.
/// </summary>
public sealed class ExpressionEvaluationException : Exception
{
    /// <summary>
    /// Creates a new <see cref="ExpressionEvaluationException"/>.
    /// </summary>
    /// <param name="expression">The condition expression whose evaluation failed.</param>
    /// <param name="message">The error description.</param>
    /// <param name="innerException">The underlying cause (e.g. the engine exception) or <see langword="null"/>.</param>
    public ExpressionEvaluationException(string expression, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Expression = expression;
    }

    /// <summary>The condition expression whose evaluation failed.</summary>
    public string Expression { get; }
}
