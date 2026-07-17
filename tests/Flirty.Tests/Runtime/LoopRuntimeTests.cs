using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert die Loop-Runtime (Issue #29) end-to-end über <see cref="SubmitAnswerCommandHandler"/> und
/// <see cref="EditAnswerCommandHandler"/> gegen eine echte SQLite-Datenbank (in-memory): die Zuordnung von
/// <see cref="SessionAnswer.LoopInstanceId"/>/<see cref="SessionAnswer.IterationIndex"/> über mehrere
/// Iterationen, das Verlassen des Zyklus über die Breaking Question, collection- und iterationsindex-
/// getriebene Break-Bedingungen sowie das gezielte Editieren einer Iteration.
/// </summary>
public sealed class LoopRuntimeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public LoopRuntimeTests()
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

    /// <summary>Legt den Loop-Dialog samt einer laufenden Session an der Einstiegsfrage an.</summary>
    private (Guid SessionId, LoopDialogIds Ids) SeedLoopSession(string loopBackExpression = "more == \"yes\"")
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        LoopDialogIds ids;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildLoopDialog(dialogId, out ids, loopBackExpression));
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = "user-1",
            Status = SessionStatus.InProgress,
            CurrentQuestionId = ids.PositionQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    /// <summary>Reicht eine Antwort über den <see cref="SubmitAnswerCommandHandler"/> in eigenem Kontext ein.</summary>
    private async Task<SubmitAnswerResult> SubmitAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new SubmitAnswerCommandHandler(new DialogStore(context), new DynamicExpressoExpressionEvaluator());
        return await handler.Handle(new SubmitAnswerCommand(sessionId, questionId, value), default);
    }

    /// <summary>Editiert eine Antwort über den <see cref="EditAnswerCommandHandler"/> in eigenem Kontext.</summary>
    private async Task<EditAnswerResult> EditAsync(Guid sessionId, Guid questionId, string value, int? iterationIndex = null)
    {
        using var context = CreateContext();
        var handler = new EditAnswerCommandHandler(new DialogStore(context), new DynamicExpressoExpressionEvaluator());
        return await handler.Handle(new EditAnswerCommand(sessionId, questionId, value, iterationIndex), default);
    }

    private DialogSession LoadSession(Guid sessionId)
    {
        using var context = CreateContext();
        return context.DialogSessions.Include(session => session.Answers).Single(session => session.Id == sessionId);
    }

    // ---- Mehrere Iterationen ----------------------------------------------------------------

    /// <summary>
    /// Zwei Durchläufe der Schleife weisen den Antworten dieselbe <see cref="SessionAnswer.LoopInstanceId"/>
    /// und aufsteigende Iterationsindizes (0, 1) je Frage zu; die <see cref="SessionAnswer.Sequence"/> läuft
    /// über alle Iterationen fort.
    /// </summary>
    [Fact]
    public async Task Handle_mehrere_Iterationen_weist_Instanz_und_Index_zu()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");   // Iteration 0
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");     // -> Loop-Back
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");   // Iteration 1
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");      // -> Exit

        var session = LoadSession(sessionId);

        var positions = session.Answers
            .Where(answer => answer.QuestionId == ids.PositionQuestionId)
            .OrderBy(answer => answer.Sequence)
            .ToList();
        var mores = session.Answers
            .Where(answer => answer.QuestionId == ids.MoreQuestionId)
            .OrderBy(answer => answer.Sequence)
            .ToList();

        Assert.Equal(["A", "B"], positions.Select(answer => answer.Value.Trim('"')));
        Assert.Equal([0, 1], positions.Select(answer => answer.IterationIndex));
        Assert.Equal([0, 1], mores.Select(answer => answer.IterationIndex));

        // Alle Loop-Antworten teilen dieselbe (nicht-null) Instanz-Id.
        var instanceIds = session.Answers
            .Where(answer => answer.LoopInstanceId is not null)
            .Select(answer => answer.LoopInstanceId!.Value)
            .Distinct()
            .ToList();
        Assert.Single(instanceIds);
        Assert.Equal([0, 1, 2, 3], session.Answers.OrderBy(answer => answer.Sequence).Select(answer => answer.Sequence));
    }

    // ---- Breaking Question ------------------------------------------------------------------

    /// <summary>
    /// Die Breaking Question verlässt den Zyklus (Exit-Übergang) auf die nachgelagerte Frage; die dort
    /// gegebene Antwort trägt keine Loop-Felder mehr und der Dialog schließt normal ab.
    /// </summary>
    [Fact]
    public async Task Handle_Breaking_Question_verlaesst_Zyklus_und_setzt_normalen_Fluss_fort()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");
        var afterBreak = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");   // -> Exit auf summary

        Assert.False(afterBreak.IsCompleted);
        Assert.NotNull(afterBreak.NextQuestion);
        Assert.Equal(ids.SummaryQuestionId, afterBreak.NextQuestion.Id);

        var completed = await SubmitAsync(sessionId, ids.SummaryQuestionId, "\"fertig\"");
        Assert.True(completed.IsCompleted);

        var session = LoadSession(sessionId);
        var summary = session.Answers.Single(answer => answer.QuestionId == ids.SummaryQuestionId);
        Assert.Null(summary.LoopInstanceId);
        Assert.Null(summary.IterationIndex);
        Assert.Equal(SessionStatus.Completed, session.Status);
    }

    // ---- Collection im Kontext --------------------------------------------------------------

    /// <summary>
    /// Eine collection-getriebene Break-Bedingung (<c>positions.Count &lt; 2</c>) sieht die je Iteration
    /// gesammelten Einstiegsantworten: die Schleife läuft, bis zwei Positionen erfasst sind, und verlässt
    /// den Zyklus dann ohne explizites „nein".
    /// </summary>
    [Fact]
    public async Task Break_Bedingung_sieht_gesammelte_Collection()
    {
        var (sessionId, ids) = SeedLoopSession(loopBackExpression: "positions.Count < 2");

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");
        var afterFirst = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");   // positions=[A] -> Count 1 < 2 -> Loop-Back
        Assert.Equal(ids.PositionQuestionId, afterFirst.NextQuestion!.Id);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");
        var afterSecond = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");  // positions=[A,B] -> Count 2 -> Exit
        Assert.Equal(ids.SummaryQuestionId, afterSecond.NextQuestion!.Id);

        var session = LoadSession(sessionId);
        var positions = session.Answers.Count(answer => answer.QuestionId == ids.PositionQuestionId);
        Assert.Equal(2, positions);
    }

    /// <summary>
    /// Eine iterationsindex-getriebene Break-Bedingung (<c>iterationIndex &lt; 1</c>) verlässt den Zyklus
    /// nach genau zwei Iterationen (Index 0 und 1).
    /// </summary>
    [Fact]
    public async Task Break_Bedingung_sieht_Iterationsindex()
    {
        var (sessionId, ids) = SeedLoopSession(loopBackExpression: "iterationIndex < 1");

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");
        var afterFirst = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");   // iterationIndex 0 < 1 -> Loop-Back
        Assert.Equal(ids.PositionQuestionId, afterFirst.NextQuestion!.Id);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");
        var afterSecond = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");  // iterationIndex 1 -> Exit
        Assert.Equal(ids.SummaryQuestionId, afterSecond.NextQuestion!.Id);
    }

    // ---- Edit in Iteration ------------------------------------------------------------------

    /// <summary>
    /// Das Editieren einer bestimmten Iteration (<c>IterationIndex: 1</c>) überschreibt gezielt deren
    /// Einstiegsantwort, verwirft die nachgelagerten Antworten und berechnet den Pfad neu.
    /// </summary>
    [Fact]
    public async Task Handle_Edit_in_Iteration_ueberschreibt_gezielt_und_invalidiert()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");   // Iteration 0 (seq 0)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");     // seq 1
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");   // Iteration 1 (seq 2)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");      // seq 3 -> summary

        var result = await EditAsync(sessionId, ids.PositionQuestionId, "\"B2\"", iterationIndex: 1);

        Assert.False(result.IsCompleted);
        Assert.Equal(ids.MoreQuestionId, result.NextQuestion!.Id);
        Assert.Equal(1, result.InvalidatedAnswers);   // nur more@Iteration1 (seq 3) liegt hinter position@Iteration1

        var session = LoadSession(sessionId);
        var positions = session.Answers
            .Where(answer => answer.QuestionId == ids.PositionQuestionId)
            .OrderBy(answer => answer.IterationIndex)
            .ToList();
        Assert.Equal(["A", "B2"], positions.Select(answer => answer.Value.Trim('"')));
        Assert.Equal([0, 1], positions.Select(answer => answer.IterationIndex));
    }

    /// <summary>
    /// Ohne <c>IterationIndex</c> editiert der Handler – rückwärtskompatibel – die früheste Antwort der
    /// Frage (Iteration 0) und verwirft alle nachgelagerten Iterationen.
    /// </summary>
    [Fact]
    public async Task Handle_Edit_ohne_IterationIndex_trifft_frueheste_Antwort()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");   // Iteration 0 (seq 0)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");     // seq 1
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");   // Iteration 1 (seq 2)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");      // seq 3

        var result = await EditAsync(sessionId, ids.PositionQuestionId, "\"A2\"");

        Assert.Equal(3, result.InvalidatedAnswers);   // more@0, position@1, more@1

        var session = LoadSession(sessionId);
        var positions = session.Answers.Where(answer => answer.QuestionId == ids.PositionQuestionId).ToList();
        var remaining = Assert.Single(positions);
        Assert.Equal("A2", remaining.Value.Trim('"'));
        Assert.Equal(0, remaining.IterationIndex);
    }

    /// <summary>Der Verweis auf eine nicht vorhandene Iteration beim Editieren wird abgelehnt.</summary>
    [Fact]
    public async Task Handle_Edit_nicht_existente_Iteration_wirft_InvalidOperationException()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await EditAsync(sessionId, ids.PositionQuestionId, "\"X\"", iterationIndex: 5));
    }
}
