using Flirty.Domain;

namespace Flirty.Tests.Domain;

/// <summary>
/// Verifies the domain model from issue #17: the enum values (incl. pinned ordinals as a guard
/// against an accidental shift of the later DB storage) as well as the construction of the
/// aggregate graphs (dialog and session aggregate) over their navigations.
/// </summary>
public class DomainModelTests
{
    [Fact]
    public void QuestionType_has_the_expected_values()
    {
        Assert.Equal(0, (int)QuestionType.SingleChoice);
        Assert.Equal(1, (int)QuestionType.MultiChoice);
        Assert.Equal(2, (int)QuestionType.FreeText);
        Assert.Equal(3, (int)QuestionType.Number);
        Assert.Equal(4, (int)QuestionType.Date);
        Assert.Equal(5, (int)QuestionType.Boolean);
        Assert.Equal(6, Enum.GetValues<QuestionType>().Length);
    }

    [Fact]
    public void TriggerScope_has_the_expected_values()
    {
        Assert.Equal(0, (int)TriggerScope.OnDialogStarted);
        Assert.Equal(1, (int)TriggerScope.AfterAnswer);
        Assert.Equal(2, (int)TriggerScope.AfterQuestion);
        Assert.Equal(3, (int)TriggerScope.OnDialogCompleted);
        Assert.Equal(4, Enum.GetValues<TriggerScope>().Length);
    }

    [Fact]
    public void TriggerKind_has_the_expected_values()
    {
        Assert.Equal(0, (int)TriggerKind.InProcess);
        Assert.Equal(1, (int)TriggerKind.Webhook);
        Assert.Equal(2, Enum.GetValues<TriggerKind>().Length);
    }

    [Fact]
    public void LayoutElementKind_has_the_expected_values()
    {
        Assert.Equal(0, (int)LayoutElementKind.Question);
        Assert.Single(Enum.GetValues<LayoutElementKind>());
    }

    [Fact]
    public void SessionStatus_has_the_expected_values()
    {
        Assert.Equal(0, (int)SessionStatus.InProgress);
        Assert.Equal(1, (int)SessionStatus.Completed);
        Assert.Equal(2, (int)SessionStatus.Abandoned);
        Assert.Equal(3, Enum.GetValues<SessionStatus>().Length);
    }

    [Fact]
    public void Dialog_aggregate_can_be_built_over_its_navigations()
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            Key = "role",
            Text = "Which role?",
            Type = QuestionType.SingleChoice,
            Order = 0,
            IsRequired = true,
            Options =
            {
                new AnswerOption { Id = Guid.NewGuid(), Key = "dev", Label = "Developer", Value = "dev", Order = 0 },
            },
        };

        var dialog = new Dialog
        {
            Id = Guid.NewGuid(),
            Key = "onboarding",
            Name = "Onboarding",
            Version = 1,
            StartQuestionId = question.Id,
            Questions = { question },
        };

        var onlyQuestion = Assert.Single(dialog.Questions);
        Assert.Equal(dialog.StartQuestionId, onlyQuestion.Id);
        var onlyOption = Assert.Single(onlyQuestion.Options);
        Assert.Equal("dev", onlyOption.Value);
        // Optional / unset values are empty or default, as expected.
        Assert.Null(dialog.Description);
        Assert.False(dialog.IsPublished);
    }

    [Fact]
    public void Session_aggregate_holds_several_answers_per_question_one_per_iteration()
    {
        var questionId = Guid.NewGuid();
        var loopInstanceId = Guid.NewGuid();

        var session = new DialogSession
        {
            Id = Guid.NewGuid(),
            DialogId = Guid.NewGuid(),
            DialogVersion = 1,
            ExternalUserKey = "user-42",
            Status = SessionStatus.InProgress,
            CurrentQuestionId = questionId,
            Answers =
            {
                new SessionAnswer
                {
                    Id = Guid.NewGuid(), QuestionId = questionId, Value = "\"A\"",
                    Sequence = 0, LoopInstanceId = loopInstanceId, IterationIndex = 0,
                },
                new SessionAnswer
                {
                    Id = Guid.NewGuid(), QuestionId = questionId, Value = "\"B\"",
                    Sequence = 1, LoopInstanceId = loopInstanceId, IterationIndex = 1,
                },
            },
        };

        // Two answers to the same question, told apart by the iteration index.
        Assert.Equal(2, session.Answers.Count);
        Assert.All(session.Answers, answer => Assert.Equal(questionId, answer.QuestionId));
        Assert.Equal([0, 1], session.Answers.Select(answer => answer.IterationIndex).Order());
        Assert.Null(session.CompletedAt);
    }
}
