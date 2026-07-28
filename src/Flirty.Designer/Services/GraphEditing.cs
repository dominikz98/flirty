using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Die neue Priorität eines Übergangs nach einer Umsortierung.
/// </summary>
/// <param name="Transition">Der betroffene Übergang (unverändert – der Update-Command braucht seine Felder).</param>
/// <param name="Priority">Die zu schreibende <c>Priority</c>.</param>
internal sealed record TransitionPriority(TransitionDetail Transition, int Priority);

/// <summary>
/// Die Rechenregeln, nach denen der Designer den Graphen ändert: nächste <c>Order</c>, nächste
/// <c>Priority</c> und die Umsortierung der Auswertungsreihenfolge.
/// </summary>
/// <remarks>
/// <para>
/// Herausgezogen aus dem <c>@code</c>-Block von <c>DialogEditor.razor</c>, damit Listenansicht und
/// Canvas-Gesten (#103) <b>dieselben</b> Regeln anwenden. Zwei Prioritäts-Algorithmen wären genau die
/// stille Abweichung, die niemand bemerkt, bis zwei Ansichten verschiedene Reihenfolgen behaupten – und
/// das Akzeptanzkriterium „alles erscheint unmittelbar auch in der Listenansicht" macht sie sichtbar.
/// </para>
/// <para>
/// Die eigene Datei hat noch einen Grund: <c>tests/Flirty.Tests/Designer</c> rendert keine
/// Razor-Komponenten (kein bUnit). Was im <c>@code</c>-Block liegt, ist nicht prüfbar; eine reine
/// Funktion über <see cref="DialogDetail"/> ist es unmittelbar.
/// </para>
/// </remarks>
internal static class GraphEditing
{
    /// <summary>Die <c>Order</c> für eine neu am Ende angefügte Frage.</summary>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <returns>Die nächste freie Sortierzahl.</returns>
    public static int NextOrder(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return detail.Questions.Count == 0 ? 0 : detail.Questions.Max(question => question.Order) + 1;
    }

    /// <summary>
    /// Die <c>Priority</c> für einen neuen Übergang – zuletzt ausgewertet innerhalb seiner Ausgangsfrage.
    /// </summary>
    /// <remarks>
    /// Bewusst je Ausgangsfrage und nicht dialogweit: Der <c>TransitionResolver</c> vergleicht nur die
    /// Übergänge <b>einer</b> Frage. Eine dialogweit fortlaufende Zahl wäre nicht falsch, aber die
    /// Positionsanzeige („Position 3") würde Löcher zeigen, die niemand erklären kann.
    /// </remarks>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <param name="fromQuestionId">Die Ausgangsfrage.</param>
    /// <returns>Die nächste freie Priorität.</returns>
    public static int NextPriority(DialogDetail detail, Guid fromQuestionId)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var siblings = detail.Transitions
            .Where(transition => transition.FromQuestionId == fromQuestionId)
            .ToList();

        return siblings.Count == 0 ? 0 : siblings.Max(transition => transition.Priority) + 1;
    }

    /// <summary>
    /// Vertauscht zwei Positionen der Auswertungsreihenfolge und schreibt anschließend den
    /// <b>Positionsindex</b> als neue Priorität.
    /// </summary>
    /// <remarks>
    /// Der Positionsindex statt eines Tauschs der beiden Zahlen: Nur so werden doppelte oder
    /// lückenhafte <c>Priority</c>-Werte mitrepariert. Bei zwei Übergängen mit identischer Priorität
    /// bliebe ein reiner Zahlentausch wirkungslos – die Reihenfolge sähe unverändert aus, obwohl der
    /// Anwender sie bewegt hat.
    /// </remarks>
    /// <param name="ordered">Die ausgehenden Übergänge einer Frage in Auswertungsreihenfolge.</param>
    /// <param name="from">Die aktuelle Position.</param>
    /// <param name="to">Die Zielposition.</param>
    /// <returns>
    /// Nur die Übergänge, deren Priorität sich tatsächlich ändert – leer, wenn nichts zu schreiben ist
    /// (Position außerhalb der Liste, unveränderte Position, Prioritäten schon deckungsgleich).
    /// </returns>
    public static IReadOnlyList<TransitionPriority> Reorder(
        IReadOnlyList<TransitionDetail> ordered,
        int from,
        int to)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        if (from < 0 || from >= ordered.Count || to < 0 || to >= ordered.Count || from == to)
        {
            return [];
        }

        var moved = ordered.ToList();
        (moved[from], moved[to]) = (moved[to], moved[from]);

        return
        [
            .. moved
                .Select((transition, index) => new TransitionPriority(transition, index))
                .Where(entry => entry.Transition.Priority != entry.Priority),
        ];
    }
}
