using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für den <see cref="TransitionWarningAnalyzer"/> – die Übergangs-Warnungen, die bis #101 privat
/// im <c>DialogEditor</c> lagen und seither auch die Graph-Ansicht speisen. Zwei Dinge werden hier
/// festgenagelt: die <b>Wortlaute</b> (Listenansicht und E2E-Suite hängen daran) und die <b>Verortung</b>
/// am Knoten bzw. an der Kante (ohne sie kann der Canvas die Befunde nicht am betroffenen Element zeigen).
/// </summary>
public sealed class TransitionWarningAnalyzerTests
{
    private static readonly Guid DialogId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FromId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TargetId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherTargetId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Ohne Default und ohne bedingungslosen Übergang kann zur Laufzeit gar nichts greifen. Die Warnung
    /// hängt an der <b>Frage</b> – kein einzelner Übergang ist schuld.
    /// </summary>
    [Fact]
    public void Analyze_meldet_fehlenden_Default_an_der_Ausgangsfrage()
    {
        IReadOnlyList<TransitionDetail> outgoing = [Transition(0, expression: "a == 1")];

        var warning = Assert.Single(TransitionWarningAnalyzer.Analyze(outgoing));

        Assert.Equal(GraphElementKind.Question, warning.Kind);
        Assert.Equal(FromId, warning.ElementId);
        Assert.Equal(FromId, warning.QuestionId);
        Assert.Contains("Kein Default-Übergang", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>Mehrere Defaults sind eine Eigenschaft der Gruppe, also der Frage.</summary>
    [Fact]
    public void Analyze_meldet_mehrere_Defaults_an_der_Ausgangsfrage()
    {
        IReadOnlyList<TransitionDetail> outgoing =
        [
            Transition(0, isDefault: true),
            Transition(1, isDefault: true, target: OtherTargetId),
        ];

        var warning = Assert.Single(TransitionWarningAnalyzer.Analyze(outgoing));

        Assert.Equal(GraphElementKind.Question, warning.Kind);
        Assert.Equal(FromId, warning.ElementId);
        Assert.Contains("Mehrere Default-Übergänge", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Die ignorierte Bedingung ist die Eigenschaft <b>eines</b> Übergangs – die Warnung muss auf dessen
    /// Kante zeigen, nicht auf die Frage. Genau das braucht der Canvas.
    /// </summary>
    [Fact]
    public void Analyze_meldet_die_ignorierte_Bedingung_am_betroffenen_Default_Uebergang()
    {
        var decorated = Transition(1, expression: "a == 1", isDefault: true, target: OtherTargetId);
        IReadOnlyList<TransitionDetail> outgoing = [Transition(0, expression: "b == 2"), decorated];

        var warning = Assert.Single(
            TransitionWarningAnalyzer.Analyze(outgoing),
            candidate => candidate.Kind == GraphElementKind.Transition);

        Assert.Equal(decorated.Id, warning.ElementId);
        Assert.Equal(FromId, warning.QuestionId);
        Assert.Contains("nicht ausgewertet", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ein bedingungsloser Nicht-Default greift immer und verdeckt alles Nachfolgende. Auch das ist eine
    /// Kanten-Aussage – die Position im Text ist 1-basiert wie in der Listenansicht.
    /// </summary>
    [Fact]
    public void Analyze_meldet_den_verdeckenden_bedingungslosen_Uebergang_an_der_Kante()
    {
        var blocking = Transition(0);
        IReadOnlyList<TransitionDetail> outgoing = [blocking, Transition(1, isDefault: true, target: OtherTargetId)];

        var warning = Assert.Single(
            TransitionWarningAnalyzer.Analyze(outgoing),
            candidate => candidate.Kind == GraphElementKind.Transition);

        Assert.Equal(blocking.Id, warning.ElementId);
        Assert.Equal(
            "Der bedingungslose Übergang an Position 1 greift immer – die nachfolgenden Übergänge "
            + "werden nie geprüft.",
            warning.Text);
    }

    /// <summary>
    /// Der wichtigste Test des Umbaus aus #101: Die vier deutschen Volltexte sind Vertrag. Der
    /// <c>DialogEditor</c> zeigt sie unverändert an, die E2E-Suite und die Publish-Rückfrage hängen
    /// daran. Wer hier etwas umformuliert, ändert die Oberfläche – und muss es bewusst tun.
    /// </summary>
    [Fact]
    public void Analyze_liefert_die_bisherigen_Wortlaute_unveraendert()
    {
        IReadOnlyList<TransitionDetail> outgoing =
        [
            Transition(0),
            Transition(1, expression: "a == 1", isDefault: true, target: OtherTargetId),
            Transition(2, isDefault: true, target: TargetId),
        ];

        var texts = TransitionWarningAnalyzer.Analyze(outgoing).Select(warning => warning.Text);

        Assert.Equal(
            [
                "Mehrere Default-Übergänge – es greift nur der oberste.",
                "Die Bedingung eines Default-Übergangs wird zur Laufzeit nicht ausgewertet.",
                "Der bedingungslose Übergang an Position 1 greift immer – die nachfolgenden Übergänge "
                + "werden nie geprüft.",
            ],
            texts);
    }

    /// <summary>Der stimmige Verzweigungs-Dialog der Engine erzeugt keine Warnung.</summary>
    [Fact]
    public void Analyze_meldet_nichts_fuer_einen_stimmigen_Graphen()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _));

        Assert.Empty(TransitionWarningAnalyzer.Analyze(detail));
    }

    /// <summary>
    /// Über den ganzen Graphen läuft die Prüfung in Fragen-Reihenfolge; Fragen ohne ausgehende Übergänge
    /// enden regulär und sind kein Befund. Beides muss so bleiben, weil der <c>DialogEditor</c> die
    /// Reihenfolge unverändert als Liste zeigt.
    /// </summary>
    [Fact]
    public void Analyze_ueber_den_Dialog_folgt_der_Fragenreihenfolge_und_ueberspringt_Endfragen()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);

        // Der Default fällt weg -> die Startfrage hat nur noch eine bedingte Kante.
        dialog.Transitions.Remove(dialog.Transitions.First(transition => transition.IsDefault));

        // Und die bislang terminale Frage bekommt ebenfalls eine unvollständige Gruppe.
        dialog.Transitions.Add(new Transition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            FromQuestionId = ids.DevQuestionId,
            TargetQuestionId = ids.PmQuestionId,
            Expression = "devDetail == \"csharp\"",
            Priority = 0,
        });

        var warnings = TransitionWarningAnalyzer.Analyze(AdminProjection.ToDetail(dialog));

        Assert.Equal([ids.RoleQuestionId, ids.DevQuestionId], warnings.Select(warning => warning.QuestionId));
        Assert.All(warnings, warning => Assert.Contains("Kein Default-Übergang", warning.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Übergänge mit unbekannter Ausgangsfrage werden nie ausgewertet und haben keinen Knoten, an dem
    /// eine Warnung hängen könnte – sie bleiben hier außen vor und werden getrennt ausgewiesen.
    /// </summary>
    [Fact]
    public void Analyze_ueber_den_Dialog_ignoriert_verwaiste_Uebergaenge()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.Transitions.Add(new Transition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            FromQuestionId = Guid.NewGuid(),
            TargetQuestionId = dialog.StartQuestionId!.Value,
            Priority = 0,
        });

        Assert.Empty(TransitionWarningAnalyzer.Analyze(AdminProjection.ToDetail(dialog)));
    }

    /// <summary>Die ausgehenden Übergänge kommen in Auswertungsreihenfolge, nicht in Speicherreihenfolge.</summary>
    [Fact]
    public void Outgoing_sortiert_nach_Prioritaet()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        foreach (var transition in dialog.Transitions)
        {
            transition.Priority = transition.IsDefault ? 0 : 1;
        }

        var outgoing = TransitionWarningAnalyzer.Outgoing(AdminProjection.ToDetail(dialog), ids.RoleQuestionId);

        Assert.Equal([true, false], outgoing.Select(transition => transition.IsDefault));
    }

    private static TransitionDetail Transition(
        int priority, string? expression = null, bool isDefault = false, Guid? target = null)
        => new(
            Guid.Parse($"55555555-5555-5555-5555-{priority:D12}"),
            DialogId,
            FromId,
            target ?? TargetId,
            expression,
            priority,
            isDefault);
}
