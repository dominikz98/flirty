using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Pure unit tests of the <see cref="LoopResolver"/> (issue #29) without a database: body computation
/// incl. the single-question loop and the rejection of overlaps, the iteration/instance assignment
/// while persisting, the build-up of the collections gathered per iteration and the current
/// iteration index.
/// </summary>
public sealed class LoopResolverTests
{
    private static DialogSession NewSession(params SessionAnswer[] answers)
    {
        var session = new DialogSession
        {
            Id = Guid.NewGuid(),
            DialogId = Guid.NewGuid(),
            DialogVersion = 1,
            ExternalUserKey = "user-1",
            Status = SessionStatus.InProgress,
            StartedAt = TestDialogFactory.SampleTime,
        };

        foreach (var answer in answers)
        {
            session.Answers.Add(answer);
        }

        return session;
    }

    private static SessionAnswer Answer(
        Guid questionId, string value, int sequence, Guid? loopInstanceId = null, int? iterationIndex = null)
        => new()
        {
            Id = Guid.NewGuid(),
            QuestionId = questionId,
            Value = value,
            AnsweredAt = TestDialogFactory.SampleTime,
            Sequence = sequence,
            LoopInstanceId = loopInstanceId,
            IterationIndex = iterationIndex,
        };

    // ---- Iteration/instance assignment ------------------------------------------------------

    /// <summary>The first entry into the loop starts a fresh instance with iteration 0.</summary>
    [Fact]
    public void ResolveAssignment_the_first_entry_starts_a_new_instance_at_iteration_zero()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);

        var assignment = resolver.ResolveAssignment(NewSession(), ids.PositionQuestionId);

        Assert.NotNull(assignment.LoopInstanceId);
        Assert.NotEqual(Guid.Empty, assignment.LoopInstanceId!.Value);
        Assert.Equal(0, assignment.IterationIndex);
    }

    /// <summary>A follow-up question of the same iteration keeps the instance and the iteration index.</summary>
    [Fact]
    public void ResolveAssignment_a_follow_up_question_keeps_the_instance_and_the_iteration()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);
        var instanceId = Guid.NewGuid();
        var session = NewSession(Answer(ids.PositionQuestionId, "\"A\"", 0, instanceId, 0));

        var assignment = resolver.ResolveAssignment(session, ids.MoreQuestionId);

        Assert.Equal(instanceId, assignment.LoopInstanceId);
        Assert.Equal(0, assignment.IterationIndex);
    }

    /// <summary>Answering the entry question again (loop back) increases the iteration index.</summary>
    [Fact]
    public void ResolveAssignment_a_loop_back_increases_the_iteration_index()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);
        var instanceId = Guid.NewGuid();
        var session = NewSession(
            Answer(ids.PositionQuestionId, "\"A\"", 0, instanceId, 0),
            Answer(ids.MoreQuestionId, "\"yes\"", 1, instanceId, 0));

        var assignment = resolver.ResolveAssignment(session, ids.PositionQuestionId);

        Assert.Equal(instanceId, assignment.LoopInstanceId);
        Assert.Equal(1, assignment.IterationIndex);
    }

    /// <summary>A question outside every loop gets no loop fields.</summary>
    [Fact]
    public void ResolveAssignment_a_non_loop_question_returns_null()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);

        var assignment = resolver.ResolveAssignment(NewSession(), ids.SummaryQuestionId);

        Assert.Null(assignment.LoopInstanceId);
        Assert.Null(assignment.IterationIndex);
    }

    /// <summary>A single-question loop (entry == breaking) counts up on every re-answer.</summary>
    [Fact]
    public void ResolveAssignment_a_single_question_loop_counts_up()
    {
        var questionId = Guid.NewGuid();
        var dialog = SingleQuestionLoopDialog(questionId);
        var resolver = new LoopResolver(dialog);
        var instanceId = Guid.NewGuid();
        var session = NewSession(Answer(questionId, "\"A\"", 0, instanceId, 0));

        var assignment = resolver.ResolveAssignment(session, questionId);

        Assert.Equal(instanceId, assignment.LoopInstanceId);
        Assert.Equal(1, assignment.IterationIndex);
    }

    // ---- Collections ------------------------------------------------------------------------

    /// <summary>The collection key is bound even without a previous answer (as empty).</summary>
    [Fact]
    public void BuildCollections_binds_the_key_even_without_answers()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        var resolver = new LoopResolver(dialog);

        var collections = resolver.BuildCollections(NewSession());

        Assert.True(collections.ContainsKey("positions"));
        Assert.Empty(collections["positions"]);
    }

    /// <summary>The collection gathers the entry answer per iteration in iteration order.</summary>
    [Fact]
    public void BuildCollections_gathers_the_entry_values_per_iteration()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);
        var instanceId = Guid.NewGuid();
        var session = NewSession(
            Answer(ids.PositionQuestionId, "\"A\"", 0, instanceId, 0),
            Answer(ids.MoreQuestionId, "\"yes\"", 1, instanceId, 0),
            Answer(ids.PositionQuestionId, "\"B\"", 2, instanceId, 1));

        var collections = resolver.BuildCollections(session);

        Assert.Equal(["\"A\"", "\"B\""], collections["positions"]);
    }

    // ---- Iteration index --------------------------------------------------------------------

    /// <summary>The iteration index mirrors the latest answer to the question; outside the loop it is null.</summary>
    [Fact]
    public void ResolveIterationIndex_returns_the_current_iteration_and_null_outside()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);
        var instanceId = Guid.NewGuid();
        var session = NewSession(
            Answer(ids.PositionQuestionId, "\"A\"", 0, instanceId, 0),
            Answer(ids.PositionQuestionId, "\"B\"", 2, instanceId, 1));

        Assert.Equal(1, resolver.ResolveIterationIndex(session, ids.PositionQuestionId));
        Assert.Null(resolver.ResolveIterationIndex(session, ids.SummaryQuestionId));
    }

    // ---- Constructor ------------------------------------------------------------------------

    /// <summary>Overlapping loop ranges are rejected in the constructor (nesting is out of scope).</summary>
    [Fact]
    public void Constructor_throws_on_overlapping_loops()
    {
        var dialogId = Guid.NewGuid();
        var q1 = Guid.NewGuid();
        var q2 = Guid.NewGuid();
        var dialog = new Dialog
        {
            Id = dialogId, Key = "overlap", Name = "Overlap", Version = 1, IsPublished = true,
            StartQuestionId = q1, CreatedAt = TestDialogFactory.SampleTime, UpdatedAt = TestDialogFactory.SampleTime,
            Questions =
            {
                new Question { Id = q1, DialogId = dialogId, Key = "q1", Text = "Q1", Type = QuestionType.FreeText, Order = 0 },
                new Question { Id = q2, DialogId = dialogId, Key = "q2", Text = "Q2", Type = QuestionType.FreeText, Order = 1 },
            },
            Transitions =
            {
                new Transition { Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = q1, TargetQuestionId = q2, Priority = 0, IsDefault = true },
                new Transition { Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = q2, TargetQuestionId = q1, Priority = 0, IsDefault = true },
            },
            Loops =
            {
                new LoopDefinition { Id = Guid.NewGuid(), DialogId = dialogId, CollectionKey = "a", EntryQuestionId = q1, BreakingQuestionId = q2 },
                new LoopDefinition { Id = Guid.NewGuid(), DialogId = dialogId, CollectionKey = "b", EntryQuestionId = q1, BreakingQuestionId = q2 },
            },
        };

        Assert.Throws<InvalidOperationException>(() => new LoopResolver(dialog));
    }

    /// <summary>The constructor rejects a <c>null</c> dialog.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_dialog()
        => Assert.Throws<ArgumentNullException>(() => new LoopResolver(null!));

    private static Dialog SingleQuestionLoopDialog(Guid questionId)
    {
        var dialogId = Guid.NewGuid();
        return new Dialog
        {
            Id = dialogId, Key = "single", Name = "Single", Version = 1, IsPublished = true,
            StartQuestionId = questionId, CreatedAt = TestDialogFactory.SampleTime, UpdatedAt = TestDialogFactory.SampleTime,
            Questions =
            {
                new Question { Id = questionId, DialogId = dialogId, Key = "q", Text = "Q", Type = QuestionType.FreeText, Order = 0 },
            },
            Transitions =
            {
                new Transition { Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = questionId, TargetQuestionId = questionId, Priority = 0, IsDefault = true },
            },
            Loops =
            {
                new LoopDefinition { Id = Guid.NewGuid(), DialogId = dialogId, CollectionKey = "items", EntryQuestionId = questionId, BreakingQuestionId = questionId },
            },
        };
    }
}
