using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the <see cref="StartDialogCommandHandler"/> (issue #25) against a real SQLite database
/// (in-memory): a fresh start (session creation, pinned version, entry question), the projection of
/// the <see cref="QuestionView"/>, resuming a running session as well as the error cases (unknown or
/// unpublished dialog, missing entry question, <c>null</c> store).
/// </summary>
public sealed class StartDialogCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
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

    /// <summary>Closes the connection and thereby discards the in-memory database.</summary>
    public void Dispose() => _connection.Dispose();

    private FlirtyDbContext CreateContext() => new(_options);

    private static StartDialogCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), new SpyPublisher());

    private static StartDialogCommandHandler CreateHandler(FlirtyDbContext context, IPublisher publisher)
        => new(new DialogStore(context), publisher);

    // ---- Fresh start ------------------------------------------------------------------------

    /// <summary>A fresh start creates a running session with a pinned version and the entry question.</summary>
    [Fact]
    public async Task Handle_a_fresh_start_creates_an_in_progress_session()
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

    /// <summary>The current question is projected incl. its options in <see cref="AnswerOption.Order"/> order.</summary>
    [Fact]
    public async Task Handle_projects_the_question_and_the_options_in_order()
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
        Assert.Equal("Which role?", question.Text);
        Assert.Equal(QuestionType.SingleChoice, question.Type);
        Assert.Equal(["dev", "pm"], question.Options.Select(option => option.Key));
        Assert.Equal("Developer", question.Options[0].Label);
        Assert.Equal("dev", question.Options[0].Value);
    }

    // ---- Resume -----------------------------------------------------------------------------

    /// <summary>If a running session already exists, it is resumed instead of created anew.</summary>
    [Fact]
    public async Task Handle_resumes_a_running_session_without_creating_a_new_one()
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

    /// <summary>Different users each get their own session.</summary>
    [Fact]
    public async Task Handle_different_users_get_separate_sessions()
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

    // ---- Error cases ------------------------------------------------------------------------

    /// <summary>An unknown dialog key leads to a <see cref="DialogNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_an_unknown_key_throws_DialogNotFoundException()
    {
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<DialogNotFoundException>(
            async () => await CreateHandler(act).Handle(new StartDialogCommand("does-not-exist", "user-1"), default));

        Assert.Equal("does-not-exist", exception.DialogKey);
    }

    /// <summary>A dialog that exists only unpublished counts as not found.</summary>
    [Fact]
    public async Task Handle_an_unpublished_dialog_throws_DialogNotFoundException()
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

    /// <summary>A published dialog without an entry question is misconfigured and is rejected.</summary>
    [Fact]
    public async Task Handle_a_published_dialog_without_an_entry_question_throws_InvalidOperationException()
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

    /// <summary>The constructor rejects a <c>null</c> store.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_store()
        => Assert.Throws<ArgumentNullException>(() => new StartDialogCommandHandler(null!, new SpyPublisher()));

    /// <summary>The constructor rejects a <c>null</c> publisher.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_publisher()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new StartDialogCommandHandler(new DialogStore(context), null!));
    }

    // ---- Trigger notifications --------------------------------------------------------------

    /// <summary>A fresh start publishes exactly one <see cref="DialogStartedNotification"/>.</summary>
    [Fact]
    public async Task Handle_a_fresh_start_publishes_DialogStarted()
    {
        var dialogId = Guid.NewGuid();
        Guid questionId;
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out questionId));
            arrange.SaveChanges();
        }

        var spy = new SpyPublisher();
        StartDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act, spy).Handle(new StartDialogCommand("onboarding", "user-1"), default);
        }

        var notification = Assert.IsType<DialogStartedNotification>(Assert.Single(spy.Published));
        Assert.Equal(result.SessionId, notification.SessionId);
        Assert.Equal(dialogId, notification.DialogId);
        Assert.Equal("onboarding", notification.DialogKey);
        Assert.Equal("user-1", notification.ExternalUserKey);
        Assert.Equal(questionId, notification.CurrentQuestionId);
    }

    /// <summary>Resuming a running session deliberately publishes no notification.</summary>
    [Fact]
    public async Task Handle_a_resume_publishes_no_notification()
    {
        var dialogId = Guid.NewGuid();
        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            arrange.SaveChanges();
        }

        using (var firstContext = CreateContext())
        {
            await CreateHandler(firstContext).Handle(new StartDialogCommand("onboarding", "user-1"), default);
        }

        var spy = new SpyPublisher();
        using (var resumeContext = CreateContext())
        {
            var result = await CreateHandler(resumeContext, spy)
                .Handle(new StartDialogCommand("onboarding", "user-1"), default);
            Assert.True(result.IsResumed);
        }

        Assert.Empty(spy.Published);
    }
}
