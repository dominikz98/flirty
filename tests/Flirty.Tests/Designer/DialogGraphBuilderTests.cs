using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für den <see cref="DialogGraphBuilder"/> – das Zeichenmodell der Graph-Ansicht (#101). Geprüft
/// wird vor allem, dass die Aussagen des Domänenmodells auf dem Canvas <b>ehrlich</b> ankommen: der
/// reguläre Abschluss als Abschluss und nicht als kaputte Kante, unerreichbare Fragen als solche, und
/// jede Warnung an dem Element, das sie verursacht.
/// </summary>
public sealed class DialogGraphBuilderTests
{
    /// <summary>
    /// Die drei Marker, die den Ablauf lesbar machen. Der Abschluss ist der heikelste: Eine Frage ohne
    /// ausgehenden Übergang ist der <b>reguläre</b> Dialogabschluss (<c>TransitionResolver</c> liefert
    /// dort <see langword="null"/>) – ohne Kennzeichnung liest sich das wie eine fehlende Kante.
    /// </summary>
    [Fact]
    public void Build_markiert_Einstieg_Abschluss_und_nicht_erreichbare_Fragen()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        var orphanId = Guid.NewGuid();
        dialog.Questions.Add(new Question
        {
            Id = orphanId,
            DialogId = dialog.Id,
            Key = "verwaist",
            Text = "Nie erreichbar",
            Type = QuestionType.FreeText,
            Order = 9,
        });

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var start = model.Node(ids.RoleQuestionId)!;
        Assert.True(start.IsStart);
        Assert.False(start.IsTerminal);
        Assert.False(start.IsUnreachable);

        var terminal = model.Node(ids.DevQuestionId)!;
        Assert.True(terminal.IsTerminal);
        Assert.False(terminal.IsStart);

        var orphan = model.Node(orphanId)!;
        Assert.True(orphan.IsUnreachable);
        Assert.Contains(
            orphan.Warnings,
            warning => warning.Text.Contains("nicht erreichbar", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ohne Einstiegsfrage ist die Erreichbarkeit gar nicht bestimmbar. Der Befund gehört deshalb an den
    /// Dialog – und ausdrücklich <b>nicht</b> an jeden Knoten, sonst wäre der ganze Graph rot, obwohl
    /// nur eine einzige Angabe fehlt.
    /// </summary>
    [Fact]
    public void Build_warnt_ohne_Einstiegsfrage_auf_Dialog_Ebene()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.StartQuestionId = null;

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Contains(
            model.DialogWarnings,
            warning => warning.Kind == GraphElementKind.Dialog
                && warning.Text.Contains("Keine Einstiegsfrage", StringComparison.Ordinal));
        Assert.All(model.Nodes, node => Assert.False(node.IsUnreachable));
    }

    /// <summary>
    /// Der Kern des Akzeptanzkriteriums „Warnungen erscheinen am betroffenen Element": Die
    /// Gruppen-Warnung sitzt am Knoten, die Kanten-Warnung an der Kante – und keine geht verloren.
    /// </summary>
    [Fact]
    public void Build_verortet_Uebergangswarnungen_am_Knoten_und_an_der_Kante()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);

        // Der bisher bedingte Übergang wird bedingungslos und verdeckt damit den Default dahinter.
        var blocking = dialog.Transitions.First(transition => !transition.IsDefault);
        blocking.Expression = null;

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Contains(
            model.Edge(blocking.Id)!.Warnings,
            warning => warning.Text.Contains("greift immer", StringComparison.Ordinal));

        // Und die Gruppen-Warnung bleibt am Knoten: mehrere Defaults sind niemandes Einzelschuld.
        dialog.Transitions.First(transition => transition.IsDefault).IsDefault = true;
        var second = dialog.Transitions.First(transition => !transition.IsDefault);
        second.IsDefault = true;

        var withTwoDefaults = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Contains(
            withTwoDefaults.Node(ids.RoleQuestionId)!.Warnings,
            warning => warning.Text.Contains("Mehrere Default-Übergänge", StringComparison.Ordinal));
    }

    /// <summary>
    /// Die Schleifen-Befunde verteilen sich auf zwei Orte: Was den Marker betrifft, hängt am Rahmen; was
    /// die Breaking Question betrifft, an ihrem Knoten.
    /// </summary>
    [Fact]
    public void Build_verortet_die_Loop_Befunde_am_Rahmen_und_an_der_Breaking_Question()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId));
        dialog.Loops.First().CollectionKey = "meine-positionen";

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var frame = Assert.Single(model.Loops);
        Assert.Contains(
            frame.Warnings,
            warning => warning.Text.Contains("nicht referenzierbar", StringComparison.Ordinal));

        Assert.Contains(
            model.Node(ids.MoreQuestionId)!.Warnings,
            warning => warning.Text.Contains("Endlosschleife", StringComparison.Ordinal));
    }

    /// <summary>Der Rahmen umschließt genau die Knoten des vom <c>LoopAnalyzer</c> berechneten Bereichs.</summary>
    [Fact]
    public void Build_rahmt_den_LoopAnalyzer_Body_als_Bounding_Box()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var frame = Assert.Single(model.Loops);
        Assert.Equal("positions", frame.CollectionKey);

        foreach (var id in new[] { ids.PositionQuestionId, ids.MoreQuestionId })
        {
            var node = model.Node(id)!;
            Assert.True(node.X >= frame.X, $"{node.Key} liegt links des Rahmens.");
            Assert.True(node.Y >= frame.Y, $"{node.Key} liegt über dem Rahmen.");
            Assert.True(node.X + GraphMetrics.NodeWidth <= frame.X + frame.Width);
            Assert.True(node.Y + GraphMetrics.NodeHeight <= frame.Y + frame.Height);
            Assert.True(node.InLoop);
        }

        // Die Frage hinter dem Ausstieg gehört nicht dazu und darf nicht eingerahmt sein.
        var outside = model.Node(ids.SummaryQuestionId)!;
        Assert.False(outside.InLoop);
        Assert.True(outside.Y > frame.Y + frame.Height);

        Assert.True(model.Node(ids.PositionQuestionId)!.IsLoopEntry);
        Assert.True(model.Node(ids.MoreQuestionId)!.IsLoopBreaking);
    }

    /// <summary>
    /// Ein Marker, dessen Fragen gelöscht wurden, hat keinen Bereich – dann gibt es keinen Rahmen,
    /// sondern nur die Warnung. Ein Rahmen um nichts wäre schlimmer als keiner.
    /// </summary>
    [Fact]
    public void Build_zeichnet_keinen_Rahmen_fuer_einen_Marker_ins_Leere()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().EntryQuestionId = Guid.NewGuid();

        var frame = Assert.Single(DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog)).Loops);

        Assert.Equal(0, frame.Width);
        Assert.Equal(0, frame.Height);
        Assert.NotEmpty(frame.Warnings);
    }

    /// <summary>
    /// Trigger hängen dort, wo sie feuern: <c>AfterQuestion</c> am Knoten, alles andere an den
    /// Scope-Markern. <c>AfterAnswer</c> hat keinen natürlichen Ort und landet bewusst am Start-Marker –
    /// an jeden Knoten gehängt würde er dieselbe Konfiguration vielfach zeigen.
    /// </summary>
    [Fact]
    public void Build_haengt_Trigger_an_die_Frage_bzw_an_die_Scope_Marker()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.AfterQuestion, ids.RoleQuestionId));
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.OnDialogStarted, null));
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.AfterAnswer, null));
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.OnDialogCompleted, null));

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var chip = Assert.Single(model.Node(ids.RoleQuestionId)!.Triggers);
        Assert.Contains("Webhook", chip.Label, StringComparison.Ordinal);
        Assert.Empty(model.Node(ids.DevQuestionId)!.Triggers);

        Assert.Equal(2, model.StartMarker!.Triggers.Count);
        Assert.Single(model.EndMarker!.Triggers);

        // Die Marker liegen außerhalb der Knotenfläche – die Zeichenfläche wächst nach oben und unten.
        Assert.True(model.MinY < 0);
        Assert.True(model.Height > GraphLayout.Compute(AdminProjection.ToDetail(dialog)).Height);
    }

    /// <summary>Ohne solche Trigger gibt es auch keine Marker – der Canvas bleibt frei von Leerformen.</summary>
    [Fact]
    public void Build_zeigt_ohne_Scope_Trigger_keine_Marker()
    {
        var model = DialogGraphBuilder.Build(
            AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _)));

        Assert.Null(model.StartMarker);
        Assert.Null(model.EndMarker);
        Assert.Equal(0, model.MinY);
    }

    /// <summary>
    /// Übergänge und Trigger auf gelöschte Fragen sind nicht zeichenbar. Sie verschwinden nicht still,
    /// sondern werden getrennt ausgewiesen – wie der bestehende Hinweis in der Listenansicht.
    /// </summary>
    [Fact]
    public void Build_weist_verwaiste_Uebergaenge_und_Trigger_getrennt_aus()
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
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.AfterQuestion, Guid.NewGuid()));

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Single(model.OrphanTransitions);
        Assert.Single(model.OrphanTriggers);
        Assert.Equal(2, model.Edges.Count);
        Assert.All(model.Nodes, node => Assert.Empty(node.Triggers));
    }

    /// <summary>
    /// Für Screenreader ist das <c>aria-label</c> die einzige Fassung eines Knotens. Alles, was sonst nur
    /// als Farbe oder Position vorliegt, muss darin vorkommen.
    /// </summary>
    [Fact]
    public void Build_beschreibt_jeden_Knoten_vollstaendig_in_Worten()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var start = model.Node(ids.RoleQuestionId)!.AriaLabel;
        Assert.Contains("Frage role", start, StringComparison.Ordinal);
        Assert.Contains("Einfachauswahl", start, StringComparison.Ordinal);
        Assert.Contains("Pflichtfrage", start, StringComparison.Ordinal);
        Assert.Contains("2 Antwortoptionen", start, StringComparison.Ordinal);
        Assert.Contains("Einstiegsfrage", start, StringComparison.Ordinal);
        Assert.Contains("2 ausgehende Übergänge", start, StringComparison.Ordinal);

        var terminal = model.Node(ids.PmQuestionId)!.AriaLabel;
        Assert.Contains("Abschluss, kein ausgehender Übergang", terminal, StringComparison.Ordinal);
        Assert.Contains("optional", terminal, StringComparison.Ordinal);

        // Auch Kanten tragen ihre volle Aussage – sie sind nicht fokussierbar, aber vorlesbar.
        Assert.All(model.Edges, edge => Assert.Contains("Übergang", edge.AriaLabel, StringComparison.Ordinal));
    }

    /// <summary>Die Zusammenfassung ersetzt das Bild für alle, die es nicht sehen.</summary>
    [Fact]
    public void Build_fasst_den_Graphen_in_Worten_zusammen()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);

        var summary = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog)).Summary;

        Assert.Contains("3 Fragen", summary, StringComparison.Ordinal);
        Assert.Contains("3 Übergänge", summary, StringComparison.Ordinal);
        Assert.Contains("1 Schleife", summary, StringComparison.Ordinal);
        Assert.Contains("keine Warnungen", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Die Kantenbeschriftung nennt die Auswertungsposition – dieselbe 1-basierte Zählung wie die Spalte
    /// „#“ der Listenansicht, auf die sich auch die Warntexte beziehen.
    /// </summary>
    [Fact]
    public void Build_beschriftet_Kanten_mit_Bedingung_und_Auswertungsposition()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var conditional = model.Edges.Single(edge => !edge.IsDefault);
        Assert.Equal(1, conditional.Position);
        Assert.Contains("role ==", conditional.Label, StringComparison.Ordinal);

        var fallback = model.Edges.Single(edge => edge.IsDefault);
        Assert.Equal(2, fallback.Position);
        Assert.Equal("Default", fallback.Label);
    }

    /// <summary>
    /// Der Badge „Rücksprung“ folgt der <b>Listenreihenfolge</b> – dieselbe Aussage wie im
    /// <c>DialogEditor</c>, damit Liste und Graph nicht Verschiedenes behaupten. Die Zeichenform des
    /// Layouts ist eine andere Frage (Schichtung) und darf hier nicht durchschlagen.
    /// </summary>
    [Fact]
    public void Build_markiert_Ruecksprünge_nach_der_Listenreihenfolge()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(
            transition => transition.TargetQuestionId == ids.PositionQuestionId);

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.True(model.Edge(loopBack.Id)!.IsBackJump);
        Assert.All(
            model.Edges.Where(edge => edge.TransitionId != loopBack.Id),
            edge => Assert.False(edge.IsBackJump));
    }

    /// <summary>
    /// Derselbe Dialog ergibt dasselbe Modell – die Determinismus-Zusage trägt bis hierher.
    /// </summary>
    /// <remarks>
    /// Verglichen werden die Skalarwerte, nicht die Records selbst: Ein <c>record</c> vergleicht seine
    /// Sammlungs-Eigenschaften über <c>EqualityComparer&lt;T&gt;.Default</c>, für Listen also über die
    /// <b>Referenz</b>. Zwei Aufrufe erzeugen zwangsläufig verschiedene Listeninstanzen; ein direkter
    /// <c>Assert.Equal</c> auf den Knoten prüfte damit Objektidentität statt Anordnung.
    /// </remarks>
    [Fact]
    public void Build_liefert_fuer_denselben_Dialog_dasselbe_Ergebnis()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _));

        var first = DialogGraphBuilder.Build(detail);
        var second = DialogGraphBuilder.Build(detail);

        Assert.Equal(
            first.Nodes.Select(node => (node.QuestionId, node.X, node.Y, node.AriaLabel)),
            second.Nodes.Select(node => (node.QuestionId, node.X, node.Y, node.AriaLabel)));
        Assert.Equal(
            first.Edges.Select(edge => (edge.TransitionId, edge.Path, edge.LabelX, edge.LabelY, edge.Label)),
            second.Edges.Select(edge => (edge.TransitionId, edge.Path, edge.LabelX, edge.LabelY, edge.Label)));
        Assert.Equal(
            first.Loops.Select(loop => (loop.LoopId, loop.X, loop.Y, loop.Width, loop.Height)),
            second.Loops.Select(loop => (loop.LoopId, loop.X, loop.Y, loop.Width, loop.Height)));
        Assert.Equal(first.Summary, second.Summary);
    }

    /// <summary>Ein leerer Dialog ergibt ein leeres, aber gültiges Modell – die Seite darf nicht scheitern.</summary>
    [Fact]
    public void Build_vertraegt_einen_Dialog_ohne_Fragen()
    {
        var dialog = TestDialogFactory.NewDialog("leer", 1, "Leer");

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
        Assert.Empty(model.DialogWarnings);
        Assert.Contains("0 Fragen", model.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Der Schleifen-Rahmen folgt einem verschobenen Knoten: Er entsteht als Bounding-Box über den
    /// Positionen des Bodys, nicht über die Schichtung. Zöge er nicht mit, läge ein Knoten seiner
    /// eigenen Schleife außerhalb ihres Rahmens.
    /// </summary>
    [Fact]
    public void Build_zieht_den_Schleifen_Rahmen_ueber_die_gespeicherte_Position()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids));
        var before = Assert.Single(DialogGraphBuilder.Build(detail).Loops);

        var moved = detail with
        {
            Layout =
            [
                new DialogLayoutDetail(
                    Guid.NewGuid(), detail.Dialog.Id, LayoutElementKind.Question,
                    ids.MoreQuestionId, 1200, 900),
            ],
        };

        var model = DialogGraphBuilder.Build(moved);
        var frame = Assert.Single(model.Loops);

        Assert.True(frame.Width > before.Width);
        Assert.True(frame.X + frame.Width >= 1200 + GraphMetrics.NodeWidth);
        Assert.True(frame.Y + frame.Height >= 900 + GraphMetrics.NodeHeight);

        // Und der Knoten weiß von seiner eigenen Position – daran hängt die Markierung in der Karte.
        Assert.True(model.Node(ids.MoreQuestionId)!.IsPinned);
        Assert.Contains("eigene Position", model.Node(ids.MoreQuestionId)!.AriaLabel, StringComparison.Ordinal);
    }

    private static TriggerDefinition Trigger(Guid dialogId, TriggerScope scope, Guid? questionId)
        => new()
        {
            Id = Guid.NewGuid(),
            DialogId = dialogId,
            Scope = scope,
            QuestionId = questionId,
            Kind = TriggerKind.Webhook,
            Config = """{"url":"https://example.test/hook"}""",
        };
}
