using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Reine Unit-Tests des <see cref="LoopResolver"/> (Issue #29) ohne Datenbank: Body-Ermittlung inkl.
/// Ein-Fragen-Loop und Überlappungs-Ablehnung, die Iterations-/Instanz-Zuordnung beim Persistieren, der
/// Aufbau der je Iteration gesammelten Collections und der aktuelle Iterationsindex.
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

    // ---- Iterations-/Instanz-Zuordnung ------------------------------------------------------

    /// <summary>Der erste Eintritt in die Schleife startet eine frische Instanz mit Iteration 0.</summary>
    [Fact]
    public void ResolveAssignment_erster_Eintritt_startet_neue_Instanz_bei_Iteration_null()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);

        var assignment = resolver.ResolveAssignment(NewSession(), ids.PositionQuestionId);

        Assert.NotNull(assignment.LoopInstanceId);
        Assert.NotEqual(Guid.Empty, assignment.LoopInstanceId!.Value);
        Assert.Equal(0, assignment.IterationIndex);
    }

    /// <summary>Eine Folgefrage derselben Iteration behält Instanz und Iterationsindex.</summary>
    [Fact]
    public void ResolveAssignment_Folgefrage_behaelt_Instanz_und_Iteration()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);
        var instanceId = Guid.NewGuid();
        var session = NewSession(Answer(ids.PositionQuestionId, "\"A\"", 0, instanceId, 0));

        var assignment = resolver.ResolveAssignment(session, ids.MoreQuestionId);

        Assert.Equal(instanceId, assignment.LoopInstanceId);
        Assert.Equal(0, assignment.IterationIndex);
    }

    /// <summary>Das erneute Beantworten der Einstiegsfrage (Loop-Back) erhöht den Iterationsindex.</summary>
    [Fact]
    public void ResolveAssignment_Loop_Back_erhoeht_Iterationsindex()
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

    /// <summary>Eine Frage außerhalb jeder Schleife erhält keine Loop-Felder.</summary>
    [Fact]
    public void ResolveAssignment_Nicht_Loop_Frage_liefert_null()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var resolver = new LoopResolver(dialog);

        var assignment = resolver.ResolveAssignment(NewSession(), ids.SummaryQuestionId);

        Assert.Null(assignment.LoopInstanceId);
        Assert.Null(assignment.IterationIndex);
    }

    /// <summary>Ein Ein-Fragen-Loop (Entry == Breaking) zählt bei jeder Wiederbeantwortung hoch.</summary>
    [Fact]
    public void ResolveAssignment_Ein_Fragen_Loop_zaehlt_hoch()
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

    /// <summary>Der Collection-Key wird auch ohne bisherige Antwort (leer) gebunden.</summary>
    [Fact]
    public void BuildCollections_bindet_Key_auch_ohne_Antworten()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        var resolver = new LoopResolver(dialog);

        var collections = resolver.BuildCollections(NewSession());

        Assert.True(collections.ContainsKey("positions"));
        Assert.Empty(collections["positions"]);
    }

    /// <summary>Die Collection sammelt die Einstiegsantwort je Iteration in Iterationsreihenfolge.</summary>
    [Fact]
    public void BuildCollections_sammelt_Entry_Werte_je_Iteration()
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

    // ---- Iterationsindex --------------------------------------------------------------------

    /// <summary>Der Iterationsindex spiegelt die jüngste Antwort auf die Frage; außerhalb der Schleife ist er null.</summary>
    [Fact]
    public void ResolveIterationIndex_liefert_aktuelle_Iteration_und_null_ausserhalb()
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

    // ---- Konstruktor ------------------------------------------------------------------------

    /// <summary>Überlappende Schleifen-Bereiche werden im Konstruktor abgelehnt (Nesting out of scope).</summary>
    [Fact]
    public void Konstruktor_wirft_bei_ueberlappenden_Loops()
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

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Dialog ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Dialog()
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
