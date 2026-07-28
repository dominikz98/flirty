using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für den <see cref="LoopAnalyzer"/> des Loop-Editors (#41): die Ermittlung des Schleifenbereichs,
/// die Einteilung der Übergänge in Rücksprünge und Ausstiege sowie die Warnungen – allen voran der Zyklus
/// ohne erreichbaren Exit (Endlosschleife). Kernprobe ist der Abgleich mit dem Core-<see cref="LoopResolver"/>:
/// Der Designer rechnet den Bereich nach, weil der Resolver nicht wiederverwendbar ist – auseinanderlaufen
/// dürfen die beiden trotzdem nicht.
/// </summary>
public sealed class LoopAnalyzerTests
{
    /// <summary>
    /// Der Analyzer spiegelt <c>LoopResolver.ComputeBody</c>. Da der Bereich dort privat ist, wird er
    /// indirekt abgefragt: <see cref="LoopResolver.ResolveAssignment"/> vergibt genau für Fragen im
    /// Schleifenbereich eine Instanz-Id. Beide laufen auf demselben Graphen – der Designer-Graph entsteht
    /// per <c>AdminProjection</c> aus der Entity, damit sich keine Abweichung in den Testdaten versteckt.
    /// </summary>
    [Theory]
    [InlineData("more == \"yes\"")]
    [InlineData("positions.Count < 2")]
    public void ComputeBody_stimmt_mit_dem_LoopResolver_der_Engine_ueberein(string loopBackExpression)
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _, loopBackExpression);
        var detail = AdminProjection.ToDetail(dialog);
        var resolver = new LoopResolver(dialog);
        var session = NewSession(dialog);

        var vomResolver = dialog.Questions
            .Where(question => resolver.ResolveAssignment(session, question.Id).LoopInstanceId is not null)
            .Select(question => question.Id)
            .ToHashSet();

        var vomAnalyzer = LoopAnalyzer.ComputeBody(detail, detail.Loops[0]);

        Assert.Equal(vomResolver, vomAnalyzer);
    }

    /// <summary>
    /// Der Schleifenbereich umfasst Einstieg und Breaking Question, nicht aber die nachgelagerte Frage –
    /// deren Antworten tragen zur Laufzeit keinen Iterationsindex.
    /// </summary>
    [Fact]
    public void Analyze_ermittelt_Bereich_Ruecksprung_und_Ausstieg()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Equal(
            [ids.PositionQuestionId, ids.MoreQuestionId],
            insight.Body.Select(question => question.Id));
        Assert.Equal(ids.PositionQuestionId, insight.EntryQuestion!.Id);
        Assert.Equal(ids.MoreQuestionId, insight.BreakingQuestion!.Id);
        Assert.Equal(ids.PositionQuestionId, Assert.Single(insight.LoopBackTransitions).TargetQuestionId);
        Assert.Equal(ids.SummaryQuestionId, Assert.Single(insight.ExitTransitions).TargetQuestionId);
        Assert.Empty(insight.Warnings);
    }

    /// <summary>Ein Ein-Fragen-Loop (<c>Entry == Breaking</c>) ist zulässig und ergibt genau diese Frage.</summary>
    [Fact]
    public void Analyze_Ein_Fragen_Loop_ergibt_nur_die_Einstiegsfrage()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Loops.First().EntryQuestionId = ids.MoreQuestionId;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Equal([ids.MoreQuestionId], insight.Body.Select(question => question.Id));
    }

    [Fact]
    public void Analyze_warnt_wenn_der_Ruecksprung_fehlt()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.PositionQuestionId
                                                && transition.FromQuestionId == ids.MoreQuestionId));
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Contains(insight.Warnings, warning => warning.Contains("kein Zyklus", StringComparison.Ordinal));
    }

    /// <summary>Ohne Übergang aus dem Bereich heraus lässt sich die Schleife nie verlassen.</summary>
    [Fact]
    public void Analyze_warnt_bei_fehlendem_Ausstieg()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId));
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Empty(insight.ExitTransitions);
        Assert.Contains(insight.Warnings, warning => warning.Contains("Endlosschleife", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ein bedingungsloser Rücksprung vor dem Ausstieg greift zur Laufzeit immer – der Ausstieg wird
    /// nie geprüft. Genau die Regel des <c>TransitionResolver</c>: erster zutreffender Nicht-Default gewinnt.
    /// </summary>
    [Fact]
    public void Analyze_warnt_wenn_ein_bedingungsloser_Ruecksprung_den_Ausstieg_verdeckt()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions
            .First(transition => transition.FromQuestionId == ids.MoreQuestionId
                              && transition.TargetQuestionId == ids.PositionQuestionId)
            .Expression = null;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.NotEmpty(insight.ExitTransitions);
        Assert.Contains(insight.Warnings, warning => warning.Contains("nie geprüft", StringComparison.Ordinal));
    }

    /// <summary>
    /// Steht der Ausstieg vor dem bedingungslosen Rücksprung, greift er – dieselbe Konfiguration darf
    /// dann keine Warnung mehr erzeugen.
    /// </summary>
    [Fact]
    public void Analyze_akzeptiert_einen_Ausstieg_vor_dem_bedingungslosen_Ruecksprung()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(transition => transition.FromQuestionId == ids.MoreQuestionId
                                                          && transition.TargetQuestionId == ids.PositionQuestionId);
        var exit = dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId);
        loopBack.Expression = null;
        loopBack.Priority = 1;
        exit.Expression = "more == \"no\"";
        exit.IsDefault = false;
        exit.Priority = 0;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Empty(insight.Warnings);
    }

    /// <summary>
    /// Überlappende Schleifenbereiche lässt der <see cref="LoopResolver"/> schon im Konstruktor scheitern –
    /// jede Session gegen den Dialog bricht dann ab. Der Analyzer muss das vorher sichtbar machen.
    /// </summary>
    [Fact]
    public void Analyze_warnt_bei_ueberlappenden_Schleifen()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Loops.Add(new LoopDefinition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            CollectionKey = "zweite",
            EntryQuestionId = ids.PositionQuestionId,
            BreakingQuestionId = ids.MoreQuestionId,
        });
        var detail = AdminProjection.ToDetail(dialog);

        Assert.Throws<InvalidOperationException>(() => new LoopResolver(dialog));
        Assert.All(
            LoopAnalyzer.Analyze(detail),
            insight => Assert.Contains(
                insight.Warnings, warning => warning.Contains("überschneidet", StringComparison.Ordinal)));
    }

    [Fact]
    public void Analyze_warnt_wenn_der_Collection_Schluessel_eine_Frage_verdeckt()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().CollectionKey = "summary";
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Contains(insight.Warnings, warning => warning.Contains("verdeckt", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ein Schlüssel, der kein Bezeichner ist (oder von <c>now</c>/<c>iterationIndex</c>/<c>session</c>
    /// verdeckt wird), lässt sich in keiner Bedingung referenzieren – die Schleife wäre unbrauchbar.
    /// </summary>
    [Theory]
    [InlineData("meine-positionen")]
    [InlineData("iterationIndex")]
    public void Analyze_warnt_bei_nicht_referenzierbarem_Collection_Schluessel(string collectionKey)
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().CollectionKey = collectionKey;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Contains(
            insight.Warnings, warning => warning.Contains("nicht referenzierbar", StringComparison.Ordinal));
    }

    /// <summary>Zeigt der Marker auf eine gelöschte Frage, bleibt der Bereich leer und wird gemeldet.</summary>
    [Fact]
    public void Analyze_warnt_bei_Marker_auf_unbekannte_Frage()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().EntryQuestionId = Guid.NewGuid();
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Empty(insight.Body);
        Assert.Null(insight.EntryQuestion);
        Assert.Contains(insight.Warnings, warning => warning.Contains("Einstiegsfrage", StringComparison.Ordinal));
    }

    /// <summary>
    /// Seit #101 trägt jede Warnung eine Ortsangabe, damit die Graph-Ansicht sie am betroffenen Element
    /// zeigen kann. Eine Warnung ohne Ziel wäre auf dem Canvas unsichtbar – deshalb muss jede eine haben,
    /// und der Bezug muss auf ein Element <b>dieses</b> Dialogs zeigen.
    /// </summary>
    [Fact]
    public void Analyze_verortet_jede_Warnung_an_einem_Element()
    {
        // Ein Marker ohne Rücksprung und ohne Ausstieg: erzeugt Warnungen an Loop und Breaking Question.
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Clear();
        dialog.Loops.First().CollectionKey = "more";
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.NotEmpty(insight.TargetedWarnings);
        Assert.All(insight.TargetedWarnings, warning =>
        {
            Assert.NotEqual(GraphElementKind.Dialog, warning.Kind);
            Assert.NotNull(warning.ElementId);
        });
        Assert.Contains(
            insight.TargetedWarnings,
            warning => warning.Kind == GraphElementKind.Question && warning.ElementId == ids.MoreQuestionId);
        Assert.Contains(
            insight.TargetedWarnings,
            warning => warning.Kind == GraphElementKind.Loop && warning.ElementId == detail.Loops[0].Id);
    }

    /// <summary>
    /// Der verdeckte Ausstieg hat einen konkreten Verursacher – den Rücksprung, der immer vorher greift.
    /// Die Warnung hängt an <b>seiner</b> Kante, sonst kann der Canvas nicht zeigen, welche Verbindung
    /// zu ändern ist.
    /// </summary>
    [Fact]
    public void Analyze_verortet_den_verdeckten_Ausstieg_am_verdeckenden_Ruecksprung()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(
            transition => transition.TargetQuestionId == ids.PositionQuestionId);
        loopBack.Expression = null;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        var warning = Assert.Single(
            insight.TargetedWarnings,
            candidate => candidate.Text.Contains("nie geprüft", StringComparison.Ordinal));
        Assert.Equal(GraphElementKind.Transition, warning.Kind);
        Assert.Equal(loopBack.Id, warning.ElementId);
        Assert.Equal(ids.MoreQuestionId, warning.QuestionId);
    }

    /// <summary>
    /// <c>Warnings</c> ist seit #101 eine berechnete Sicht auf <c>TargetedWarnings</c>. Loop- und
    /// Dialog-Editor lesen ausschließlich diese Sicht – Wortlaut und Reihenfolge müssen deckungsgleich
    /// bleiben.
    /// </summary>
    [Fact]
    public void Analyze_liefert_Warnings_in_unveraenderter_Reihenfolge_und_Wortlaut()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Transitions.Clear();
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Equal(insight.TargetedWarnings.Select(warning => warning.Text), insight.Warnings);
    }

    // ---- Rücksprung-Erkennung (aus dem DialogEditor gezogen, #103) ----------------------------------

    /// <summary>
    /// Ein Rücksprung zeigt auf eine frühere Frage <b>der Listenreihenfolge</b> – bewusst nicht auf eine
    /// höhere Schicht des Layouts, damit Listenansicht und Graph-Kante dasselbe behaupten.
    /// </summary>
    [Fact]
    public void IsBackJump_erkennt_nur_Rueckwaertskanten()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var detail = AdminProjection.ToDetail(dialog);

        var rueckwaerts = detail.Transitions.Single(
            transition => transition.FromQuestionId == ids.MoreQuestionId
                && transition.TargetQuestionId == ids.PositionQuestionId);
        var vorwaerts = detail.Transitions.Single(
            transition => transition.FromQuestionId == ids.PositionQuestionId
                && transition.TargetQuestionId == ids.MoreQuestionId);

        Assert.True(LoopAnalyzer.IsBackJump(detail, rueckwaerts));
        Assert.False(LoopAnalyzer.IsBackJump(detail, vorwaerts));
    }

    /// <summary>Ein Verweis auf sich selbst ist ein Zyklus – <c>target &lt;= from</c> schließt ihn ein.</summary>
    [Fact]
    public void IsBackJump_zaehlt_den_Selbstbezug()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var detail = AdminProjection.ToDetail(dialog);
        var selbst = new TransitionDetail(
            Guid.NewGuid(), dialog.Id, ids.MoreQuestionId, ids.MoreQuestionId, null, 9, false);

        Assert.True(LoopAnalyzer.IsBackJump(detail, selbst));
    }

    /// <summary>
    /// Der passende Marker macht einen Rücksprung „markiert". Genau diese Liste speist die Vorschläge –
    /// in der Listenansicht (#41) wie am Zyklus auf dem Canvas (#103).
    /// </summary>
    [Fact]
    public void UnmarkedBackJumps_schliesst_markierte_Ruecksprünge_aus()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);

        Assert.Empty(LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog)));

        // Ohne Marker bleibt derselbe Zyklus übrig – die Antworten würden zur Laufzeit überschrieben
        // statt gesammelt, und genau darauf weist der Vorschlag hin.
        dialog.Loops.Clear();
        var ohneMarker = LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog));

        var rueckwaerts = Assert.Single(ohneMarker);
        Assert.True(LoopAnalyzer.IsBackJump(AdminProjection.ToDetail(dialog), rueckwaerts));
    }

    /// <summary>
    /// Ein Marker auf einem <b>anderen</b> Frage-Paar zählt nicht: Er beschreibt einen anderen Zyklus.
    /// </summary>
    [Fact]
    public void UnmarkedBackJumps_prueft_das_Frage_Paar_nicht_nur_die_Existenz_eines_Markers()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loop = dialog.Loops.Single();
        loop.BreakingQuestionId = ids.SummaryQuestionId;

        Assert.Single(LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog)));
    }

    /// <summary>Vorwärtskanten erscheinen nie unter den Vorschlägen.</summary>
    [Fact]
    public void UnmarkedBackJumps_enthaelt_keine_Vorwaertskanten()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);

        Assert.Empty(LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog)));
    }

    private static DialogSession NewSession(Dialog dialog)
        => new()
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            DialogVersion = dialog.Version,
            ExternalUserKey = "designer",
            Status = SessionStatus.InProgress,
            StartedAt = TestDialogFactory.SampleTime,
        };
}
