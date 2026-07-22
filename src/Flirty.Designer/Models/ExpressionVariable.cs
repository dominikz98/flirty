namespace Flirty.Designer.Models;

/// <summary>
/// Art des Werts, den eine Ausdrucks-Variable im Ausdruckskontext liefert. Steuert im
/// Baustein-Einfüger des Branching-Editors (#40) die angebotenen Operatoren und die Quotierung des
/// eingegebenen Vergleichswerts.
/// </summary>
internal enum ExpressionValueKind
{
    /// <summary>Zeichenkette – Vergleichswerte werden quotiert (<c>role == "dev"</c>).</summary>
    Text = 0,

    /// <summary>Zahl – Vergleichswerte werden roh übernommen (<c>alter &gt; 18</c>).</summary>
    Number = 1,

    /// <summary>Wahrheitswert – <c>true</c>/<c>false</c> bzw. der Bezeichner allein.</summary>
    Boolean = 2,

    /// <summary>Liste (Mehrfachauswahl oder Loop-Collection) – <c>skills.Count &gt; 0</c>.</summary>
    List = 3,

    /// <summary>
    /// Reservierte Kontext-Variable (<c>now</c>, <c>session</c>). Wird nur im Nachschlagewerk gezeigt,
    /// nicht im Baustein-Einfüger – sinnvolle Ausdrücke greifen hier auf Member zu (<c>now.Year</c>).
    /// </summary>
    Context = 4,
}

/// <summary>
/// Ein im Ausdruckskontext verfügbarer Bezeichner samt Typ und Beispiel – die Datengrundlage für die
/// Referenztabelle und den Baustein-Einfüger des Branching-Editors (#40).
/// </summary>
/// <param name="Name">Der Bezeichner, wie er im Ausdruck steht (Frage-Schlüssel, <c>CollectionKey</c> oder reservierter Name).</param>
/// <param name="Kind">Die Art des Werts (steuert Operatoren und Quotierung).</param>
/// <param name="TypeLabel">Der deutsche Typname für die Anzeige (z. B. „Zahl").</param>
/// <param name="Example">Ein gültiger Beispiel-Ausdruck mit diesem Bezeichner.</param>
/// <param name="IsUsable">
/// Gibt an, ob der Bezeichner im Ausdruck referenzierbar ist. <see langword="false"/> z. B. bei
/// Schlüsseln, die keine gültigen Bezeichner sind, oder die von einer reservierten Variable verdeckt werden.
/// </param>
/// <param name="Note">
/// Erläuterung für die Referenztabelle – bei nicht nutzbaren Bezeichnern die Begründung, sonst ein
/// Hinweis zur Bedeutung (oder <see langword="null"/>).
/// </param>
internal sealed record ExpressionVariable(
    string Name,
    ExpressionValueKind Kind,
    string TypeLabel,
    string Example,
    bool IsUsable,
    string? Note);
