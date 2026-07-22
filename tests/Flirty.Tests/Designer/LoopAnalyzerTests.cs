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
