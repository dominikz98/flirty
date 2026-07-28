using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für die Rechenregeln der Graph-Bearbeitung (#103): nächste <c>Order</c>, nächste
/// <c>Priority</c> und die Umsortierung der Auswertungsreihenfolge.
/// </summary>
/// <remarks>
/// Diese Regeln lagen bis #103 privat im <c>@code</c>-Block von <c>DialogEditor.razor</c> und waren
/// damit unprüfbar – der Designer hat kein bUnit, es wird keine Razor-Komponente gerendert. Sie
/// herauszuziehen war die Voraussetzung dafür, dass Listenansicht und Canvas nachweisbar dieselbe
/// Reihenfolge berechnen.
/// </remarks>
public sealed class GraphEditingTests
{
    private static readonly Guid DialogId = Guid.NewGuid();

    [Fact]
    public void NextOrder_beginnt_bei_null_wenn_der_Dialog_leer_ist()
        => Assert.Equal(0, GraphEditing.NextOrder(Dialog()));

    [Fact]
    public void NextOrder_haengt_an_der_groessten_Zahl_nicht_an_der_Anzahl()
    {
        // Lückenhafte Order-Werte entstehen durch Löschen. Wer die Anzahl nimmt statt des Maximums,
        // vergibt eine bereits belegte Zahl.
        var detail = Dialog(Question("a", 0), Question("b", 7));

        Assert.Equal(8, GraphEditing.NextOrder(detail));
    }

    [Fact]
    public void NextPriority_zaehlt_je_Ausgangsfrage_nicht_dialogweit()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var detail = Dialog(
            questions: [Question("a", 0, first), Question("b", 1, second)],
            transitions:
            [
                Transition(first, second, priority: 0),
                Transition(first, second, priority: 1),
                Transition(second, first, priority: 0),
            ]);

        // Die zweite Frage hat nur einen Übergang – dialogweit gezählt käme hier 2 heraus, und die
        // Positionsanzeige zeigte eine Lücke, die niemand erklären kann.
        Assert.Equal(2, GraphEditing.NextPriority(detail, first));
        Assert.Equal(1, GraphEditing.NextPriority(detail, second));
    }

    [Fact]
    public void NextPriority_beginnt_bei_null_ohne_ausgehende_Uebergaenge()
        => Assert.Equal(0, GraphEditing.NextPriority(Dialog(Question("a", 0)), Guid.NewGuid()));

    [Fact]
    public void Reorder_schreibt_den_Positionsindex_als_Prioritaet()
    {
        var from = Guid.NewGuid();
        var target = Guid.NewGuid();
        var ordered = new[]
        {
            Transition(from, target, priority: 0),
            Transition(from, target, priority: 1),
            Transition(from, target, priority: 2),
        };

        var changed = GraphEditing.Reorder(ordered, 2, 0);

        // Getauscht werden Position 0 und 2; Position 1 bleibt, wo sie ist, und darf deshalb nicht
        // geschrieben werden.
        Assert.Equal(2, changed.Count);
        Assert.Equal(ordered[2].Id, changed[0].Transition.Id);
        Assert.Equal(0, changed[0].Priority);
        Assert.Equal(ordered[0].Id, changed[1].Transition.Id);
        Assert.Equal(2, changed[1].Priority);
    }

    [Fact]
    public void Reorder_repariert_doppelte_Prioritaeten()
    {
        var from = Guid.NewGuid();
        var target = Guid.NewGuid();

        // Zwei gleiche Prioritäten: Ein reiner Zahlentausch bliebe wirkungslos – die Reihenfolge sähe
        // unverändert aus, obwohl der Anwender sie bewegt hat. Genau deshalb wird der Index geschrieben.
        var ordered = new[]
        {
            Transition(from, target, priority: 5),
            Transition(from, target, priority: 5),
        };

        var changed = GraphEditing.Reorder(ordered, 0, 1);

        Assert.Equal(2, changed.Count);
        Assert.Equal([0, 1], changed.Select(entry => entry.Priority));
    }

    [Fact]
    public void Reorder_meldet_nichts_wenn_sich_nichts_aendert()
    {
        var from = Guid.NewGuid();
        var target = Guid.NewGuid();
        var ordered = new[] { Transition(from, target, priority: 0), Transition(from, target, priority: 1) };

        Assert.Empty(GraphEditing.Reorder(ordered, 1, 1));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    public void Reorder_ignoriert_Positionen_ausserhalb_der_Liste(int from, int to)
    {
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var ordered = new[] { Transition(source, target, priority: 0), Transition(source, target, priority: 1) };

        Assert.Empty(GraphEditing.Reorder(ordered, from, to));
    }

    private static DialogDetail Dialog(params QuestionDetail[] questions)
        => Dialog(questions, []);

    private static DialogDetail Dialog(
        IReadOnlyList<QuestionDetail> questions,
        IReadOnlyList<TransitionDetail> transitions)
        => new(
            new DialogSummary(
                DialogId, "dialog", "Dialog", null, 1, false, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            questions,
            transitions,
            [],
            [],
            []);

    private static QuestionDetail Question(string key, int order, Guid? id = null)
        => new(id ?? Guid.NewGuid(), DialogId, key, $"Frage {key}", QuestionType.FreeText, order, false, null, []);

    private static TransitionDetail Transition(Guid from, Guid target, int priority)
        => new(Guid.NewGuid(), DialogId, from, target, null, priority, false);
}
