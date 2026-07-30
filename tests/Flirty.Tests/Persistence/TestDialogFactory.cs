using Flirty.Domain;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Shared test-data factory for the persistence tests: builds dialog aggregates used by the SQLite
/// configuration tests (#18) and the cross-provider migration tests (#19). All timestamps are
/// UTC-normalized, because the PostgreSQL provider (Npgsql) maps <see cref="DateTimeOffset"/> to
/// <c>timestamptz</c> and requires offset == UTC.
/// </summary>
internal static class TestDialogFactory
{
    /// <summary>Deterministic, UTC-normalized timestamp for reproducible round trips.</summary>
    public static readonly DateTimeOffset SampleTime = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a minimal dialog with the given <paramref name="key"/>, the
    /// <paramref name="version"/> and the display name <paramref name="name"/>.</summary>
    public static Dialog NewDialog(string key, int version, string name) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = name,
        Version = version,
        CreatedAt = SampleTime,
        UpdatedAt = SampleTime,
    };

    /// <summary>
    /// Builds a complete dialog aggregate with one child per navigation (a question with two
    /// options, a transition, a loop, a trigger). Returns the id of the single question via
    /// <paramref name="questionId"/>.
    /// </summary>
    public static Dialog BuildFullDialog(Guid dialogId, out Guid questionId)
    {
        questionId = Guid.NewGuid();
        var qId = questionId;

        return new Dialog
        {
            Id = dialogId,
            Key = "onboarding",
            Name = "Onboarding",
            Version = 1,
            IsPublished = true,
            StartQuestionId = qId,
            CreatedAt = SampleTime,
            UpdatedAt = SampleTime,
            Questions =
            {
                new Question
                {
                    Id = qId,
                    DialogId = dialogId,
                    Key = "role",
                    Text = "Which role?",
                    Type = QuestionType.SingleChoice,
                    Order = 0,
                    IsRequired = true,
                    ValidationRules = "{\"maxLength\":50}",
                    Options =
                    {
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = qId, Key = "dev", Label = "Developer", Value = "dev", Order = 0 },
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = qId, Key = "pm", Label = "Product Manager", Value = "pm", Order = 1 },
                    },
                },
            },
            Transitions =
            {
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = qId,
                    TargetQuestionId = Guid.NewGuid(), Priority = 0, IsDefault = true,
                },
            },
            Loops =
            {
                new LoopDefinition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, CollectionKey = "positions",
                    EntryQuestionId = qId, BreakingQuestionId = Guid.NewGuid(),
                },
            },
            Triggers =
            {
                new TriggerDefinition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, Scope = TriggerScope.OnDialogCompleted,
                    Kind = TriggerKind.Webhook, Config = "{\"url\":\"https://example.test/hook\"}",
                },
            },
            Layout =
            {
                new DialogLayout
                {
                    Id = Guid.NewGuid(), DialogId = dialogId,
                    ElementKind = LayoutElementKind.Question, ElementId = qId, X = 320, Y = 160,
                },
            },
        };
    }

    /// <summary>
    /// Builds a published dialog with branching for the submit runtime tests (#26): an entry
    /// question <c>role</c> (SingleChoice, options <c>dev</c>/<c>pm</c>) with a conditional
    /// transition (<c>role == "dev"</c>) to the question <c>devDetail</c> and a default transition to
    /// <c>pmDetail</c>. Both target questions are terminal (no outgoing transitions) and therefore
    /// trigger completion. Returns the question ids via <paramref name="ids"/>.
    /// </summary>
    public static Dialog BuildBranchingDialog(Guid dialogId, out BranchingDialogIds ids)
    {
        var roleQuestionId = Guid.NewGuid();
        var devQuestionId = Guid.NewGuid();
        var pmQuestionId = Guid.NewGuid();
        ids = new BranchingDialogIds(roleQuestionId, devQuestionId, pmQuestionId);

        return new Dialog
        {
            Id = dialogId,
            Key = "branching",
            Name = "Branching",
            Version = 1,
            IsPublished = true,
            StartQuestionId = roleQuestionId,
            CreatedAt = SampleTime,
            UpdatedAt = SampleTime,
            Questions =
            {
                new Question
                {
                    Id = roleQuestionId, DialogId = dialogId, Key = "role", Text = "Which role?",
                    Type = QuestionType.SingleChoice, Order = 0, IsRequired = true,
                    Options =
                    {
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "dev", Label = "Developer", Value = "dev", Order = 0 },
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "pm", Label = "Product Manager", Value = "pm", Order = 1 },
                    },
                },
                new Question
                {
                    Id = devQuestionId, DialogId = dialogId, Key = "devDetail",
                    Text = "Which programming language?", Type = QuestionType.FreeText, Order = 1,
                },
                new Question
                {
                    Id = pmQuestionId, DialogId = dialogId, Key = "pmDetail",
                    Text = "Which product?", Type = QuestionType.FreeText, Order = 2,
                },
            },
            Transitions =
            {
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = roleQuestionId,
                    Expression = "role == \"dev\"", TargetQuestionId = devQuestionId, Priority = 0, IsDefault = false,
                },
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = roleQuestionId,
                    TargetQuestionId = pmQuestionId, Priority = 1, IsDefault = true,
                },
            },
        };
    }

    /// <summary>
    /// Builds a published dialog with a loop for the loop runtime tests (#29): an entry question
    /// <c>position</c> (FreeText, <see cref="LoopDefinition.CollectionKey"/> <c>positions</c>) leads
    /// to the breaking question <c>more</c> (SingleChoice <c>yes</c>/<c>no</c>). From <c>more</c>
    /// there is a loop-back transition to <c>position</c> (condition
    /// <paramref name="loopBackExpression"/>, priority 0) and a default exit transition to the
    /// terminal question <c>summary</c> that lies outside the loop (priority 1). Via
    /// <paramref name="loopBackExpression"/> the break can be driven either by the answer
    /// (<c>more == "yes"</c>), the collection (<c>positions.Count &lt; 2</c>) or the iteration index
    /// (<c>iterationIndex &lt; 1</c>). Returns the question ids via <paramref name="ids"/>.
    /// </summary>
    public static Dialog BuildLoopDialog(
        Guid dialogId, out LoopDialogIds ids, string loopBackExpression = "more == \"yes\"")
    {
        var positionQuestionId = Guid.NewGuid();
        var moreQuestionId = Guid.NewGuid();
        var summaryQuestionId = Guid.NewGuid();
        ids = new LoopDialogIds(positionQuestionId, moreQuestionId, summaryQuestionId);

        return new Dialog
        {
            Id = dialogId,
            Key = "loop",
            Name = "Loop",
            Version = 1,
            IsPublished = true,
            StartQuestionId = positionQuestionId,
            CreatedAt = SampleTime,
            UpdatedAt = SampleTime,
            Questions =
            {
                new Question
                {
                    Id = positionQuestionId, DialogId = dialogId, Key = "position",
                    Text = "Which position?", Type = QuestionType.FreeText, Order = 0, IsRequired = true,
                },
                new Question
                {
                    Id = moreQuestionId, DialogId = dialogId, Key = "more",
                    Text = "Another position?", Type = QuestionType.SingleChoice, Order = 1, IsRequired = true,
                    Options =
                    {
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = moreQuestionId, Key = "yes", Label = "Yes", Value = "yes", Order = 0 },
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = moreQuestionId, Key = "no", Label = "No", Value = "no", Order = 1 },
                    },
                },
                new Question
                {
                    Id = summaryQuestionId, DialogId = dialogId, Key = "summary",
                    Text = "Summary?", Type = QuestionType.FreeText, Order = 2,
                },
            },
            Transitions =
            {
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = positionQuestionId,
                    TargetQuestionId = moreQuestionId, Priority = 0, IsDefault = true,
                },
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = moreQuestionId,
                    Expression = loopBackExpression, TargetQuestionId = positionQuestionId,
                    Priority = 0, IsDefault = false,
                },
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = moreQuestionId,
                    TargetQuestionId = summaryQuestionId, Priority = 1, IsDefault = true,
                },
            },
            Loops =
            {
                new LoopDefinition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, CollectionKey = "positions",
                    EntryQuestionId = positionQuestionId, BreakingQuestionId = moreQuestionId,
                },
            },
        };
    }
}

/// <summary>
/// The question ids of the dialog built by <see cref="TestDialogFactory.BuildBranchingDialog"/>.
/// </summary>
/// <param name="RoleQuestionId">The entry/choice question <c>role</c>.</param>
/// <param name="DevQuestionId">The target question <c>devDetail</c> of the conditional transition.</param>
/// <param name="PmQuestionId">The target question <c>pmDetail</c> of the default transition.</param>
internal sealed record BranchingDialogIds(Guid RoleQuestionId, Guid DevQuestionId, Guid PmQuestionId);

/// <summary>
/// The question ids of the loop dialog built by <see cref="TestDialogFactory.BuildLoopDialog"/>.
/// </summary>
/// <param name="PositionQuestionId">The loop's entry question <c>position</c>.</param>
/// <param name="MoreQuestionId">The breaking question <c>more</c>.</param>
/// <param name="SummaryQuestionId">The terminal question <c>summary</c> that lies outside the loop.</param>
internal sealed record LoopDialogIds(Guid PositionQuestionId, Guid MoreQuestionId, Guid SummaryQuestionId);
