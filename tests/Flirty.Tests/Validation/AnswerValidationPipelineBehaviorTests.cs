using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Pipeline;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Flirty.Validation;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Validation;

/// <summary>
/// Verifiziert das <c>AnswerValidationPipelineBehavior</c> (Issue #30) end-to-end durch die volle
/// Mediator-Pipeline via <see cref="IFlirtyEngine"/> gegen eine echte SQLite-Datenbank: Eine ungültige
/// Antwort wird <b>vor</b> dem Handler mit <see cref="AnswerValidationException"/> abgewiesen (ohne
/// Persistenz bzw. Invalidierung), gültige Antworten laufen unverändert durch, und die DI-Registrierung
/// stimmt.
/// </summary>
public sealed class AnswerValidationPipelineBehaviorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Öffnet eine offen gehaltene SQLite-in-memory-Verbindung und legt das Schema an.</summary>
    public AnswerValidationPipelineBehaviorTests()
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

    private Guid SeedBranchingDialog()
    {
        var dialogId = Guid.NewGuid();
        using var seed = new FlirtyDbContext(_options);
        seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        seed.SaveChanges();
        return dialogId;
    }

    /// <summary>
    /// Eine typwidrige Auswahl (kein bekannter Options-Value) wird vor dem Handler mit
    /// <see cref="AnswerValidationException"/> abgewiesen – es wird <b>keine</b> Antwort persistiert.
    /// </summary>
    [Fact]
    public async Task SubmitAnswerAsync_ungueltige_Auswahl_wirft_und_persistiert_nicht()
    {
        SeedBranchingDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");

        var exception = await Assert.ThrowsAsync<AnswerValidationException>(
            async () => await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"lead\""));
        Assert.Equal(start.CurrentQuestion.Id, exception.QuestionId);

        using var assert = new FlirtyDbContext(_options);
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == start.SessionId);
        Assert.Empty(session.Answers);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal(start.CurrentQuestion.Id, session.CurrentQuestionId);
    }

    /// <summary>Eine gültige Auswahl läuft unverändert durch die Pipeline (Branching greift).</summary>
    [Fact]
    public async Task SubmitAnswerAsync_gueltige_Auswahl_laeuft_durch()
    {
        SeedBranchingDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var result = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal("devDetail", result.NextQuestion.Key);
    }

    /// <summary>
    /// Ein ungültiger Editier-Wert wird abgewiesen, <b>bevor</b> der Handler nachgelagerte Antworten
    /// invalidiert oder den Pfad neu berechnet – die abgeschlossene Session bleibt unverändert.
    /// </summary>
    [Fact]
    public async Task EditAnswerAsync_ungueltiger_Wert_wirft_und_invalidiert_nicht()
    {
        SeedBranchingDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        // dev-Zweig vollständig durchlaufen (role → devDetail → Abschluss, zwei Antworten).
        var start = await engine.StartDialogAsync("branching", "user-1");
        var afterRole = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        await engine.SubmitAnswerAsync(start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");

        await Assert.ThrowsAsync<AnswerValidationException>(
            async () => await engine.EditAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"lead\""));

        using var assert = new FlirtyDbContext(_options);
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == start.SessionId);
        Assert.Equal(2, session.Answers.Count);
        Assert.Equal(SessionStatus.Completed, session.Status);
    }

    /// <summary>
    /// <c>AddFlirty()</c> registriert den <see cref="IAnswerValidator"/> und das geschlossene
    /// <c>AnswerValidationPipelineBehavior</c> für <see cref="SubmitAnswerCommand"/>.
    /// </summary>
    [Fact]
    public void AddFlirty_registriert_Validator_und_Behavior()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<AnswerValidator>(scope.ServiceProvider.GetRequiredService<IAnswerValidator>());

        var behaviors = scope.ServiceProvider
            .GetServices<IPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>>();
        Assert.Contains(
            behaviors,
            behavior => behavior is AnswerValidationPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>);
    }
}
