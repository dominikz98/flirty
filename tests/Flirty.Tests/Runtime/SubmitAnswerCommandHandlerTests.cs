using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert den <see cref="SubmitAnswerCommandHandler"/> (Issue #26) gegen eine echte SQLite-Datenbank
/// (in-memory): Persistenz der Antwort, Branching über den <see cref="IExpressionEvaluator"/> (bedingter
/// und Default-Übergang), fortlaufende <see cref="SessionAnswer.Sequence"/>, den Abschluss an einer
/// terminalen Frage sowie die Fehlerfälle (unbekannte/abgeschlossene Session, falsche Frage,
/// fehlkonfiguriertes Branching, <c>null</c>-Abhängigkeiten).
/// </summary>
public sealed class SubmitAnswerCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public SubmitAnswerCommandHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>Schließt die Verbindung und verwirft damit die in-memory-Datenbank.</summary>
    public void Dispose() => _connection.Dispose();

    private FlirtyDbContext CreateContext() => new(_options);

    private static SubmitAnswerCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());

    private static SubmitAnswerCommandHandler CreateHandler(FlirtyDbContext context, IPublisher publisher)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), publisher);

    /// <summary>Legt den Branching-Dialog samt einer laufenden Session an der Start-Frage an.</summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedBranchingSession(string externalUserKey = "user-1")
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        BranchingDialogIds ids;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = externalUserKey,
            Status = SessionStatus.InProgress,
            CurrentQuestionId = ids.RoleQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    // ---- Persistenz -------------------------------------------------------------------------

    /// <summary>Eine Antwort wird als <see cref="SessionAnswer"/> (Wert, Sequenz, Zeitpunkt) persistiert.</summary>
    [Fact]
    public async Task Handle_persistiert_Antwort()
    {
        var (sessionId, ids) = SeedBranchingSession();

        using (var act = CreateContext())
        {
            await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        var answer = Assert.Single(session.Answers);
        Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
        Assert.Equal("\"dev\"", answer.Value);
        Assert.Equal(0, answer.Sequence);
        Assert.NotEqual(default, answer.AnsweredAt);
    }

    /// <summary>Über mehrere Antworten hinweg wird die <see cref="SessionAnswer.Sequence"/> fortgeschrieben.</summary>
    [Fact]
    public async Task Handle_schreibt_Sequence_fortlaufend()
    {
        var (sessionId, ids) = SeedBranchingSession();

        using (var first = CreateContext())
        {
            await CreateHandler(first).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        // Nach dem ersten Submit steht die Session auf devDetail (terminal) – zweiter Submit schließt ab.
        using (var second = CreateContext())
        {
            await CreateHandler(second).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal([0, 1], session.Answers.OrderBy(a => a.Sequence).Select(a => a.Sequence));
    }

    // ---- Branching --------------------------------------------------------------------------

    /// <summary>Trifft der bedingte Ausdruck zu, schaltet die Session auf dessen Zielfrage weiter.</summary>
    [Fact]
    public async Task Handle_bedingter_Uebergang_fuehrt_zur_Zielfrage()
    {
        var (sessionId, ids) = SeedBranchingSession();

        SubmitAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.DevQuestionId, result.NextQuestion.Id);
        Assert.Equal("devDetail", result.NextQuestion.Key);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(ids.DevQuestionId, session.CurrentQuestionId);
        Assert.Equal(SessionStatus.InProgress, session.Status);
    }

    /// <summary>Trifft kein bedingter Übergang zu, greift der Default-Übergang.</summary>
    [Fact]
    public async Task Handle_ohne_Treffer_greift_Default_Uebergang()
    {
        var (sessionId, ids) = SeedBranchingSession();

        SubmitAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.PmQuestionId, result.NextQuestion.Id);
    }

    // ---- Completion -------------------------------------------------------------------------

    /// <summary>Eine terminale Frage (ohne ausgehende Übergänge) schließt den Dialog ab.</summary>
    [Fact]
    public async Task Handle_terminale_Frage_schliesst_Dialog_ab()
    {
        var (sessionId, ids) = SeedBranchingSession();

        using (var first = CreateContext())
        {
            await CreateHandler(first).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        SubmitAnswerResult result;
        using (var second = CreateContext())
        {
            result = await CreateHandler(second).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default);
        }

        Assert.True(result.IsCompleted);
        Assert.Null(result.NextQuestion);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.Null(session.CurrentQuestionId);
    }

    // ---- Fehlerfälle ------------------------------------------------------------------------

    /// <summary>Eine unbekannte Session führt zu einer <see cref="SessionNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_unbekannte_Session_wirft_SessionNotFoundException()
    {
        var unknownSession = Guid.NewGuid();
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<SessionNotFoundException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(unknownSession, Guid.NewGuid(), "\"x\""), default));

        Assert.Equal(unknownSession, exception.SessionId);
    }

    /// <summary>Eine nicht mehr laufende Session nimmt keine Antworten an.</summary>
    [Fact]
    public async Task Handle_abgeschlossene_Session_wirft_InvalidOperationException()
    {
        var (sessionId, ids) = SeedBranchingSession();
        using (var complete = CreateContext())
        {
            var session = complete.DialogSessions.Single(s => s.Id == sessionId);
            session.Status = SessionStatus.Completed;
            complete.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default));
    }

    /// <summary>Eine andere als die aktuell offene Frage wird abgelehnt (Editieren ist #28).</summary>
    [Fact]
    public async Task Handle_falsche_Frage_wirft_InvalidOperationException()
    {
        var (sessionId, ids) = SeedBranchingSession();
        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default));
    }

    /// <summary>Vorhandene Übergänge ohne Treffer und ohne Default gelten als Fehlkonfiguration.</summary>
    [Fact]
    public async Task Handle_kein_Treffer_ohne_Default_wirft_InvalidOperationException()
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(new Dialog
            {
                Id = dialogId, Key = "deadend", Name = "Dead End", Version = 1, IsPublished = true,
                StartQuestionId = questionId, CreatedAt = TestDialogFactory.SampleTime,
                UpdatedAt = TestDialogFactory.SampleTime,
                Questions =
                {
                    new Question
                    {
                        Id = questionId, DialogId = dialogId, Key = "q", Text = "Frage?",
                        Type = QuestionType.FreeText, Order = 0,
                    },
                },
                Transitions =
                {
                    new Transition
                    {
                        Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = questionId,
                        Expression = "q == \"never\"", TargetQuestionId = Guid.NewGuid(),
                        Priority = 0, IsDefault = false,
                    },
                },
            });
            arrange.DialogSessions.Add(new DialogSession
            {
                Id = sessionId, DialogId = dialogId, DialogVersion = 1, ExternalUserKey = "user-1",
                Status = SessionStatus.InProgress, CurrentQuestionId = questionId,
                StartedAt = TestDialogFactory.SampleTime,
            });
            arrange.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, questionId, "\"other\""), default));
    }

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Store ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Store()
        => Assert.Throws<ArgumentNullException>(
            () => new SubmitAnswerCommandHandler(null!, new DynamicExpressoExpressionEvaluator(), new SpyPublisher()));

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Evaluator ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Evaluator()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new SubmitAnswerCommandHandler(new DialogStore(context), null!, new SpyPublisher()));
    }

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Publisher ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Publisher()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new SubmitAnswerCommandHandler(
                new DialogStore(context), new DynamicExpressoExpressionEvaluator(), null!));
    }

    // ---- Trigger-Notifications --------------------------------------------------------------

    /// <summary>
    /// Beim Weiterschalten werden – in dieser Reihenfolge – <see cref="AnswerSubmittedNotification"/> und
    /// <see cref="QuestionAnsweredNotification"/> (mit der Folgefrage, nicht abgeschlossen) publiziert.
    /// </summary>
    [Fact]
    public async Task Handle_Weiterschalten_publiziert_AnswerSubmitted_und_QuestionAnswered()
    {
        var (sessionId, ids) = SeedBranchingSession();

        var spy = new SpyPublisher();
        using (var act = CreateContext())
        {
            await CreateHandler(act, spy).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.Collection(
            spy.Published,
            published =>
            {
                var answer = Assert.IsType<AnswerSubmittedNotification>(published);
                Assert.Equal(sessionId, answer.SessionId);
                Assert.Equal("branching", answer.DialogKey);
                Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
                Assert.Equal("\"dev\"", answer.Value);
            },
            published =>
            {
                var question = Assert.IsType<QuestionAnsweredNotification>(published);
                Assert.Equal(ids.RoleQuestionId, question.QuestionId);
                Assert.Equal(ids.DevQuestionId, question.NextQuestionId);
                Assert.False(question.IsCompleted);
            });
    }

    /// <summary>
    /// Beim Abschluss werden <see cref="AnswerSubmittedNotification"/>, eine abschließende
    /// <see cref="QuestionAnsweredNotification"/> und die <see cref="DialogCompletedNotification"/>
    /// (samt aller Antworten) publiziert.
    /// </summary>
    [Fact]
    public async Task Handle_Abschluss_publiziert_AnswerSubmitted_QuestionAnswered_und_DialogCompleted()
    {
        var (sessionId, ids) = SeedBranchingSession();
        using (var first = CreateContext())
        {
            await CreateHandler(first).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        var spy = new SpyPublisher();
        using (var second = CreateContext())
        {
            await CreateHandler(second, spy).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default);
        }

        Assert.Collection(
            spy.Published,
            published => Assert.IsType<AnswerSubmittedNotification>(published),
            published =>
            {
                var question = Assert.IsType<QuestionAnsweredNotification>(published);
                Assert.Null(question.NextQuestionId);
                Assert.True(question.IsCompleted);
            },
            published =>
            {
                var completed = Assert.IsType<DialogCompletedNotification>(published);
                Assert.Equal(sessionId, completed.SessionId);
                Assert.Equal("branching", completed.DialogKey);
                Assert.Equal(2, completed.Answers.Count);
            });
    }
}
