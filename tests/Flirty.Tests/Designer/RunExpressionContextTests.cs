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
/// Tests für den <see cref="RunExpressionContext"/> des Test-Runners (#43). Kernprobe ist der Abgleich
/// mit dem Core-<c>SessionExpressionContextBuilder</c>: Der Designer rechnet die Bindungen nach, weil der
/// Builder <c>internal</c> ist und eine <see cref="Dialog"/>-Entity mit Navigationen braucht – der Runner
/// hat nur <see cref="DialogDetail"/> und <see cref="ResumeDialogResult"/>. Auseinanderlaufen dürfen die
/// beiden trotzdem nicht, sonst zeigte der Runner andere Werte, als die Engine tatsächlich auswertet.
/// </summary>
public sealed class RunExpressionContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public RunExpressionContextTests()
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

    /// <summary>
    /// Der Abgleich mit der Engine – geprüft an jedem Punkt eines echten Durchlaufs mit zwei Iterationen:
    /// Antworten je Frage-Schlüssel, gesammelte Loop-Collection und Iterationsindex müssen in beiden
    /// Implementierungen identisch sein.
    /// </summary>
    [Fact]
    public async Task Build_stimmt_an_jedem_Schritt_mit_dem_SessionExpressionContextBuilder_ueberein()
    {
        var (sessionId, ids) = SeedLoopSession();

        AssertMatchesEngine(sessionId);

        // Iteration 1
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Entwickler\"");
        AssertMatchesEngine(sessionId);
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        AssertMatchesEngine(sessionId);

        // Iteration 2
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Architekt\"");
        AssertMatchesEngine(sessionId);
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");
        AssertMatchesEngine(sessionId);

        // Abschluss (keine offene Frage mehr)
        await SubmitAsync(sessionId, ids.SummaryQuestionId, "\"fertig\"");
        AssertMatchesEngine(sessionId);
    }

    /// <summary>
    /// Die Collection sammelt die Antworten der Einstiegsfrage je Iteration – das ist der Wert, den der
    /// Runner unter dem <c>CollectionKey</c> anzeigt.
    /// </summary>
    [Fact]
    public async Task Build_sammelt_die_Antworten_je_Iteration_unter_dem_CollectionKey()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Entwickler\"");
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Architekt\"");

        var snapshot = BuildSnapshot(sessionId);

        Assert.Equal(["\"Entwickler\"", "\"Architekt\""], snapshot.Collections["positions"]);

        // Unter dem Frage-Schlüssel steht die Antwort der AKTUELLEN Iteration, nicht die erste.
        Assert.Equal("\"Architekt\"", snapshot.Answers["position"]);
    }

    /// <summary>
    /// Vor der ersten Iteration ist die Collection leer, aber <b>gebunden</b> – sonst wäre
    /// <c>positions.Count &gt; 0</c> im Ausdruck ein unbekannter Bezeichner.
    /// </summary>
    [Fact]
    public void Build_bindet_die_Collection_auch_vor_der_ersten_Iteration()
    {
        var (sessionId, _) = SeedLoopSession();

        var snapshot = BuildSnapshot(sessionId);

        Assert.Empty(Assert.Contains("positions", snapshot.Collections));
        Assert.Empty(snapshot.Answers);
        Assert.Null(snapshot.IterationIndex);
    }

    /// <summary>
    /// Der Iterationsindex bezieht sich auf die aktuell offene Frage – und zwar auf deren <b>zuletzt
    /// gegebene</b> Antwort, nicht auf die bevorstehende. Das ist die Semantik von
    /// <c>LoopResolver.ResolveIterationIndex</c> und damit genau der Wert, den eine Bedingung an dieser
    /// Stelle auswertet; der Runner zeigt ihn deshalb nur als Kontext-Bindung und nicht als „laufende
    /// Iteration" an der Frage selbst.
    /// </summary>
    [Fact]
    public async Task Build_liefert_den_Iterationsindex_der_letzten_Antwort_auf_die_offene_Frage()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Entwickler\"");
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Architekt\"");

        // Offen ist "more". Beantwortet wurde sie bisher nur einmal – in Iteration 0.
        Assert.Equal(0, BuildSnapshot(sessionId).IterationIndex);

        // Nach dem zweiten "more" (Iteration 1) ist "summary" offen – die liegt außerhalb der Schleife.
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");
        Assert.Null(BuildSnapshot(sessionId).IterationIndex);
    }

    /// <summary>
    /// Antworten auf Fragen, die es im Dialog nicht (mehr) gibt, werden ignoriert – wie im Core, der
    /// über die Frage-Schlüssel des Graphen abbildet.
    /// </summary>
    [Fact]
    public async Task Build_ignoriert_Antworten_ohne_Frage_im_Graphen()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Entwickler\"");

        var detail = LoadDetail(sessionId);
        var state = LoadState(sessionId);

        // Den Graphen künstlich um die beantwortete Frage kürzen.
        var reduced = detail with
        {
            Questions = [.. detail.Questions.Where(question => question.Id != ids.PositionQuestionId)],
        };

        Assert.DoesNotContain("position", RunExpressionContext.Build(reduced, state).Answers);
    }

    // ---- Aufbau ------------------------------------------------------------------------------

    /// <summary>
    /// Baut beide Kontexte auf demselben Zustand und vergleicht sie. Der Core-Builder bekommt dieselbe
    /// „aktuelle Frage", die auch der Runner sieht (<see cref="ResumeDialogResult.CurrentQuestion"/>).
    /// </summary>
    /// <param name="sessionId">Die zu vergleichende Session.</param>
    private void AssertMatchesEngine(Guid sessionId)
    {
        var state = LoadState(sessionId);
        var vomDesigner = RunExpressionContext.Build(LoadDetail(sessionId), state);

        using var context = CreateContext();
        var session = context.DialogSessions
            .Include(entity => entity.Answers)
            .Single(entity => entity.Id == sessionId);
        var dialog = LoadDialog(context, session.DialogId);

        var vonDerEngine = SessionExpressionContextBuilder.Build(
            dialog, session, state.CurrentQuestion?.Id);

        Assert.Equal(vonDerEngine.Answers, vomDesigner.Answers);
        Assert.Equal(vonDerEngine.IterationIndex, vomDesigner.IterationIndex);
        Assert.Equal(
            vonDerEngine.Collections.ToDictionary(entry => entry.Key, entry => entry.Value.ToList()),
            vomDesigner.Collections.ToDictionary(entry => entry.Key, entry => entry.Value.ToList()));
    }

    private RunExpressionSnapshot BuildSnapshot(Guid sessionId)
        => RunExpressionContext.Build(LoadDetail(sessionId), LoadState(sessionId));

    /// <summary>Liest den Session-Zustand über dieselbe Query, die auch der Runner nutzt.</summary>
    /// <param name="sessionId">Die zu lesende Session.</param>
    /// <returns>Der Zustand.</returns>
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
    /// <param name="sessionId">Die Session, deren gepinnte Dialogversion geladen wird.</param>
    /// <returns>Die Sicht auf den Dialog.</returns>
    private DialogDetail LoadDetail(Guid sessionId)
    {
        using var context = CreateContext();
        var dialogId = context.DialogSessions.Single(session => session.Id == sessionId).DialogId;

        return AdminProjection.ToDetail(LoadDialog(context, dialogId));
    }

    private static Dialog LoadDialog(FlirtyDbContext context, Guid dialogId)
        => context.Dialogs
            .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
            .Include(dialog => dialog.Transitions)
            .Include(dialog => dialog.Loops)
            .Include(dialog => dialog.Triggers)
            .Single(dialog => dialog.Id == dialogId);

    /// <summary>Legt den Loop-Dialog samt einer laufenden Session an der Einstiegsfrage an.</summary>
    /// <returns>Die Session-Id und die Frage-Ids des Dialogs.</returns>
    private (Guid SessionId, LoopDialogIds Ids) SeedLoopSession()
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildLoopDialog(dialogId, out var ids));
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = "designer-test-1",
            Status = SessionStatus.InProgress,
            CurrentQuestionId = ids.PositionQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    /// <summary>Reicht eine Antwort über den echten Handler in eigenem Kontext ein.</summary>
    /// <param name="sessionId">Die laufende Session.</param>
    /// <param name="questionId">Die zu beantwortende Frage.</param>
    /// <param name="value">Der rohe JSON-Antwortwert.</param>
    private async Task SubmitAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new SubmitAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());

        _ = await handler.Handle(new SubmitAnswerCommand(sessionId, questionId, value), default);
    }
}
