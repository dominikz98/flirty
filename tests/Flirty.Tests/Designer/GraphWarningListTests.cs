using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="GraphWarningList"/> – the text version of the graph warnings, which the
/// confirmation before publishing hangs on (#118). The core is not the formatting but the
/// <b>completeness</b>: until #118 the confirmation drew only on the transition warnings, so an
/// unreachable question could be published without any confirmation – and once published the graph
/// is locked (ADR 0005), so the mistake then costs a new version.
/// </summary>
public sealed class GraphWarningListTests
{
    /// <summary>
    /// The issue's finding: a question no path leads to appears in the list – and it does so with its
    /// key, so that one can find it again in the graph.
    /// </summary>
    [Fact]
    public void Describe_names_the_unreachable_question_with_its_key()
    {
        var detail = AdminProjection.ToDetail(BranchingWithOrphan());

        var lines = GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail));

        var line = Assert.Single(lines);
        Assert.StartsWith("orphan: ", line, StringComparison.Ordinal);
        Assert.Contains("Not reachable", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every warning is named after its <b>cause</b>, and the three cases differ: a question carries
    /// its key, a loop marker its <c>CollectionKey</c> (it hangs on the frame, not on a question), and
    /// a warning on the dialog stays <b>without</b> a prefix. The last case is the delicate one: until
    /// #118 the list accessed <c>QuestionId</c> hard – a dialog or loop warning would have crashed it.
    /// </summary>
    [Fact]
    public void Describe_puts_the_cause_in_front_of_every_warning()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);

        // Without an exit the LoopAnalyzer warns at the breaking question, the shadowing CollectionKey
        // at the frame – and without an entry question the dialog warning is added on top.
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId));
        dialog.Loops.First().CollectionKey = "my-positions";
        dialog.StartQuestionId = null;

        var detail = AdminProjection.ToDetail(dialog);

        var lines = GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail));

        Assert.Contains(lines, line => line.StartsWith("more: ", StringComparison.Ordinal)
            && line.Contains("infinite loop", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("my-positions: ", StringComparison.Ordinal)
            && line.Contains("not referenceable", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("No entry question", StringComparison.Ordinal));
    }

    /// <summary>
    /// The contract nail, the counterpart to
    /// <see cref="TransitionWarningAnalyzerTests.Analyze_returns_the_existing_wordings_unchanged"/>:
    /// the publish confirmation counts these lines and the E2E suite searches within them. That also
    /// pins the order – nodes before edges, the way
    /// <see cref="Flirty.Designer.Models.DialogGraphModel.AllWarnings"/> returns them.
    /// </summary>
    [Fact]
    public void Describe_returns_the_wordings_unchanged()
    {
        var dialog = BranchingWithOrphan();

        // Plus a finding on an edge: the condition on a default is never evaluated.
        dialog.Transitions.First(transition => transition.IsDefault).Expression = "role == \"pm\"";

        var detail = AdminProjection.ToDetail(dialog);

        var lines = GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail));

        Assert.Equal(
            [
                "orphan: Not reachable from the entry question – no path via transitions leads here. The "
                + "question is never asked at runtime.",
                "role: The condition of a default transition is not evaluated at runtime.",
            ],
            lines);
    }

    /// <summary>A consistent graph yields nothing – otherwise publishing would always ask back.</summary>
    [Fact]
    public void Describe_returns_nothing_for_a_consistent_graph()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _));

        Assert.Empty(GraphWarningList.Describe(detail, DialogGraphBuilder.Build(detail)));
    }

    /// <summary>
    /// The branching dialog together with a question no transition points at – the same setup as in
    /// <see cref="DialogGraphBuilderTests.Build_marks_the_entry_the_completion_and_unreachable_questions"/>.
    /// </summary>
    private static Dialog BranchingWithOrphan()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.Questions.Add(new Question
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            Key = "orphan",
            Text = "Never reachable",
            Type = QuestionType.FreeText,
            Order = 9,
        });

        return dialog;
    }
}
