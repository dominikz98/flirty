using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert den <see cref="StartDialogVersionCommandHandler"/> (Issue #43) gegen eine echte
/// SQLite-Datenbank (in-memory). Kern der Abgrenzung zum <see cref="StartDialogCommandHandler"/>: Dieser
/// Command startet eine <b>konkrete Dialogversion unabhängig vom Veröffentlichungsstatus</b> – die
/// Grundlage dafür, dass der Designer-Test-Runner einen Entwurf durchspielen kann.
/// </summary>
public sealed class StartDialogVersionCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public StartDialogVersionCommandHandlerTests()
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

    private static StartDialogVersionCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), new SpyPublisher());

    private static StartDialogVersionCommandHandler CreateHandler(FlirtyDbContext context, IPublisher publisher)
        => new(new DialogStore(context), publisher);

    /// <summary>
    /// Legt einen <b>unveröffentlichten</b> Dialog mit Startfrage an – der Fall, den
    /// <see cref="StartDialogCommand"/> bewusst ablehnt.
    /// </summary>
    /// <param name="dialogId">Die zu vergebende Dialog-Id.</param>
    /// <returns>Die Id der Einstiegsfrage.</returns>
    private Guid ArrangeDraft(Guid dialogId)
    {
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        dialog.IsPublished = false;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(dialog);
        arrange.SaveChanges();

        return questionId;
    }

    // ---- Neu-Start --------------------------------------------------------------------------

    /// <summary>
    /// Der Kern von #43: Ein Entwurf lässt sich starten, obwohl er nicht veröffentlicht ist. Zur
    /// Gegenprobe wird derselbe Dialog über <see cref="StartDialogCommand"/> abgelehnt.
    /// </summary>
    [Fact]
    public async Task Handle_startet_einen_unveroeffentlichten_Entwurf()
    {
        var dialogId = Guid.NewGuid();
        var questionId = ArrangeDraft(dialogId);

        StartDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);
        }

        Assert.False(result.IsResumed);
        Assert.Equal(questionId, result.CurrentQuestion.Id);
        Assert.Equal("role", result.CurrentQuestion.Key);

        using var assert = CreateContext();
        var session = Assert.Single(assert.DialogSessions);
        Assert.Equal(result.SessionId, session.Id);
        Assert.Equal(dialogId, session.DialogId);
        Assert.Equal(1, session.DialogVersion);
        Assert.Equal("designer-test-1", session.ExternalUserKey);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal(questionId, session.CurrentQuestionId);
    }

    /// <summary>Gegenprobe: Denselben Entwurf lehnt der veröffentlichungsgebundene Start weiterhin ab.</summary>
    [Fact]
    public async Task StartDialogCommand_lehnt_denselben_Entwurf_weiterhin_ab()
    {
        _ = ArrangeDraft(Guid.NewGuid());

        using var act = CreateContext();

        await Assert.ThrowsAsync<DialogNotFoundException>(
            async () => await new StartDialogCommandHandler(new DialogStore(act), new SpyPublisher())
                .Handle(new StartDialogCommand("onboarding", "designer-test-1"), default));
    }

    /// <summary>Ein veröffentlichter Dialog lässt sich über die Id genauso starten.</summary>
    [Fact]
    public async Task Handle_startet_auch_einen_veroeffentlichten_Dialog()
    {
        var dialogId = Guid.NewGuid();
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            arrange.SaveChanges();
        }

        using var act = CreateContext();
        var result = await CreateHandler(act)
            .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);

        Assert.False(result.IsResumed);
        Assert.NotEqual(Guid.Empty, result.SessionId);
    }

    // ---- Resume -----------------------------------------------------------------------------

    /// <summary>
    /// Wie beim veröffentlichungsgebundenen Start wird eine laufende Session desselben Anwenders
    /// fortgesetzt. Genau deshalb vergibt der Test-Runner je Lauf einen frischen Anwenderschlüssel.
    /// </summary>
    [Fact]
    public async Task Handle_setzt_eine_laufende_Session_desselben_Anwenders_fort()
    {
        var dialogId = Guid.NewGuid();
        _ = ArrangeDraft(dialogId);

        StartDialogResult first;
        using (var firstContext = CreateContext())
        {
            first = await CreateHandler(firstContext)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);
        }

        StartDialogResult second;
        using (var secondContext = CreateContext())
        {
            second = await CreateHandler(secondContext)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);
        }

        Assert.False(first.IsResumed);
        Assert.True(second.IsResumed);
        Assert.Equal(first.SessionId, second.SessionId);

        using var assert = CreateContext();
        Assert.Single(assert.DialogSessions);
    }

    /// <summary>Ein frischer Anwenderschlüssel liefert einen frischen Lauf – das Muster des Test-Runners.</summary>
    [Fact]
    public async Task Handle_liefert_je_Anwenderschluessel_eine_eigene_Session()
    {
        var dialogId = Guid.NewGuid();
        _ = ArrangeDraft(dialogId);

        StartDialogResult first;
        using (var firstContext = CreateContext())
        {
            first = await CreateHandler(firstContext)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);
        }

        StartDialogResult second;
        using (var secondContext = CreateContext())
        {
            second = await CreateHandler(secondContext)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-2"), default);
        }

        Assert.NotEqual(first.SessionId, second.SessionId);

        using var assert = CreateContext();
        Assert.Equal(2, assert.DialogSessions.Count());
    }

    // ---- Fehlerfälle ------------------------------------------------------------------------

    /// <summary>Eine unbekannte Dialog-Id meldet <see cref="ConfigurationNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_meldet_unbekannte_Dialogversion()
    {
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<ConfigurationNotFoundException>(
            async () => await CreateHandler(act)
                .Handle(new StartDialogVersionCommand(Guid.NewGuid(), "designer-test-1"), default));

        Assert.Contains("Dialog", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Ohne Einstiegsfrage gibt es nichts zu starten – das meldet der Handler.</summary>
    [Fact]
    public async Task Handle_meldet_fehlende_Einstiegsfrage()
    {
        var headless = TestDialogFactory.NewDialog("headless", version: 1, name: "Ohne Start");
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(headless); // StartQuestionId bleibt null
            arrange.SaveChanges();
        }

        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act)
                .Handle(new StartDialogVersionCommand(headless.Id, "designer-test-1"), default));

        Assert.Contains("Einstiegsfrage", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Store ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Store()
        => Assert.Throws<ArgumentNullException>(
            () => new StartDialogVersionCommandHandler(null!, new SpyPublisher()));

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Publisher ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Publisher()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new StartDialogVersionCommandHandler(new DialogStore(context), null!));
    }

    // ---- Trigger-Notifications --------------------------------------------------------------

    /// <summary>Ein Neu-Start publiziert genau eine <see cref="DialogStartedNotification"/>.</summary>
    [Fact]
    public async Task Handle_publiziert_DialogStarted_beim_Neustart()
    {
        var dialogId = Guid.NewGuid();
        var questionId = ArrangeDraft(dialogId);

        var spy = new SpyPublisher();
        StartDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act, spy)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);
        }

        var notification = Assert.IsType<DialogStartedNotification>(Assert.Single(spy.Published));
        Assert.Equal(result.SessionId, notification.SessionId);
        Assert.Equal(dialogId, notification.DialogId);
        Assert.Equal("onboarding", notification.DialogKey);
        Assert.Equal("designer-test-1", notification.ExternalUserKey);
        Assert.Equal(questionId, notification.CurrentQuestionId);
    }

    /// <summary>Ein Resume publiziert bewusst keine Notification.</summary>
    [Fact]
    public async Task Handle_Resume_publiziert_keine_Notification()
    {
        var dialogId = Guid.NewGuid();
        _ = ArrangeDraft(dialogId);

        using (var firstContext = CreateContext())
        {
            _ = await CreateHandler(firstContext)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);
        }

        var spy = new SpyPublisher();
        using (var resumeContext = CreateContext())
        {
            var result = await CreateHandler(resumeContext, spy)
                .Handle(new StartDialogVersionCommand(dialogId, "designer-test-1"), default);
            Assert.True(result.IsResumed);
        }

        Assert.Empty(spy.Published);
    }
}
