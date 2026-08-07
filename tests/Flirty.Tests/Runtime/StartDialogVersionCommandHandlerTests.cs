using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Placeholders;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the <see cref="StartDialogVersionCommandHandler"/> (issue #43) against a real SQLite
/// database (in-memory). The core of what sets it apart from the
/// <see cref="StartDialogCommandHandler"/>: this command starts a <b>specific dialog version
/// regardless of the publication status</b> – the basis for the designer test runner being able to
/// play a draft through.
/// </summary>
public sealed class StartDialogVersionCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
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

    /// <summary>Closes the connection and thereby discards the in-memory database.</summary>
    public void Dispose() => _connection.Dispose();

    private FlirtyDbContext CreateContext() => new(_options);

    private static StartDialogVersionCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), new SpyPublisher(), PlaceholderRenderer.Disabled);

    private static StartDialogVersionCommandHandler CreateHandler(FlirtyDbContext context, IPublisher publisher)
        => new(new DialogStore(context), publisher, PlaceholderRenderer.Disabled);

    /// <summary>
    /// Creates an <b>unpublished</b> dialog with an entry question – the case
    /// <see cref="StartDialogCommand"/> deliberately rejects.
    /// </summary>
    /// <param name="dialogId">The dialog id to assign.</param>
    /// <returns>The id of the entry question.</returns>
    private Guid ArrangeDraft(Guid dialogId)
    {
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        dialog.IsPublished = false;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(dialog);
        arrange.SaveChanges();

        return questionId;
    }

    // ---- Fresh start ------------------------------------------------------------------------

    /// <summary>
    /// The core of #43: a draft can be started even though it is not published. As a counter-check,
    /// the same dialog is rejected by <see cref="StartDialogCommand"/>.
    /// </summary>
    [Fact]
    public async Task Handle_starts_an_unpublished_draft()
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

    /// <summary>Counter-check: the publication-bound start still rejects the very same draft.</summary>
    [Fact]
    public async Task StartDialogCommand_still_rejects_the_same_draft()
    {
        _ = ArrangeDraft(Guid.NewGuid());

        using var act = CreateContext();

        await Assert.ThrowsAsync<DialogNotFoundException>(
            async () => await new StartDialogCommandHandler(
                    new DialogStore(act), new SpyPublisher(), PlaceholderRenderer.Disabled)
                .Handle(new StartDialogCommand("onboarding", "designer-test-1"), default));
    }

    /// <summary>A published dialog can be started by id just the same.</summary>
    [Fact]
    public async Task Handle_starts_a_published_dialog_too()
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
    /// As with the publication-bound start, a running session of the same user is resumed. That is
    /// exactly why the test runner hands out a fresh user key per run.
    /// </summary>
    [Fact]
    public async Task Handle_resumes_a_running_session_of_the_same_user()
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

    /// <summary>A fresh user key yields a fresh run – the test runner's pattern.</summary>
    [Fact]
    public async Task Handle_returns_its_own_session_per_user_key()
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

    // ---- Error cases ------------------------------------------------------------------------

    /// <summary>An unknown dialog id reports a <see cref="ConfigurationNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_reports_an_unknown_dialog_version()
    {
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<ConfigurationNotFoundException>(
            async () => await CreateHandler(act)
                .Handle(new StartDialogVersionCommand(Guid.NewGuid(), "designer-test-1"), default));

        Assert.Contains("dialog", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Without an entry question there is nothing to start – the handler reports that.</summary>
    [Fact]
    public async Task Handle_reports_a_missing_entry_question()
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

        Assert.Contains("entry question", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The constructor rejects a <c>null</c> store.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_store()
        => Assert.Throws<ArgumentNullException>(
            () => new StartDialogVersionCommandHandler(null!, new SpyPublisher(), PlaceholderRenderer.Disabled));

    /// <summary>The constructor rejects a <c>null</c> publisher.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_publisher()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new StartDialogVersionCommandHandler(new DialogStore(context), null!, PlaceholderRenderer.Disabled));
    }

    /// <summary>The constructor rejects a <c>null</c> renderer.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_renderer()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new StartDialogVersionCommandHandler(new DialogStore(context), new SpyPublisher(), null!));
    }

    // ---- Trigger notifications --------------------------------------------------------------

    /// <summary>A fresh start publishes exactly one <see cref="DialogStartedNotification"/>.</summary>
    [Fact]
    public async Task Handle_publishes_DialogStarted_on_a_fresh_start()
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

    /// <summary>A resume deliberately publishes no notification.</summary>
    [Fact]
    public async Task Handle_a_resume_publishes_no_notification()
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
