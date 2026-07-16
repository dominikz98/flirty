namespace Flirty.Expressions;

/// <summary>
/// Wird geworfen, wenn ein <see cref="IExpressionEvaluator"/> einen Bedingungsausdruck nicht
/// erfolgreich zu einem booleschen Ergebnis auswerten kann – etwa bei Syntaxfehlern, unbekannten
/// Bezeichnern, nicht auf der Member-Whitelist stehenden Typen/Membern oder einem nicht-booleschen
/// Ergebnis. Kapselt die engine-spezifische Ursache (z. B. DynamicExpresso) in
/// <see cref="System.Exception.InnerException"/>, damit die austauschbare Engine-Implementierung
/// nicht nach außen durchschlägt.
/// </summary>
public sealed class ExpressionEvaluationException : Exception
{
    /// <summary>
    /// Erstellt eine neue <see cref="ExpressionEvaluationException"/>.
    /// </summary>
    /// <param name="expression">Der Bedingungsausdruck, dessen Auswertung fehlgeschlagen ist.</param>
    /// <param name="message">Die Fehlerbeschreibung.</param>
    /// <param name="innerException">Die zugrunde liegende Ursache (z. B. die Engine-Exception) oder <see langword="null"/>.</param>
    public ExpressionEvaluationException(string expression, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Expression = expression;
    }

    /// <summary>Der Bedingungsausdruck, dessen Auswertung fehlgeschlagen ist.</summary>
    public string Expression { get; }
}
