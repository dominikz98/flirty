namespace Flirty.Domain;

/// <summary>
/// Legt fest, auf welche Art von Element sich eine <see cref="DialogLayout"/>-Zeile bezieht.
/// </summary>
/// <remarks>
/// Zunächst gibt es nur <see cref="Question"/> – genau dieser Aufzählungstyp ist aber der Grund, warum
/// das Layout in einer eigenen Tabelle liegt statt als zwei Spalten an der Frage: Kanten-Wegpunkte,
/// Notizknoten oder ein Viewport kommen später ohne Schema-Umbau dazu (ADR 0007).
/// </remarks>
public enum LayoutElementKind
{
    /// <summary>Die Position einer Frage (<see cref="Domain.Question.Id"/>) auf dem Canvas.</summary>
    Question = 0,
}
