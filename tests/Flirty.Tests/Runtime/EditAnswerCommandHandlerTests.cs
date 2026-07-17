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
/// Verifiziert den <see cref="EditAnswerCommandHandler"/> (Issue #28) gegen eine echte SQLite-Datenbank
/// (in-memory): das Überschreiben einer früheren Antwort, das Invalidieren der nachgelagerten Antworten,
/// die Pfad-Neuberechnung über das Branching (Zweigwechsel/gleicher Zweig), das Wieder-Öffnen einer
/// abgeschlossenen Session sowie die Fehlerfälle (unbekannte/abgebrochene Session, nicht beantwortete bzw.
/// fremde Frage, <c>null</c>-Abhängigkeiten).
/// </summary>
public sealed class EditAnswerCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public EditAnswerCommandHandlerTests()
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

    private static EditAnswerCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());

    private static EditAnswerCommandHandler CreateHandler(FlirtyDbContext context, IPublisher publisher)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), publisher);

    /// <summary>
    /// Legt den Branching-Dialog samt einer <b>abgeschlossenen</b> Session an, die den <c>dev</c>-Zweig
    /// vollständig durchlaufen hat: <c>role</c> = <c>"dev"</c> (Sequence 0) und <c>devDetail</c> = <c>"C#"</c>
    /// (Sequence 1). Grundlage für die Edit-/Invalidierungs-/Reopen-Fälle.
    /// </summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedCompletedDevSession(string externalUserKey = "user-1")
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
            Status = SessionStatus.Completed,
            CurrentQuestionId = null,
            StartedAt = TestDialogFactory.SampleTime,
            CompletedAt = TestDialogFactory.SampleTime,
            Answers =
            {
                new SessionAnswer
                {
                    Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.RoleQuestionId,
                    Value = "\"dev\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
                },
                new SessionAnswer
                {
                    Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.DevQuestionId,
                    Value = "\"C#\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 1,
                },
            },
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    // ---- Überschreiben ----------------------------------------------------------------------

    /// <summary>Die editierte Antwort wird überschrieben; Wert und Zeitpunkt ändern sich, die Sequence bleibt.</summary>
    [Fact]
    public async Task Handle_ueberschreibt_Antwort()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        using (var act = CreateContext())
        {
            await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        var role = session.Answers.Single(a => a.QuestionId == ids.RoleQuestionId);
        Assert.Equal("\"pm\"", role.Value);
        Assert.Equal(0, role.Sequence);
        Assert.NotEqual(TestDialogFactory.SampleTime, role.AnsweredAt);
    }

    // ---- Invalidierung ----------------------------------------------------------------------

    /// <summary>Nachgelagerte Antworten werden verworfen; nur Antworten bis zur editierten Frage bleiben.</summary>
    [Fact]
    public async Task Handle_invalidiert_nachgelagerte_Antworten()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        Assert.Equal(1, result.InvalidatedAnswers);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        var answer = Assert.Single(session.Answers);
        Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
    }

    // ---- Pfad-Neuberechnung -----------------------------------------------------------------

    /// <summary>Eine geänderte Auswahl führt über das Branching zu einem anderen Zweig (neue Folgefrage).</summary>
    [Fact]
    public async Task Handle_geaenderte_Auswahl_fuehrt_zu_neuem_Zweig()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.PmQuestionId, result.NextQuestion.Id);
        Assert.Equal("pmDetail", result.NextQuestion.Key);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(ids.PmQuestionId, session.CurrentQuestionId);
    }

    /// <summary>Bleibt der Zweig gleich, wird auf dieselbe Folgefrage gesetzt – die nachgelagerte Antwort trotzdem verworfen.</summary>
    [Fact]
    public async Task Handle_gleicher_Wert_setzt_auf_gleiche_Folgefrage()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.DevQuestionId, result.NextQuestion.Id);
        Assert.Equal(1, result.InvalidatedAnswers);
    }

    // ---- Session-Status ---------------------------------------------------------------------

    /// <summary>Eine abgeschlossene Session wird bei nicht-terminaler Neuberechnung wieder geöffnet.</summary>
    [Fact]
    public async Task Handle_reoeffnet_abgeschlossene_Session()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        using (var act = CreateContext())
        {
            await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Null(session.CompletedAt);
        Assert.Equal(ids.PmQuestionId, session.CurrentQuestionId);
    }

    /// <summary>Wird die terminale Frage editiert, bleibt der Dialog abgeschlossen (keine Invalidierung).</summary>
    [Fact]
    public async Task Handle_edit_terminale_Frage_bleibt_abgeschlossen()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.DevQuestionId, "\"Rust\""), default);
        }

        Assert.True(result.IsCompleted);
        Assert.Null(result.NextQuestion);
        Assert.Equal(0, result.InvalidatedAnswers);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.Null(session.CurrentQuestionId);
        Assert.Equal("\"Rust\"", session.Answers.Single(a => a.QuestionId == ids.DevQuestionId).Value);
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
                new EditAnswerCommand(unknownSession, Guid.NewGuid(), "\"x\""), default));

        Assert.Equal(unknownSession, exception.SessionId);
    }

    /// <summary>Eine noch nicht beantwortete Frage kann nicht editiert werden.</summary>
    [Fact]
    public async Task Handle_nicht_beantwortete_Frage_wirft_InvalidOperationException()
    {
        // Die Session lief über den dev-Zweig – pmDetail wurde nie beantwortet.
        var (sessionId, ids) = SeedCompletedDevSession();
        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.PmQuestionId, "\"x\""), default));
    }

    /// <summary>Eine abgebrochene Session kann nicht editiert werden.</summary>
    [Fact]
    public async Task Handle_abgebrochene_Session_wirft_InvalidOperationException()
    {
        var (sessionId, ids) = SeedCompletedDevSession();
        using (var abandon = CreateContext())
        {
            var session = abandon.DialogSessions.Single(s => s.Id == sessionId);
            session.Status = SessionStatus.Abandoned;
            abandon.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default));
    }

    /// <summary>Eine nicht zum Dialog gehörende Frage wird abgelehnt.</summary>
    [Fact]
    public async Task Handle_fremde_Frage_wirft_InvalidOperationException()
    {
        var (sessionId, _) = SeedCompletedDevSession();
        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, Guid.NewGuid(), "\"x\""), default));
    }

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Store ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Store()
        => Assert.Throws<ArgumentNullException>(
            () => new EditAnswerCommandHandler(null!, new DynamicExpressoExpressionEvaluator(), new SpyPublisher()));

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Evaluator ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Evaluator()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new EditAnswerCommandHandler(new DialogStore(context), null!, new SpyPublisher()));
    }

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Publisher ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Publisher()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new EditAnswerCommandHandler(
                new DialogStore(context), new DynamicExpressoExpressionEvaluator(), null!));
    }

    // ---- Trigger-Notifications --------------------------------------------------------------

    /// <summary>
    /// Editiert eine terminale Frage, sodass die Neuberechnung erneut abschließt: es wird genau eine
    /// <see cref="DialogCompletedNotification"/> (mit den Antworten) publiziert.
    /// </summary>
    [Fact]
    public async Task Handle_Abschluss_publiziert_DialogCompleted()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        var spy = new SpyPublisher();
        using (var act = CreateContext())
        {
            await CreateHandler(act, spy).Handle(
                new EditAnswerCommand(sessionId, ids.DevQuestionId, "\"Rust\""), default);
        }

        var notification = Assert.IsType<DialogCompletedNotification>(Assert.Single(spy.Published));
        Assert.Equal(sessionId, notification.SessionId);
        Assert.Equal("branching", notification.DialogKey);
        Assert.Equal(2, notification.Answers.Count);
    }

    /// <summary>
    /// Führt die Neuberechnung auf eine nicht-terminale Folgefrage (Wieder-Öffnen), wird bewusst keine
    /// Notification publiziert.
    /// </summary>
    [Fact]
    public async Task Handle_Reopen_publiziert_keine_Notification()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        var spy = new SpyPublisher();
        using (var act = CreateContext())
        {
            var result = await CreateHandler(act, spy).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
            Assert.False(result.IsCompleted);
        }

        Assert.Empty(spy.Published);
    }
}
