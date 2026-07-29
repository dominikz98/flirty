using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für die <see cref="GraphWarningList"/> – die Textfassung der Graph-Warnungen, an der die
/// Rückfrage vor dem Veröffentlichen hängt (#118). Der Kern ist nicht die Formatierung, sondern die
/// <b>Vollständigkeit</b>: Bis #118 speiste sich die Rückfrage nur aus den Übergangs-Warnungen, eine
/// unerreichbare Frage ließ sich also ohne Rückfrage veröffentlichen – und veröffentlicht ist der Graph
/// gesperrt (ADR 0005), der Fehler kostet dann eine neue Version.
/// </summary>
public sealed class GraphWarningListTests
{
    /// <summary>
    /// Der Befund des Issues: Eine Frage, auf die kein Pfad führt, steht in der Liste – und zwar mit
    /// ihrem Schlüssel, damit man sie im Graphen wiederfindet.
    /// </summary>
    [Fact]
    public void Describe_nennt_die_unerreichbare_Frage_mit_ihrem_Schluessel()
    {
        var detail = AdminProjection.ToDetail(BranchingWithOrphan());

        var lines = GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail));

        var line = Assert.Single(lines);
        Assert.StartsWith("verwaist: ", line, StringComparison.Ordinal);
        Assert.Contains("nicht erreichbar", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// Jede Warnung wird nach ihrem <b>Verursacher</b> benannt, und die drei Fälle unterscheiden sich:
    /// Eine Frage trägt ihren Schlüssel, ein Schleifen-Marker seinen <c>CollectionKey</c> (er hängt am
    /// Rahmen, nicht an einer Frage), und eine Warnung am Dialog bleibt <b>ohne</b> Präfix. Der letzte
    /// Fall ist der heikle: Bis #118 griff die Liste hart auf <c>QuestionId</c> zu – eine Dialog- oder
    /// Schleifen-Warnung hätte sie zum Absturz gebracht.
    /// </summary>
    [Fact]
    public void Describe_stellt_jeder_Warnung_ihren_Verursacher_voran()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);

        // Ohne Ausstieg warnt der LoopAnalyzer an der Breaking Question, der verdeckende CollectionKey am
        // Rahmen – und ohne Einstiegsfrage kommt die Warnung am Dialog dazu.
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId));
        dialog.Loops.First().CollectionKey = "meine-positionen";
        dialog.StartQuestionId = null;

        var detail = AdminProjection.ToDetail(dialog);

        var lines = GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail));

        Assert.Contains(lines, line => line.StartsWith("more: ", StringComparison.Ordinal)
            && line.Contains("Endlosschleife", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("meine-positionen: ", StringComparison.Ordinal)
            && line.Contains("nicht referenzierbar", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("Keine Einstiegsfrage", StringComparison.Ordinal));
    }

    /// <summary>
    /// Der Vertragsnagel, Gegenstück zu
    /// <see cref="TransitionWarningAnalyzerTests.Analyze_liefert_die_bisherigen_Wortlaute_unveraendert"/>:
    /// Die Publish-Rückfrage zählt diese Zeilen und die E2E-Suite sucht darin. Festgehalten ist damit
    /// auch die Reihenfolge – Knoten vor Kanten, wie
    /// <see cref="Flirty.Designer.Models.DialogGraphModel.AllWarnings"/> sie liefert.
    /// </summary>
    [Fact]
    public void Describe_liefert_die_Wortlaute_unveraendert()
    {
        var dialog = BranchingWithOrphan();

        // Zusätzlich ein Befund an einer Kante: Die Bedingung an einem Default wird nie ausgewertet.
        dialog.Transitions.First(transition => transition.IsDefault).Expression = "role == \"pm\"";

        var detail = AdminProjection.ToDetail(dialog);

        var lines = GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail));

        Assert.Equal(
            [
                "verwaist: Von der Einstiegsfrage aus nicht erreichbar – kein Pfad über Übergänge führt "
                + "hierher. Die Frage wird zur Laufzeit nie gestellt.",
                "role: Die Bedingung eines Default-Übergangs wird zur Laufzeit nicht ausgewertet.",
            ],
            lines);
    }

    /// <summary>Ein stimmiger Graph liefert nichts – sonst fragte das Veröffentlichen immer zurück.</summary>
    [Fact]
    public void Describe_liefert_fuer_einen_stimmigen_Graphen_nichts()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _));

        Assert.Empty(GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail)));
    }

    /// <summary>
    /// Der Branching-Dialog samt einer Frage, auf die kein Übergang zeigt – derselbe Aufbau wie in
    /// <see cref="DialogGraphBuilderTests.Build_markiert_Einstieg_Abschluss_und_nicht_erreichbare_Fragen"/>.
    /// </summary>
    private static Dialog BranchingWithOrphan()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.Questions.Add(new Question
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            Key = "verwaist",
            Text = "Nie erreichbar",
            Type = QuestionType.FreeText,
            Order = 9,
        });

        return dialog;
    }
}
