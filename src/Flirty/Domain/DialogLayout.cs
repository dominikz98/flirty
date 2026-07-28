namespace Flirty.Domain;

/// <summary>
/// Die vom Autor gewählte Position eines Elements auf dem Graph-Canvas des Designers. Reine
/// Anzeigedaten: Ohne Zeile ordnet das Auto-Layout an, und die Laufzeit liest die Tabelle nie.
/// </summary>
/// <remarks>
/// <para>
/// Bewusst eine eigene Entity statt zweier Spalten an <see cref="Question"/> (ADR 0007). Das hält die
/// Graph-Entities frei von Anzeigebelangen und trägt später auch Positionen für Elemente, die keine
/// Frage sind (siehe <see cref="LayoutElementKind"/>).
/// </para>
/// <para>
/// Der Gegenwert ist mehr als Erweiterbarkeit: Weil die Tabelle nicht zum Graphen gehört, fällt das
/// Schreiben von Koordinaten nicht unter die Publish-Sperre (<c>DialogEditGuard</c>) – ein
/// veröffentlichter Dialog lässt sich also übersichtlich anordnen, ohne dass die Sperre aufweicht.
/// Sessions pinnen <c>DialogId</c>/<c>DialogVersion</c> und folgen Guids, nicht Pixeln.
/// </para>
/// </remarks>
public sealed class DialogLayout
{
    /// <summary>Eindeutiger Primärschlüssel der Layout-Zeile.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf den zugehörigen <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>Die Art des Elements, dessen Position hier festgehalten ist.</summary>
    public LayoutElementKind ElementKind { get; set; }

    /// <summary>
    /// Verweis auf das Element – bei <see cref="LayoutElementKind.Question"/> eine
    /// <see cref="Question.Id"/>. Bewusst ohne Fremdschlüssel, wie die Frage-Verweise in
    /// <see cref="LoopDefinition"/>; verwaiste Zeilen räumt <c>DeleteQuestionCommand</c> ab.
    /// </summary>
    public Guid ElementId { get; set; }

    /// <summary>Die waagerechte Canvas-Koordinate der linken oberen Ecke in px (nie negativ).</summary>
    public int X { get; set; }

    /// <summary>Die senkrechte Canvas-Koordinate der linken oberen Ecke in px (nie negativ).</summary>
    public int Y { get; set; }

    /// <summary>Der Dialog, zu dem diese Layout-Zeile gehört.</summary>
    public Dialog Dialog { get; set; } = null!;
}
