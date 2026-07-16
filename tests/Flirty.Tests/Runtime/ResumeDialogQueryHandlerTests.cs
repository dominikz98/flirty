using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert den <see cref="ResumeDialogQueryHandler"/> (Issue #27) gegen eine echte SQLite-Datenbank
/// (in-memory): das Lesen von Status, aktueller Frage und bisherigen Antworten einer laufenden Session,
/// die chronologische Ordnung der Antworten nach <see cref="SessionAnswer.Sequence"/>, das Verhalten bei
/// abgeschlossener Session (keine offene Frage) sowie die Fehlerfälle (unbekannte Session,
/// <c>null</c>-Store).
/// </summary>
public sealed class ResumeDialogQueryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public ResumeDialogQueryHandlerTests()
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

    private static ResumeDialogQueryHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context));

    // ---- Zustand lesen ----------------------------------------------------------------------

    /// <summary>
    /// Eine laufende Session liefert ihren Status, die aktuell offene Frage und die bisherigen Antworten
    /// (samt aufgelöstem fachlichen Frage-Schlüssel).
    /// </summary>
    [Fact]
    public async Task Handle_laufende_Session_liefert_Status_aktuelle_Frage_und_Antworten()
    {
        // Session steht (nach Antwort "dev" auf role) auf der Folgefrage devDetail.
        var (sessionId, ids) = SeedBranchingSession(
            SessionStatus.InProgress,
            selectCurrentQuestion: dialogIds => dialogIds.DevQuestionId,
            withDetailAnswer: false);

        ResumeDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(new ResumeDialogQuery(sessionId), default);
        }

        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(SessionStatus.InProgress, result.Status);
        Assert.NotNull(result.CurrentQuestion);
        Assert.Equal(ids.DevQuestionId, result.CurrentQuestion.Id);
        Assert.Equal("devDetail", result.CurrentQuestion.Key);

        var answer = Assert.Single(result.Answers);
        Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
        Assert.Equal("role", answer.QuestionKey);
        Assert.Equal("\"dev\"", answer.Value);
        Assert.Equal(0, answer.Sequence);
    }

    /// <summary>Die bisherigen Antworten werden aufsteigend nach <see cref="SessionAnswer.Sequence"/> geliefert.</summary>
    [Fact]
    public async Task Handle_antworten_sind_nach_Sequence_geordnet()
    {
        var (sessionId, _) = SeedBranchingSession(
            SessionStatus.Completed,
            selectCurrentQuestion: _ => null,
            withDetailAnswer: true,
            answersUnsorted: true);

        ResumeDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(new ResumeDialogQuery(sessionId), default);
        }

        Assert.Equal([0, 1], result.Answers.Select(answer => answer.Sequence));
        Assert.Equal(["\"dev\"", "\"C#\""], result.Answers.Select(answer => answer.Value));
    }

    /// <summary>
    /// Eine abgeschlossene Session liefert <c>null</c> als aktuelle Frage, den Status
    /// <see cref="SessionStatus.Completed"/> und dennoch alle bisherigen Antworten.
    /// </summary>
    [Fact]
    public async Task Handle_abgeschlossene_Session_liefert_null_CurrentQuestion()
    {
        var (sessionId, _) = SeedBranchingSession(
            SessionStatus.Completed,
            selectCurrentQuestion: _ => null,
            withDetailAnswer: true);

        ResumeDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(new ResumeDialogQuery(sessionId), default);
        }

        Assert.Equal(SessionStatus.Completed, result.Status);
        Assert.Null(result.CurrentQuestion);
        Assert.Equal(2, result.Answers.Count);
    }

    // ---- Fehlerfälle ------------------------------------------------------------------------

    /// <summary>Eine unbekannte Session führt zu einer <see cref="SessionNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_unbekannte_Session_wirft_SessionNotFoundException()
    {
        var unknownSession = Guid.NewGuid();
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<SessionNotFoundException>(
            async () => await CreateHandler(act).Handle(new ResumeDialogQuery(unknownSession), default));

        Assert.Equal(unknownSession, exception.SessionId);
    }

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Store ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Store()
        => Assert.Throws<ArgumentNullException>(() => new ResumeDialogQueryHandler(null!));

    // ---- Testdaten-Helfer -------------------------------------------------------------------

    /// <summary>
    /// Legt den Branching-Dialog samt einer Session im angegebenen Zustand an. Die aktuell offene Frage
    /// wird über <paramref name="selectCurrentQuestion"/> aus den Dialog-Frage-Ids gewählt. Es wird stets
    /// die <c>role</c>-Antwort (Sequenz 0) angehängt; mit <paramref name="withDetailAnswer"/> zusätzlich
    /// die <c>devDetail</c>-Antwort (Sequenz 1). <paramref name="answersUnsorted"/> kehrt die
    /// Einfüge-Reihenfolge um, um die Sortierung im Handler zu prüfen.
    /// </summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedBranchingSession(
        SessionStatus status,
        Func<BranchingDialogIds, Guid?> selectCurrentQuestion,
        bool withDetailAnswer,
        bool answersUnsorted = false)
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        BranchingDialogIds ids;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));

        var session = new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = "user-1",
            Status = status,
            CurrentQuestionId = selectCurrentQuestion(ids),
            StartedAt = TestDialogFactory.SampleTime,
            CompletedAt = status == SessionStatus.Completed ? TestDialogFactory.SampleTime : null,
        };

        var role = new SessionAnswer
        {
            Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.RoleQuestionId,
            Value = "\"dev\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
        };
        var detail = new SessionAnswer
        {
            Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.DevQuestionId,
            Value = "\"C#\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 1,
        };

        if (withDetailAnswer && answersUnsorted)
        {
            session.Answers.Add(detail);
            session.Answers.Add(role);
        }
        else
        {
            session.Answers.Add(role);
            if (withDetailAnswer)
            {
                session.Answers.Add(detail);
            }
        }

        arrange.DialogSessions.Add(session);
        arrange.SaveChanges();

        return (sessionId, ids);
    }
}
