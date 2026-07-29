using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;
using Flirty.Tests.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für den <see cref="GraphRunAnalyzer"/> – den Laufzustand über dem Graphen (#104).
/// </summary>
/// <remarks>
/// <para>
/// Gespielt wird mit der <b>echten Engine</b> (dieselben Handler, die der Runner über die Gateways ruft),
/// weil genau das den Kern der Ableitung prüft: Der Pfad steht nirgends gespeichert, sondern entsteht aus
/// der Antwortfolge. Ein handgebauter <see cref="ResumeDialogResult"/> würde die Erwartung nur wiederholen.
/// </para>
/// <para>
/// Die Trigger-Zuordnung ist der eine Fall, der ohne Engine geprüft wird: Sie hängt am
/// <c>DesignerTriggerLog</c>, dessen Einträge im Test direkt gestellt werden können.
/// </para>
/// </remarks>
public sealed class GraphRunAnalyzerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen) und
    /// erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public GraphRunAnalyzerTests()
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

    /// <summary>
    /// Der Kern des Akzeptanzkriteriums: besuchte Knoten, die offene Frage und die tatsächlich
    /// gegriffenen Kanten – und ausdrücklich <b>nicht</b> die nicht gegriffenen.
    /// </summary>
    [Fact]
    public async Task Build_hebt_besuchte_Knoten_die_offene_Frage_und_gegriffene_Kanten_hervor()
    {
        var (sessionId, ids) = SeedLoopSession();
        var detail = LoadDetail(sessionId);

        // Direkt nach dem Start: die Einstiegsfrage ist offen, nichts ist beantwortet, nichts gegriffen.
        var started = BuildOverlay(sessionId);
        var entry = Assert.Single(started.Visits);
        Assert.Equal(ids.PositionQuestionId, entry.QuestionId);
        Assert.True(entry.IsCurrent);
        Assert.Empty(entry.Answers);
        Assert.Empty(started.TakenEdges);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Backend\"");

        var overlay = BuildOverlay(sessionId);

        Assert.Equal(ids.MoreQuestionId, overlay.CurrentQuestionId);
        Assert.False(overlay.Visit(ids.PositionQuestionId)!.IsCurrent);
        Assert.True(overlay.Visit(ids.MoreQuestionId)!.IsCurrent);
        Assert.Null(overlay.Visit(ids.SummaryQuestionId));
        Assert.Equal(1, overlay.Steps);

        // Der lesbare Wert kommt aus dem AnswerValueCodec, der Rohwert bleibt daneben stehen.
        var answer = Assert.Single(overlay.Visit(ids.PositionQuestionId)!.Answers);
        Assert.Equal("Backend", answer.Display);
        Assert.Equal("\"Backend\"", answer.Value);

        // Die Einstiegsfrage liegt im Schleifen-Bereich, die Engine vergibt also schon in der ERSTEN
        // Runde einen Iterationsindex – der Knoten trägt darum von Anfang an „Iteration 1".
        Assert.Equal(0, answer.IterationIndex);

        // Gegriffen hat genau der Übergang position → more.
        var taken = Assert.Single(overlay.TakenEdges);
        Assert.Equal(TransitionOf(detail, ids.PositionQuestionId, ids.MoreQuestionId).Id, taken.TransitionId);
        Assert.Equal(1, taken.Count);
        Assert.False(taken.IsAmbiguous);

        // Der Rücksprung und der Ausstieg sind nicht gegriffen – die Kante zeigt das, nicht der Graph.
        Assert.Null(overlay.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.PositionQuestionId).Id));
        Assert.Null(overlay.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.SummaryQuestionId).Id));
    }

    /// <summary>
    /// Die Iterationszahl am Schleifenrahmen: zwei Durchläufe, danach der Ausstieg. Der Rücksprung ist
    /// eine <b>einmal</b> gegriffene Kante, die Schleife nach dem Ausstieg nicht mehr aktiv.
    /// </summary>
    [Fact]
    public async Task Build_zaehlt_die_Iterationen_der_Schleife_und_verlaesst_sie_wieder()
    {
        var (sessionId, ids) = SeedLoopSession();
        var detail = LoadDetail(sessionId);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Backend\"");
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Frontend\"");

        var inLoop = BuildOverlay(sessionId);
        var running = Assert.Single(inLoop.Loops);
        Assert.Equal("positions", running.CollectionKey);
        Assert.Equal(2, running.Iterations);
        Assert.True(running.IsActive);
        Assert.Equal(
            [ids.PositionQuestionId, ids.MoreQuestionId],
            running.Body);

        // Der Knoten der Einstiegsfrage trägt beide Antworten – eine je Iteration.
        var visits = inLoop.Visit(ids.PositionQuestionId)!.Answers;
        Assert.Equal(2, visits.Count);
        Assert.Equal([0, 1], visits.Select(answer => answer.IterationIndex));

        var backJump = inLoop.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.PositionQuestionId).Id);
        Assert.Equal(1, backJump!.Count);

        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");

        var afterExit = BuildOverlay(sessionId);
        var left = Assert.Single(afterExit.Loops);
        Assert.Equal(2, left.Iterations);
        Assert.False(left.IsActive);
        Assert.NotNull(afterExit.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.SummaryQuestionId).Id));
        Assert.Equal(ids.SummaryQuestionId, afterExit.CurrentQuestionId);

        // Und die Zusammenfassung – für Screenreader die einzige Fassung der Hervorhebung.
        Assert.Contains("offene Frage summary", afterExit.Summary, StringComparison.Ordinal);
        Assert.Contains("positions: 2 Iterationen", afterExit.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Das Akzeptanzkriterium zum Editieren: Ein Edit rechnet den Pfad neu – <b>auch</b> wenn der neue
    /// Pfad einen anderen Zweig nimmt. Dafür braucht es keine eigene Logik: Der Pfad ist aus der
    /// Antwortfolge abgeleitet, und <c>EditAnswerCommand</c> verwirft die nachgelagerten Antworten.
    /// </summary>
    [Fact]
    public async Task Build_rechnet_den_Pfad_nach_einem_Edit_neu_und_wechselt_den_Zweig()
    {
        var (sessionId, ids) = SeedBranchingSession();
        var detail = LoadDetail(sessionId);
        var toDev = TransitionOf(detail, ids.RoleQuestionId, ids.DevQuestionId);
        var toPm = TransitionOf(detail, ids.RoleQuestionId, ids.PmQuestionId);

        await SubmitAsync(sessionId, ids.RoleQuestionId, "\"dev\"");
        await SubmitAsync(sessionId, ids.DevQuestionId, "\"C#\"");

        var devPath = BuildOverlay(sessionId);
        Assert.NotNull(devPath.Edge(toDev.Id));
        Assert.Null(devPath.Edge(toPm.Id));
        Assert.NotNull(devPath.Visit(ids.DevQuestionId));
        Assert.Equal("Entwickler", devPath.Visit(ids.RoleQuestionId)!.Answers[0].Display);

        await EditAsync(sessionId, ids.RoleQuestionId, "\"pm\"");

        var pmPath = BuildOverlay(sessionId);

        // Der alte Zweig ist weg – Kante UND Knoten.
        Assert.Null(pmPath.Edge(toDev.Id));
        Assert.Null(pmPath.Visit(ids.DevQuestionId));

        Assert.NotNull(pmPath.Edge(toPm.Id));
        Assert.Equal(ids.PmQuestionId, pmPath.CurrentQuestionId);
        Assert.Equal("Product Manager", pmPath.Visit(ids.RoleQuestionId)!.Answers[0].Display);
        Assert.Equal(1, pmPath.Steps);
    }

    /// <summary>
    /// Mehrere Übergänge zwischen denselben zwei Fragen sind <b>nicht</b> unterscheidbar: Die Engine hält
    /// nicht fest, welcher gegriffen hat. Dann sind alle markiert und alle als mehrdeutig ausgewiesen –
    /// einen davon zu behaupten wäre eine Erfindung.
    /// </summary>
    [Fact]
    public async Task Build_weist_parallele_Uebergaenge_als_mehrdeutig_aus()
    {
        // Ein zweiter Übergang mit derselben Bedingung auf dasselbe Ziel: Zur Laufzeit gewinnt der mit
        // der niedrigeren Priorität, aber die Antwortfolge kennt nur das Fragenpaar.
        var (sessionId, ids) = SeedBranchingSession((dialog, graph) => dialog.Transitions.Add(new Transition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            FromQuestionId = graph.RoleQuestionId,
            TargetQuestionId = graph.DevQuestionId,
            Expression = "role == \"dev\"",
            Priority = 2,
        }));

        await SubmitAsync(sessionId, ids.RoleQuestionId, "\"dev\"");

        var overlay = BuildOverlay(sessionId);
        var detail = LoadDetail(sessionId);

        Assert.Equal(2, overlay.TakenEdges.Count);
        Assert.All(overlay.TakenEdges, edge => Assert.True(edge.IsAmbiguous));
        Assert.All(
            overlay.TakenEdges,
            edge =>
            {
                var transition = detail.Transitions.Single(candidate => candidate.Id == edge.TransitionId);
                Assert.Equal(ids.RoleQuestionId, transition.FromQuestionId);
                Assert.Equal(ids.DevQuestionId, transition.TargetQuestionId);
            });

        // Der Default auf den anderen Zweig bleibt unmarkiert – mehrdeutig heißt nicht „alles an".
        Assert.Null(overlay.Edge(TransitionOf(detail, ids.RoleQuestionId, ids.PmQuestionId).Id));
    }

    /// <summary>
    /// Die Trigger-Ereignisse hängen an der auslösenden Frage; die ohne Frage-Bezug bleiben dialogweit –
    /// und ein Ereignis zu einer gelöschten Frage ebenfalls, statt still zu verschwinden. <c>freshFrom</c>
    /// markiert genau die Ereignisse des letzten Schritts (sie blitzen einmal auf).
    /// </summary>
    [Fact]
    public void Build_ordnet_die_Trigger_dem_ausloesenden_Knoten_zu_und_markiert_die_neuen()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids));
        var state = new ResumeDialogResult(Guid.NewGuid(), SessionStatus.InProgress, null, []);

        DesignerTriggerEntry[] events =
        [
            new(TestDialogFactory.SampleTime, TriggerScope.OnDialogStarted, "DialogStartedNotification",
                ids.PositionQuestionId, "Session gestartet."),
            new(TestDialogFactory.SampleTime, TriggerScope.AfterQuestion, "QuestionAnsweredNotification",
                Guid.NewGuid(), "Übergang ausgewertet."),
            new(TestDialogFactory.SampleTime, TriggerScope.OnDialogCompleted, "DialogCompletedNotification",
                null, "Dialog abgeschlossen."),
        ];

        var overlay = GraphRunAnalyzer.Build(detail, state, events, freshFrom: 2);

        var atNode = Assert.Single(overlay.TriggersOf(ids.PositionQuestionId));
        Assert.Equal("Start", atNode.Label);
        Assert.Contains("OnDialogStarted", atNode.Title, StringComparison.Ordinal);
        Assert.False(atNode.IsFresh);

        // Das Ereignis zur gelöschten Frage verliert seinen Ort, nicht seine Sichtbarkeit.
        Assert.Equal(2, overlay.DialogTriggers.Count);
        Assert.True(overlay.Triggers[^1].IsFresh);
        Assert.False(overlay.Triggers[1].IsFresh);
    }

    /// <summary>
    /// Vor dem ersten Lauf ist der Zustand leer, aber gültig – Canvas und Inspector kennen deshalb keinen
    /// <see langword="null"/>-Fall.
    /// </summary>
    [Fact]
    public void NotStarted_liefert_einen_leeren_aber_gueltigen_Zustand()
    {
        var overlay = GraphRunAnalyzer.NotStarted();

        Assert.Null(overlay.CurrentQuestionId);
        Assert.Empty(overlay.Visits);
        Assert.Empty(overlay.TakenEdges);
        Assert.Empty(overlay.Loops);
        Assert.Empty(overlay.Triggers);
        Assert.Equal(0, overlay.Steps);
        Assert.Contains("noch nicht gestartet", overlay.Summary, StringComparison.Ordinal);
    }

    // ---- Aufbau --------------------------------------------------------------------------------------

    private FlirtyDbContext CreateContext() => new(_options);

    /// <summary>Baut den Laufzustand aus demselben Zustand, den auch der Runner liest.</summary>
    /// <param name="sessionId">Die laufende Session.</param>
    /// <returns>Der Laufzustand.</returns>
    private GraphRunOverlay BuildOverlay(Guid sessionId)
        => GraphRunAnalyzer.Build(LoadDetail(sessionId), LoadState(sessionId), []);

    /// <summary>Der Übergang zwischen zwei Fragen – im Testgraphen jeweils eindeutig.</summary>
    private static TransitionDetail TransitionOf(DialogDetail detail, Guid from, Guid target)
        => detail.Transitions.Single(
            transition => transition.FromQuestionId == from && transition.TargetQuestionId == target);

    /// <summary>Liest den Session-Zustand über dieselbe Query, die auch der Runner nutzt.</summary>
    private ResumeDialogResult LoadState(Guid sessionId)
    {
        using var context = CreateContext();
        return new ResumeDialogQueryHandler(new DialogStore(context))
            .Handle(new ResumeDialogQuery(sessionId), default)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Liest den Dialog-Graphen in derselben navigationsfreien Sicht, die der Runner nutzt.</summary>
    private DialogDetail LoadDetail(Guid sessionId)
    {
        using var context = CreateContext();
        var dialogId = context.DialogSessions.Single(session => session.Id == sessionId).DialogId;

        return AdminProjection.ToDetail(context.Dialogs
            .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
            .Include(dialog => dialog.Transitions)
            .Include(dialog => dialog.Loops)
            .Include(dialog => dialog.Triggers)
            .Single(dialog => dialog.Id == dialogId));
    }

    /// <summary>Legt den Loop-Dialog samt einer laufenden Session an der Einstiegsfrage an.</summary>
    private (Guid SessionId, LoopDialogIds Ids) SeedLoopSession()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildLoopDialog(dialogId, out var ids);

        return (Seed(dialog, ids.PositionQuestionId), ids);
    }

    /// <summary>
    /// Legt den Branching-Dialog samt laufender Session an. Über <paramref name="arrange"/> lässt sich der
    /// Graph vorher noch ergänzen (etwa um einen zweiten, parallelen Übergang).
    /// </summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedBranchingSession(
        Action<Dialog, BranchingDialogIds>? arrange = null)
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildBranchingDialog(dialogId, out var ids);
        arrange?.Invoke(dialog, ids);

        return (Seed(dialog, ids.RoleQuestionId), ids);
    }

    private Guid Seed(Dialog dialog, Guid currentQuestionId)
    {
        var sessionId = Guid.NewGuid();

        using var context = CreateContext();
        context.Dialogs.Add(dialog);
        context.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialog.Id,
            DialogVersion = dialog.Version,
            ExternalUserKey = "designer-test-1",
            Status = SessionStatus.InProgress,
            CurrentQuestionId = currentQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        context.SaveChanges();

        return sessionId;
    }

    /// <summary>Reicht eine Antwort über den echten Handler in eigenem Kontext ein.</summary>
    private async Task SubmitAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new SubmitAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());

        _ = await handler.Handle(new SubmitAnswerCommand(sessionId, questionId, value), default);
    }

    /// <summary>Überschreibt eine Antwort über den echten Handler in eigenem Kontext.</summary>
    private async Task EditAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new EditAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());

        _ = await handler.Handle(new EditAnswerCommand(sessionId, questionId, value), default);
    }
}
