using System.Globalization;
using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für das Auto-Layout der Graph-Ansicht (#101). Der Schwerpunkt liegt auf dem
/// <b>Determinismus</b>: Derselbe Graph muss dieselben Koordinaten ergeben, sonst wackeln E2E-Selektoren
/// und Screenshots. Geprüft wird das gegen die drei Quellen, aus denen Nichtdeterminismus üblicherweise
/// einsickert – Hash-Iterationsreihenfolge, neu vergebene Guids und die Speicherreihenfolge der
/// Übergänge.
/// </summary>
public sealed class GraphLayoutTests
{
    /// <summary>
    /// Zwei Aufrufe auf derselben Eingabe müssen deckungsgleich sein. Der Test fängt jede Stelle, an der
    /// eine <c>HashSet</c>- oder <c>Dictionary</c>-Iteration in das Ergebnis durchschlägt.
    /// </summary>
    [Fact]
    public void Compute_liefert_bei_gleichem_Graphen_gleiche_Koordinaten()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _));

        var first = GraphLayout.Compute(detail);
        var second = GraphLayout.Compute(detail);

        Assert.Equal(first.Nodes, second.Nodes);
        Assert.Equal(first.Edges, second.Edges);
        Assert.Equal(first.Crossings, second.Crossings);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
    }

    /// <summary>
    /// Das Layout darf nicht an den Guids hängen: <c>CreateDialogVersionCommand</c> vergibt beim Klonen
    /// für <b>jede</b> Frage eine neue Guid (ADR 0005 – der einzige Weg, einen veröffentlichten Dialog
    /// weiterzuentwickeln). Ein Guid-basiertes Layout würfelte damit bei jeder neuen Version neu durch.
    /// </summary>
    [Fact]
    public void Compute_haengt_nicht_von_den_Guids_ab()
    {
        var first = Layout(AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _)));
        var second = Layout(AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _)));

        Assert.Equal(first, second);

        static (int Layer, int Slot, double X, double Y)[] Layout(DialogDetail detail)
        {
            var result = GraphLayout.Compute(detail);
            var byOrder = detail.Questions.ToDictionary(question => question.Id, question => question.Order);

            return
            [
                .. result.Nodes
                    .OrderBy(node => byOrder[node.QuestionId])
                    .Select(node => (node.Layer, node.Slot, node.X, node.Y))
            ];
        }
    }

    /// <summary>
    /// <c>DialogDetail.Transitions</c> ist global nach <c>Priority</c> sortiert – bei gleicher Priorität
    /// in verschiedenen Ausgangsfragen ist die Reihenfolge damit beliebig. Das Layout darf sich davon
    /// nicht beeindrucken lassen.
    /// </summary>
    [Fact]
    public void Compute_ist_unabhaengig_von_der_globalen_Uebergangs_Reihenfolge()
    {
        var detail = Branching();
        var reversed = detail with { Transitions = [.. detail.Transitions.Reverse()] };

        var expected = GraphLayout.Compute(detail);
        var actual = GraphLayout.Compute(reversed);

        Assert.Equal(expected.Nodes, actual.Nodes);
        Assert.Equal(
            expected.Edges.OrderBy(edge => edge.TransitionId),
            actual.Edges.OrderBy(edge => edge.TransitionId));
    }

    /// <summary>Die Einstiegsfrage liegt auf Schicht 0, ihre Nachfolger eine Schicht darunter.</summary>
    [Fact]
    public void Compute_schichtet_ab_der_Einstiegsfrage()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        Assert.Equal(0, Node(result, ids.PositionQuestionId).Layer);
        Assert.Equal(1, Node(result, ids.MoreQuestionId).Layer);
        Assert.Equal(2, Node(result, ids.SummaryQuestionId).Layer);
    }

    /// <summary>
    /// Der Rücksprung einer Schleife wird als solcher erkannt und aus der Schichtung herausgehalten –
    /// sonst zöge der Zyklus die Einstiegsfrage hinter ihre eigene Breaking Question.
    /// </summary>
    [Fact]
    public void Compute_bricht_Rueckwaertskanten_auf()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(
            transition => transition.TargetQuestionId == ids.PositionQuestionId);

        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        Assert.Equal(
            GraphEdgeShape.BackJump,
            result.Edges.Single(edge => edge.TransitionId == loopBack.Id).Shape);
        Assert.Equal(0, Node(result, ids.PositionQuestionId).Layer);
    }

    /// <summary>
    /// Fragen ohne Pfad von der Einstiegsfrage liegen hinter dem erreichbaren Graphen – getrennt durch
    /// eine leere Schicht, damit das Band als solches lesbar ist.
    /// </summary>
    [Fact]
    public void Compute_ordnet_nicht_erreichbare_Fragen_hinter_die_erreichbaren()
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

        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        var orphan = Node(result, orphanId);
        var deepest = Node(result, ids.DevQuestionId).Layer;

        Assert.False(orphan.IsReachable);
        Assert.True(orphan.Layer >= deepest + 2, $"Erwartet mindestens {deepest + 2}, war {orphan.Layer}.");
        Assert.True(Node(result, ids.RoleQuestionId).IsReachable);
    }

    /// <summary>
    /// Ohne gesetzte Einstiegsfrage gibt es keinen Bezugspunkt für Erreichbarkeit. Alle Fragen als
    /// „nicht erreichbar“ zu markieren wäre irreführend – der Befund gehört an den Dialog.
    /// </summary>
    [Fact]
    public void Compute_ohne_Einstiegsfrage_markiert_keine_Frage_als_unerreichbar()
    {
        var detail = Branching();
        var headless = detail with { Dialog = detail.Dialog with { StartQuestionId = null } };

        var result = GraphLayout.Compute(headless);

        Assert.All(result.Nodes, node => Assert.True(node.IsReachable));
        Assert.Equal(3, result.Nodes.Count);
    }

    /// <summary>Zwei Knoten dürfen sich nie überlagern – sonst verdeckt einer den anderen.</summary>
    [Fact]
    public void Compute_ueberlappt_keine_Knoten()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        foreach (var left in result.Nodes)
        {
            foreach (var right in result.Nodes.Where(candidate => candidate.QuestionId != left.QuestionId))
            {
                var apart = left.X + GraphMetrics.NodeWidth <= right.X
                    || right.X + GraphMetrics.NodeWidth <= left.X
                    || left.Y + GraphMetrics.NodeHeight <= right.Y
                    || right.Y + GraphMetrics.NodeHeight <= left.Y;

                Assert.True(apart, $"Knoten bei ({left.X}|{left.Y}) und ({right.X}|{right.Y}) überlappen.");
            }
        }
    }

    /// <summary>
    /// Mehrere Übergänge zwischen denselben zwei Fragen müssen unterscheidbar bleiben: Sie werden
    /// aufgefächert und bekommen getrennte Ankerpunkte für ihre Beschriftung. Deckungsgleiche Kanten
    /// wären auf dem Canvas schlicht eine.
    /// </summary>
    [Fact]
    public void Compute_faechert_Mehrfachkanten_zwischen_denselben_Knoten_auf()
    {
        var detail = Branching();
        var first = detail.Transitions[0];
        var parallel = first with { Id = Guid.NewGuid(), Expression = "role == \"other\"", Priority = 5 };
        var withParallel = detail with { Transitions = [.. detail.Transitions, parallel] };

        var result = GraphLayout.Compute(withParallel);

        var left = result.Edges.Single(edge => edge.TransitionId == first.Id);
        var right = result.Edges.Single(edge => edge.TransitionId == parallel.Id);

        Assert.Equal(2, left.FanCount);
        Assert.Equal(2, right.FanCount);
        Assert.NotEqual(left.FanIndex, right.FanIndex);
        Assert.NotEqual(left.Path, right.Path);
        Assert.NotEqual(left.LabelX, right.LabelX);
    }

    /// <summary>
    /// Die Kreuzungsreduktion muss wirken: In der reinen Autorenreihenfolge kreuzen sich die Kanten
    /// <c>a → d</c> und <c>b → c</c>; das Baryzentrum dreht die untere Schicht um und löst das auf.
    /// </summary>
    [Fact]
    public void Compute_reduziert_Kreuzungen()
    {
        var result = GraphLayout.Compute(CrossingGraph(out var ids));

        Assert.Equal(0, result.Crossings);

        // Die untere Schicht steht entgegen der Autorenreihenfolge – genau das ist die Umsortierung.
        Assert.True(Node(result, ids.D).Slot < Node(result, ids.C).Slot);
    }

    /// <summary>Der stimmige Loop-Dialog kommt ohne jede Kreuzung aus.</summary>
    [Fact]
    public void Compute_ordnet_den_Loop_Dialog_kreuzungsfrei_an()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);

        Assert.Equal(0, GraphLayout.Compute(AdminProjection.ToDetail(dialog)).Crossings);
    }

    /// <summary>Ein Dialog ohne Fragen ergibt eine leere, aber gültige Zeichenfläche.</summary>
    [Fact]
    public void Compute_vertraegt_einen_Dialog_ohne_Fragen()
    {
        var detail = Branching() with { Questions = [], Transitions = [] };

        var result = GraphLayout.Compute(detail);

        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    /// <summary>
    /// Übergänge auf gelöschte Fragen sind nicht zeichenbar – sie haben keinen Knoten, an dem sie
    /// ansetzen könnten, und fallen deshalb aus dem Layout heraus (nicht aus der Anzeige: der Builder
    /// weist sie getrennt aus).
    /// </summary>
    [Fact]
    public void Compute_ueberspringt_Uebergaenge_auf_unbekannte_Fragen()
    {
        var detail = Branching();
        var orphan = detail.Transitions[0] with { Id = Guid.NewGuid(), TargetQuestionId = Guid.NewGuid() };
        var withOrphan = detail with { Transitions = [.. detail.Transitions, orphan] };

        var result = GraphLayout.Compute(withOrphan);

        Assert.DoesNotContain(result.Edges, edge => edge.TransitionId == orphan.Id);
        Assert.Equal(detail.Transitions.Count, result.Edges.Count);
    }

    /// <summary>
    /// Der Designer läuft unter <c>de-DE</c> (<c>DesignerApp.DisplayCulture</c>). Eine Koordinate mit
    /// Dezimal<b>komma</b> zerlegt einen SVG-Pfad still – das Komma ist dort ein Trennzeichen. Es gäbe
    /// weder Ausnahme noch Meldung, nur ein falsches Bild.
    /// </summary>
    [Fact]
    public void Pfade_tragen_auch_unter_de_DE_einen_Dezimalpunkt()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("12.5", SvgFormat.N(12.5));

            var result = GraphLayout.Compute(CrossingGraph(out _));

            Assert.All(result.Edges, edge => Assert.DoesNotContain(',', edge.Path));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static GraphNodePosition Node(GraphLayoutResult result, Guid questionId)
        => result.Nodes.Single(node => node.QuestionId == questionId);

    private static DialogDetail Branching()
        => AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _));

    /// <summary>
    /// Ein Graph, dessen Autorenreihenfolge zwangsläufig eine Kreuzung erzeugt: <c>root</c> verzweigt auf
    /// <c>a</c> und <c>b</c>, <c>a</c> führt auf das <b>zweite</b> Blatt und <c>b</c> auf das erste.
    /// </summary>
    private static DialogDetail CrossingGraph(out (Guid A, Guid B, Guid C, Guid D) ids)
    {
        var dialogId = Guid.NewGuid();
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        ids = (a, b, c, d);

        QuestionDetail Question(Guid id, string key, int order)
            => new(id, dialogId, key, $"Frage {key}", QuestionType.FreeText, order, false, null, []);

        TransitionDetail Edge(Guid from, Guid target)
            => new(Guid.NewGuid(), dialogId, from, target, null, 0, true);

        return new DialogDetail(
            new DialogSummary(
                dialogId, "crossing", "Kreuzung", null, 1, false, root,
                TestDialogFactory.SampleTime, TestDialogFactory.SampleTime),
            [
                Question(root, "root", 0),
                Question(a, "a", 1),
                Question(b, "b", 2),
                Question(c, "c", 3),
                Question(d, "d", 4),
            ],
            [Edge(root, a), Edge(root, b), Edge(a, d), Edge(b, c)],
            [],
            []);
    }
}
