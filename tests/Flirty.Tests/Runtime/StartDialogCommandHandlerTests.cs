using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert den <see cref="StartDialogCommandHandler"/> (Issue #25) gegen eine echte SQLite-Datenbank
/// (in-memory): Neu-Start (Session-Anlage, gepinnte Version, Startfrage), Projektion der
/// <see cref="QuestionView"/>, Resume einer laufenden Session sowie die Fehlerfälle
/// (unbekannter/unpublizierter Dialog, fehlende Startfrage, <c>null</c>-Store).
/// </summary>
public sealed class StartDialogCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public StartDialogCommandHandlerTests()
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

    private static StartDialogCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context));

    // ---- Neu-Start --------------------------------------------------------------------------

    /// <summary>Ein Neu-Start legt eine laufende Session mit gepinnter Version und Startfrage an.</summary>
    [Fact]
    public async Task Handle_neuer_Start_legt_InProgress_Session_an()
    {
        var dialogId = Guid.NewGuid();
        Guid questionId;
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out questionId));
            arrange.SaveChanges();
        }

        StartDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(new StartDialogCommand("onboarding", "user-1"), default);
        }

        Assert.False(result.IsResumed);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Equal(questionId, result.CurrentQuestion.Id);

        using var assert = CreateContext();
        var session = Assert.Single(assert.DialogSessions);
        Assert.Equal(result.SessionId, session.Id);
        Assert.Equal(dialogId, session.DialogId);
        Assert.Equal(1, session.DialogVersion);
        Assert.Equal("user-1", session.ExternalUserKey);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal(questionId, session.CurrentQuestionId);
    }

    /// <summary>Die aktuelle Frage wird samt Optionen in <see cref="AnswerOption.Order"/>-Reihenfolge projiziert.</summary>
    [Fact]
    public async Task Handle_projiziert_Frage_und_Optionen_in_Reihenfolge()
    {
        var dialogId = Guid.NewGuid();
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            arrange.SaveChanges();
        }

        using var act = CreateContext();
        var result = await CreateHandler(act).Handle(new StartDialogCommand("onboarding", "user-1"), default);

        var question = result.CurrentQuestion;
        Assert.Equal("role", question.Key);
        Assert.Equal("Welche Rolle?", question.Text);
        Assert.Equal(QuestionType.SingleChoice, question.Type);
        Assert.Equal(["dev", "pm"], question.Options.Select(option => option.Key));
        Assert.Equal("Entwickler", question.Options[0].Label);
        Assert.Equal("dev", question.Options[0].Value);
    }

    // ---- Resume -----------------------------------------------------------------------------

    /// <summary>Existiert bereits eine laufende Session, wird sie fortgesetzt statt neu angelegt.</summary>
    [Fact]
    public async Task Handle_setzt_laufende_Session_fort_ohne_neue_anzulegen()
    {
        var dialogId = Guid.NewGuid();
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            arrange.SaveChanges();
        }

        StartDialogResult first;
        using (var firstContext = CreateContext())
        {
            first = await CreateHandler(firstContext).Handle(new StartDialogCommand("onboarding", "user-1"), default);
        }

        StartDialogResult second;
        using (var secondContext = CreateContext())
        {
            second = await CreateHandler(secondContext).Handle(new StartDialogCommand("onboarding", "user-1"), default);
        }

        Assert.False(first.IsResumed);
        Assert.True(second.IsResumed);
        Assert.Equal(first.SessionId, second.SessionId);

        using var assert = CreateContext();
        Assert.Single(assert.DialogSessions);
    }

    /// <summary>Verschiedene Anwender erhalten je eine eigene Session.</summary>
    [Fact]
    public async Task Handle_verschiedene_Anwender_erhalten_getrennte_Sessions()
    {
        var dialogId = Guid.NewGuid();
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            arrange.SaveChanges();
        }

        StartDialogResult first;
        using (var firstContext = CreateContext())
        {
            first = await CreateHandler(firstContext).Handle(new StartDialogCommand("onboarding", "user-1"), default);
        }

        StartDialogResult second;
        using (var secondContext = CreateContext())
        {
            second = await CreateHandler(secondContext).Handle(new StartDialogCommand("onboarding", "user-2"), default);
        }

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.False(first.IsResumed);
        Assert.False(second.IsResumed);

        using var assert = CreateContext();
        Assert.Equal(2, assert.DialogSessions.Count());
    }

    // ---- Fehlerfälle ------------------------------------------------------------------------

    /// <summary>Ein unbekannter Dialog-Schlüssel führt zu einer <see cref="DialogNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_unbekannter_Key_wirft_DialogNotFoundException()
    {
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<DialogNotFoundException>(
            async () => await CreateHandler(act).Handle(new StartDialogCommand("does-not-exist", "user-1"), default));

        Assert.Equal("does-not-exist", exception.DialogKey);
    }

    /// <summary>Ein nur unpubliziert vorhandener Dialog gilt als nicht gefunden.</summary>
    [Fact]
    public async Task Handle_unpublizierter_Dialog_wirft_DialogNotFoundException()
    {
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.NewDialog("draft", version: 1, name: "Entwurf"));
            arrange.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<DialogNotFoundException>(
            async () => await CreateHandler(act).Handle(new StartDialogCommand("draft", "user-1"), default));
    }

    /// <summary>Ein veröffentlichter Dialog ohne Startfrage ist fehlkonfiguriert und wird abgelehnt.</summary>
    [Fact]
    public async Task Handle_publizierter_Dialog_ohne_Startfrage_wirft_InvalidOperationException()
    {
        using (var arrange = CreateContext())
        {
            var headless = TestDialogFactory.NewDialog("headless", version: 1, name: "Ohne Start");
            headless.IsPublished = true; // StartQuestionId bleibt null
            arrange.Dialogs.Add(headless);
            arrange.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(new StartDialogCommand("headless", "user-1"), default));
    }

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Store ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Store()
        => Assert.Throws<ArgumentNullException>(() => new StartDialogCommandHandler(null!));
}
