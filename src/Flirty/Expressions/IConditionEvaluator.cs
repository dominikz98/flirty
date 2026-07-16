namespace Flirty.Expressions;

/// <summary>
/// Austauschbare Ausdrucks-Engine, die booleschen Bedingungsausdrücke des Branchings auswertet –
/// insbesondere <c>Transition.ConditionExpression</c> (welcher Übergang greift) und
/// <c>TriggerDefinition.ConditionExpression</c> (ob ein Trigger auslöst). Die Auswertung erfolgt
/// gegen den <see cref="ExpressionContext"/> (Antworten nach Frage-Schlüssel, je Iteration
/// gesammelte Loop-Collections, Iterationsindex, Auswertungszeitpunkt, Session).
/// </summary>
/// <remarks>
/// Dieses Interface aus Issue #22 legt nur die Abstraktion fest. Die gesandboxte Default-Implementierung
/// <c>DynamicExpressoConditionEvaluator</c> (Member-Whitelist, kein roher C#-<c>eval</c>) folgt in
/// Issue #23, der Compile-/Validierungs-Pfad für den Designer in Issue #24. Registriert und
/// austauschbar wird die Engine über <c>o.UseConditionEvaluator&lt;T&gt;()</c> in Issue #34.
/// </remarks>
public interface IConditionEvaluator
{
    /// <summary>
    /// Wertet den booleschen Bedingungsausdruck <paramref name="expression"/> gegen den
    /// übergebenen <paramref name="context"/> aus.
    /// </summary>
    /// <param name="expression">
    /// Der auszuwertende Bedingungsausdruck. Implementierungen dürfen einen nicht-leeren Ausdruck
    /// erwarten; die Behandlung eines <see langword="null"/>en/leeren Ausdrucks als „bedingungslos
    /// zutreffend" obliegt der aufrufenden Runtime, nicht dem Evaluator.
    /// </param>
    /// <param name="context">Der Auswertungskontext mit Antworten, Loop-Collections, Iterationsindex, Zeitpunkt und Session.</param>
    /// <returns><see langword="true"/>, wenn der Ausdruck zutrifft, andernfalls <see langword="false"/>.</returns>
    bool Evaluate(string expression, ExpressionContext context);
}
