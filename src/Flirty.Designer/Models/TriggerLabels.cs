using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>
/// Deutsche Anzeigetexte für <see cref="TriggerScope"/> und <see cref="TriggerKind"/>. Zentral, damit
/// Trigger-Liste (<c>DialogEditor</c>) und Trigger-Editor (<c>TriggerEditor</c>) dieselben
/// Bezeichnungen verwenden (Muster: <see cref="QuestionTypeLabels"/>).
/// </summary>
internal static class TriggerLabels
{
    /// <summary>Liefert den Anzeigetext des Auslöse-Zeitpunkts.</summary>
    /// <param name="scope">Der Zeitpunkt im Dialogablauf.</param>
    /// <returns>Der deutsche Anzeigetext (inklusive des technischen Namens zur Wiedererkennung).</returns>
    public static string Describe(TriggerScope scope) => scope switch
    {
        TriggerScope.OnDialogStarted => "Beim Dialogstart (OnDialogStarted)",
        TriggerScope.AfterAnswer => "Nach jeder Antwort (AfterAnswer)",
        TriggerScope.AfterQuestion => "Nach einer bestimmten Frage (AfterQuestion)",
        TriggerScope.OnDialogCompleted => "Beim Abschluss (OnDialogCompleted)",
        _ => scope.ToString(),
    };

    /// <summary>Liefert den Anzeigetext des Kanals.</summary>
    /// <param name="kind">Der Kanal, über den benachrichtigt wird.</param>
    /// <returns>Der deutsche Anzeigetext (inklusive des technischen Namens zur Wiedererkennung).</returns>
    public static string Describe(TriggerKind kind) => kind switch
    {
        TriggerKind.Webhook => "Webhook (HTTP POST)",
        TriggerKind.InProcess => "In-Process (Handler der Host-App)",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Gibt an, ob der Zeitpunkt einen Frage-Verweis braucht. Nur
    /// <see cref="TriggerScope.AfterQuestion"/> bezieht sich auf eine einzelne Frage – die Admin-Commands
    /// weisen abweichende Kombinationen zurück.
    /// </summary>
    /// <param name="scope">Der Zeitpunkt im Dialogablauf.</param>
    /// <returns><see langword="true"/> bei <see cref="TriggerScope.AfterQuestion"/>.</returns>
    public static bool RequiresQuestion(TriggerScope scope) => scope == TriggerScope.AfterQuestion;

    /// <summary>
    /// Gibt an, ob zum Zeitpunkt der Auswertung bereits Antworten im Ausdruckskontext gebunden sind.
    /// Beim Dialogstart ist das <b>nicht</b> der Fall: eine Bedingung auf einen Fragen-Schlüssel
    /// scheitert dort zur Laufzeit (der Trigger feuert dann nicht, der Fehler wird nur protokolliert).
    /// </summary>
    /// <param name="scope">Der Zeitpunkt im Dialogablauf.</param>
    /// <returns><see langword="false"/> bei <see cref="TriggerScope.OnDialogStarted"/>.</returns>
    public static bool HasAnswers(TriggerScope scope) => scope != TriggerScope.OnDialogStarted;
}
