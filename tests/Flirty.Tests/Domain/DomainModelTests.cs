using Flirty.Domain;

namespace Flirty.Tests.Domain;

/// <summary>
/// Verifiziert das Domänenmodell aus Issue #17: die Enum-Werte (inkl. gepinnter Ordinalwerte
/// als Guard gegen eine versehentliche Verschiebung des späteren DB-Storages) sowie die
/// Konstruktion der Aggregat-Graphen (Dialog- und Session-Aggregat) über ihre Navigationen.
/// </summary>
public class DomainModelTests
{
    [Fact]
    public void QuestionType_hat_erwartete_Werte()
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
    public void TriggerScope_hat_erwartete_Werte()
    {
        Assert.Equal(0, (int)TriggerScope.OnDialogStarted);
        Assert.Equal(1, (int)TriggerScope.AfterAnswer);
        Assert.Equal(2, (int)TriggerScope.AfterQuestion);
        Assert.Equal(3, (int)TriggerScope.OnDialogCompleted);
        Assert.Equal(4, Enum.GetValues<TriggerScope>().Length);
    }

    [Fact]
    public void TriggerKind_hat_erwartete_Werte()
    {
        Assert.Equal(0, (int)TriggerKind.InProcess);
        Assert.Equal(1, (int)TriggerKind.Webhook);
        Assert.Equal(2, Enum.GetValues<TriggerKind>().Length);
    }

    [Fact]
    public void SessionStatus_hat_erwartete_Werte()
    {
        Assert.Equal(0, (int)SessionStatus.InProgress);
        Assert.Equal(1, (int)SessionStatus.Completed);
        Assert.Equal(2, (int)SessionStatus.Abandoned);
        Assert.Equal(3, Enum.GetValues<SessionStatus>().Length);
    }

    [Fact]
    public void Dialog_Aggregat_laesst_sich_ueber_Navigationen_aufbauen()
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            Key = "role",
            Text = "Welche Rolle?",
            Type = QuestionType.SingleChoice,
            Order = 0,
            IsRequired = true,
            Options =
            {
                new AnswerOption { Id = Guid.NewGuid(), Key = "dev", Label = "Entwickler", Value = "dev", Order = 0 },
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
        // Optionale/nicht gesetzte Werte sind wie erwartet leer bzw. Default.
        Assert.Null(dialog.Description);
        Assert.False(dialog.IsPublished);
    }

    [Fact]
    public void Session_Aggregat_haelt_mehrere_Antworten_pro_Frage_je_Iteration()
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

        // Zwei Antworten auf dieselbe Frage, unterschieden über den Iterationsindex.
        Assert.Equal(2, session.Answers.Count);
        Assert.All(session.Answers, answer => Assert.Equal(questionId, answer.QuestionId));
        Assert.Equal([0, 1], session.Answers.Select(answer => answer.IterationIndex).Order());
        Assert.Null(session.CompletedAt);
    }
}
