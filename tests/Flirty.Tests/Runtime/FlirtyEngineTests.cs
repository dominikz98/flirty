using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert die öffentliche Facade <see cref="IFlirtyEngine"/> (Issue #25) end-to-end durch die
/// Mediator-Pipeline: DI-Registrierung, Start eines Dialogs über die Facade (Facade → <c>ISender</c>
/// → Handler → <see cref="IDialogStore"/> → EF Core) und die deklarative Command-Validierung.
/// </summary>
public sealed class FlirtyEngineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public FlirtyEngineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FlirtyDbContext(_options);
        context.Database.EnsureCreated();
    }

    /// <summary>Schließt die Verbindung und verwirft damit die in-memory-Datenbank.</summary>
    public void Dispose() => _connection.Dispose();

    private ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddLogging()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

    /// <summary>Die Facade startet einen veröffentlichten Dialog und liefert Session + erste Frage.</summary>
    [Fact]
    public async Task StartDialogAsync_startet_Dialog_ueber_die_Facade()
    {
        var dialogId = Guid.NewGuid();
        Guid questionId;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out questionId));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var result = await engine.StartDialogAsync("onboarding", "user-1");

        Assert.False(result.IsResumed);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Equal(questionId, result.CurrentQuestion.Id);
        Assert.Equal(2, result.CurrentQuestion.Options.Count);
    }

    /// <summary>Ein leerer <c>DialogKey</c> wird durch das <c>ValidationPipelineBehavior</c> abgewiesen.</summary>
    [Fact]
    public async Task StartDialogAsync_leerer_DialogKey_wird_von_der_Pipeline_abgewiesen()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await engine.StartDialogAsync(string.Empty, "user-1"));
    }

    /// <summary>Die Facade reicht eine Antwort ein und liefert über das Branching die nächste Frage.</summary>
    [Fact]
    public async Task SubmitAnswerAsync_reicht_Antwort_ein_und_liefert_naechste_Frage()
    {
        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var result = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.DevQuestionId, result.NextQuestion.Id);
    }

    /// <summary>Ein <c>null</c>-Antwortwert wird durch das <c>ValidationPipelineBehavior</c> abgewiesen.</summary>
    [Fact]
    public async Task SubmitAnswerAsync_null_Value_wird_von_der_Pipeline_abgewiesen()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await engine.SubmitAnswerAsync(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    /// <summary>
    /// Die Facade liest nach Start und einer eingereichten Antwort den Session-Zustand: Status, die nun
    /// aktuelle Frage und die bisher gegebene Antwort.
    /// </summary>
    [Fact]
    public async Task ResumeDialogAsync_liefert_Zustand_und_bisherige_Antworten()
    {
        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        var result = await engine.ResumeDialogAsync(start.SessionId);

        Assert.Equal(start.SessionId, result.SessionId);
        Assert.Equal(SessionStatus.InProgress, result.Status);
        Assert.NotNull(result.CurrentQuestion);
        Assert.Equal(ids.DevQuestionId, result.CurrentQuestion.Id);

        var answer = Assert.Single(result.Answers);
        Assert.Equal("role", answer.QuestionKey);
        Assert.Equal("\"dev\"", answer.Value);
    }

    /// <summary>Eine unbekannte Session lässt <c>ResumeDialogAsync</c> mit <see cref="SessionNotFoundException"/> fehlschlagen.</summary>
    [Fact]
    public async Task ResumeDialogAsync_unbekannte_Session_wirft_SessionNotFoundException()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<SessionNotFoundException>(
            async () => await engine.ResumeDialogAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Die Facade editiert eine frühere Antwort einer bereits abgeschlossenen Session, berechnet den Pfad
    /// neu (dev-Zweig → pm-Zweig), öffnet die Session wieder und meldet die verworfene nachgelagerte Antwort.
    /// </summary>
    [Fact]
    public async Task EditAnswerAsync_ueberschreibt_und_berechnet_Pfad_neu()
    {
        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        // dev-Zweig vollständig durchlaufen (role → devDetail → Abschluss).
        var start = await engine.StartDialogAsync("branching", "user-1");
        var afterRole = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        await engine.SubmitAnswerAsync(start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");

        var result = await engine.EditAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"pm\"");

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.PmQuestionId, result.NextQuestion.Id);
        Assert.Equal(1, result.InvalidatedAnswers);
    }

    /// <summary>Ein <c>null</c>-Antwortwert wird bei <c>EditAnswerAsync</c> durch die Pipeline abgewiesen.</summary>
    [Fact]
    public async Task EditAnswerAsync_null_Value_wird_von_der_Pipeline_abgewiesen()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await engine.EditAnswerAsync(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    /// <summary><c>AddFlirty()</c> registriert <see cref="IFlirtyEngine"/> als <see cref="FlirtyEngine"/>.</summary>
    [Fact]
    public void AddFlirty_registriert_IFlirtyEngine()
    {
        using var provider = new ServiceCollection()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        Assert.IsType<FlirtyEngine>(engine);
    }

    /// <summary><c>AddFlirty()</c> registriert den Default-<see cref="IExpressionEvaluator"/> (#26).</summary>
    [Fact]
    public void AddFlirty_registriert_IExpressionEvaluator()
    {
        using var provider = new ServiceCollection()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<DynamicExpressoExpressionEvaluator>(evaluator);
    }
}
