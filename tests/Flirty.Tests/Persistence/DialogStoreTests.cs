using Flirty.Domain;
using Flirty.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifies the <see cref="IDialogStore"/> repository (issue #21) against a real SQLite database
/// (in-memory): loading published or pinned dialog graphs (untracked), the tracked loading of
/// sessions incl. answers, the active-session filter, the trigger query per session and point in
/// time (#42), the unit-of-work seam (<see cref="IDialogStore.AddSession"/> +
/// <see cref="IDialogStore.SaveChangesAsync"/>) as well as the DI registration.
/// </summary>
public sealed class DialogStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public DialogStoreTests()
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

    // ---- GetPublishedDialogAsync ------------------------------------------------------------

    /// <summary>With several published versions the store returns the highest one.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_returns_the_highest_published_version()
    {
        using (var context = CreateContext())
        {
            context.Dialogs.Add(PublishedDialog("survey", version: 1));
            context.Dialogs.Add(PublishedDialog("survey", version: 2));
            context.Dialogs.Add(UnpublishedDialog("survey", version: 3));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("survey");

        Assert.NotNull(dialog);
        Assert.Equal(2, dialog.Version);
        Assert.True(dialog.IsPublished);
    }

    /// <summary>If only unpublished versions exist, the store returns <c>null</c>.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_ignores_unpublished_dialogs()
    {
        using (var context = CreateContext())
        {
            context.Dialogs.Add(UnpublishedDialog("draft", version: 1));
            context.Dialogs.Add(UnpublishedDialog("draft", version: 2));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("draft");

        Assert.Null(dialog);
    }

    /// <summary>An unknown key returns <c>null</c> instead of throwing.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_an_unknown_key_returns_null()
    {
        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("does-not-exist");

        Assert.Null(dialog);
    }

    /// <summary>The complete configuration graph (questions/options, transitions, loops, triggers) is loaded.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_loads_the_complete_graph()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("onboarding");

        Assert.NotNull(dialog);
        var question = Assert.Single(dialog.Questions);
        Assert.Equal(2, question.Options.Count);
        Assert.Single(dialog.Transitions);
        Assert.Single(dialog.Loops);
        Assert.Single(dialog.Triggers);
    }

    /// <summary>The dialog graph is returned untracked (the change tracker stays empty).</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_returns_an_untracked_graph()
    {
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(Guid.NewGuid(), out _));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        _ = await new DialogStore(readContext).GetPublishedDialogAsync("onboarding");

        Assert.Empty(readContext.ChangeTracker.Entries());
    }

    // ---- GetDialogAsync ---------------------------------------------------------------------

    /// <summary>Loads exactly the version pinned by id incl. its graph, even when further versions exist.</summary>
    [Fact]
    public async Task GetDialogAsync_returns_the_pinned_version_with_its_graph()
    {
        var pinnedId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(pinnedId, out _)); // onboarding v1
            context.Dialogs.Add(PublishedDialog("onboarding", version: 2));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetDialogAsync(pinnedId);

        Assert.NotNull(dialog);
        Assert.Equal(pinnedId, dialog.Id);
        Assert.Equal(1, dialog.Version);
        Assert.Single(dialog.Questions);
        Assert.Single(dialog.Triggers);
    }

    /// <summary>Loads an unpublished dialog by id too (no <c>IsPublished</c> filter – the pinning contract).</summary>
    [Fact]
    public async Task GetDialogAsync_loads_an_unpublished_dialog_too()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var dialog = UnpublishedDialog("draft", version: 1);
            dialog.Id = dialogId;
            context.Dialogs.Add(dialog);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var loaded = await new DialogStore(readContext).GetDialogAsync(dialogId);

        Assert.NotNull(loaded);
        Assert.False(loaded.IsPublished);
    }

    /// <summary>An unknown id returns <c>null</c>.</summary>
    [Fact]
    public async Task GetDialogAsync_an_unknown_id_returns_null()
    {
        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetDialogAsync(Guid.NewGuid());

        Assert.Null(dialog);
    }

    /// <summary><see cref="IDialogStore.GetDialogAsync"/> returns the graph untracked as well.</summary>
    [Fact]
    public async Task GetDialogAsync_returns_an_untracked_graph()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        _ = await new DialogStore(readContext).GetDialogAsync(dialogId);

        Assert.Empty(readContext.ChangeTracker.Entries());
    }

    // ---- GetSessionAsync --------------------------------------------------------------------

    /// <summary>Loads the session together with its answers.</summary>
    [Fact]
    public async Task GetSessionAsync_returns_the_session_with_its_answers()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var session = NewSession(Guid.NewGuid(), "user-1", id: sessionId);
            session.Answers.Add(Answer(sessionId, questionId, "\"a\"", sequence: 0));
            session.Answers.Add(Answer(sessionId, questionId, "\"b\"", sequence: 1));
            context.DialogSessions.Add(session);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var loaded = await new DialogStore(readContext).GetSessionAsync(sessionId);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Answers.Count);
    }

    /// <summary>The session is returned tracked, so that later mutations can be persisted.</summary>
    [Fact]
    public async Task GetSessionAsync_returns_a_tracked_session()
    {
        var sessionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(Guid.NewGuid(), "user-1", id: sessionId));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(EntityState.Unchanged, readContext.Entry(session).State);
    }

    /// <summary>An unknown session id returns <c>null</c>.</summary>
    [Fact]
    public async Task GetSessionAsync_an_unknown_id_returns_null()
    {
        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).GetSessionAsync(Guid.NewGuid());

        Assert.Null(session);
    }

    /// <summary>Several answers to the same question (loop iterations) are loaded completely.</summary>
    [Fact]
    public async Task GetSessionAsync_holds_several_answers_per_question_one_per_iteration()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var loopInstanceId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var seed = NewSession(Guid.NewGuid(), "user-1", id: sessionId);
            seed.Answers.Add(Answer(sessionId, questionId, "\"A\"", sequence: 0, loopInstanceId, iterationIndex: 0));
            seed.Answers.Add(Answer(sessionId, questionId, "\"B\"", sequence: 1, loopInstanceId, iterationIndex: 1));
            context.DialogSessions.Add(seed);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(2, session.Answers.Count);
        Assert.All(session.Answers, answer => Assert.Equal(questionId, answer.QuestionId));
        Assert.Equal([0, 1], session.Answers.Select(answer => answer.IterationIndex).Order());
    }

    // ---- FindActiveSessionAsync -------------------------------------------------------------

    /// <summary>Finds the running session for (DialogId, ExternalUserKey) incl. its answers.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_finds_the_running_session_with_its_answers()
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var session = NewSession(dialogId, "user-1", id: sessionId);
            session.Answers.Add(Answer(sessionId, Guid.NewGuid(), "\"a\"", sequence: 0));
            context.DialogSessions.Add(session);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.NotNull(found);
        Assert.Equal(sessionId, found.Id);
        Assert.Single(found.Answers);
    }

    /// <summary>Completed or abandoned sessions do not count as active.</summary>
    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task FindActiveSessionAsync_ignores_sessions_that_are_not_running(SessionStatus status)
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1", status));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.Null(found);
    }

    /// <summary>The filter tells users apart by the <c>ExternalUserKey</c>.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_filters_by_ExternalUserKey()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1"));
            context.DialogSessions.Add(NewSession(dialogId, "user-2"));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-2");

        Assert.NotNull(found);
        Assert.Equal("user-2", found.ExternalUserKey);
    }

    /// <summary>The filter tells dialogs apart by the <c>DialogId</c>.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_filters_by_DialogId()
    {
        var dialogA = Guid.NewGuid();
        var dialogB = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogA, "user-1"));
            context.DialogSessions.Add(NewSession(dialogB, "user-1"));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogB, "user-1");

        Assert.NotNull(found);
        Assert.Equal(dialogB, found.DialogId);
    }

    /// <summary>Without a matching running session the store returns <c>null</c>.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_without_a_hit_returns_null()
    {
        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(Guid.NewGuid(), "user-1");

        Assert.Null(found);
    }

    /// <summary>With several running sessions the most recently started one wins.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_returns_the_newest_of_several_running_ones()
    {
        var dialogId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1", startedAt: TestDialogFactory.SampleTime));
            context.DialogSessions.Add(NewSession(dialogId, "user-1",
                startedAt: TestDialogFactory.SampleTime.AddMinutes(5), id: newerId));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.NotNull(found);
        Assert.Equal(newerId, found.Id);
    }

    /// <summary>The active session is returned tracked as well (a precondition for submit/edit).</summary>
    [Fact]
    public async Task FindActiveSessionAsync_returns_a_tracked_session()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1"));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.NotNull(session);
        Assert.Equal(EntityState.Unchanged, readContext.Entry(session).State);
    }

    // ---- GetTriggersForSessionAsync (#42) ---------------------------------------------------

    /// <summary>
    /// Returns exactly the triggers of the dialog the session hangs on – and only those of the
    /// requested point in time. The basis of the webhook delivery, which knows only the SessionId from
    /// the notification.
    /// </summary>
    [Fact]
    public async Task GetTriggersForSessionAsync_filters_on_the_sessions_dialog_and_the_scope()
    {
        var dialogId = Guid.NewGuid();
        var otherDialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            var dialog = TestDialogFactory.NewDialog("triggers", version: 1, name: "Triggers");
            dialog.Id = dialogId;
            dialog.Triggers.Add(NewTrigger(dialogId, TriggerScope.OnDialogCompleted, "https://example.test/fertig"));
            dialog.Triggers.Add(NewTrigger(dialogId, TriggerScope.AfterAnswer, "https://example.test/antwort"));

            var other = TestDialogFactory.NewDialog("other", version: 1, name: "Other");
            other.Id = otherDialogId;
            other.Triggers.Add(
                NewTrigger(otherDialogId, TriggerScope.OnDialogCompleted, "https://example.test/other"));

            context.Dialogs.Add(dialog);
            context.Dialogs.Add(other);
            context.DialogSessions.Add(NewSession(dialogId, "user-1", id: sessionId));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var triggers = await new DialogStore(readContext)
            .GetTriggersForSessionAsync(sessionId, TriggerScope.OnDialogCompleted);

        var trigger = Assert.Single(triggers);
        Assert.Equal(dialogId, trigger.DialogId);
        Assert.Contains("fertig", trigger.Config, StringComparison.Ordinal);
    }

    /// <summary>An unknown session returns an empty list instead of throwing.</summary>
    [Fact]
    public async Task GetTriggersForSessionAsync_an_unknown_session_returns_an_empty_list()
    {
        using var readContext = CreateContext();
        var triggers = await new DialogStore(readContext)
            .GetTriggersForSessionAsync(Guid.NewGuid(), TriggerScope.OnDialogCompleted);

        Assert.Empty(triggers);
    }

    // ---- AddSession + SaveChangesAsync (Unit of Work) ---------------------------------------

    /// <summary>A newly added session is persisted incl. its answers only on <c>SaveChangesAsync</c>.</summary>
    [Fact]
    public async Task AddSession_and_SaveChangesAsync_persist_a_new_session()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            var store = new DialogStore(context);
            var session = NewSession(Guid.NewGuid(), "user-1", id: sessionId);
            session.CurrentQuestionId = questionId;
            session.Answers.Add(Answer(sessionId, questionId, "\"hello\"", sequence: 0));

            store.AddSession(session);
            await store.SaveChangesAsync();
        }

        using var readContext = CreateContext();
        var loaded = readContext.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal(questionId, loaded.CurrentQuestionId);
        Assert.Single(loaded.Answers);
    }

    /// <summary>Mutations on a tracked session (a new answer, a status change) are saved.</summary>
    [Fact]
    public async Task SaveChangesAsync_persists_mutations_of_a_tracked_session()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(Guid.NewGuid(), "user-1", id: sessionId));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var store = new DialogStore(context);
            var session = await store.GetSessionAsync(sessionId);
            Assert.NotNull(session);

            // When attaching to an already tracked session, do NOT pre-set the id: the Guid key is
            // store-generated (EF convention), and a pre-set id on a child of a tracked parent would be
            // interpreted as an update instead of an insert. EF assigns the key at SaveChanges.
            session.Answers.Add(new SessionAnswer
            {
                SessionId = sessionId, QuestionId = questionId, Value = "\"done\"",
                AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
            });
            session.Status = SessionStatus.Completed;
            session.CompletedAt = TestDialogFactory.SampleTime.AddMinutes(10);
            session.CurrentQuestionId = null;

            await store.SaveChangesAsync();
        }

        using var readContext = CreateContext();
        var reloaded = readContext.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.Completed, reloaded.Status);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.Null(reloaded.CurrentQuestionId);
        Assert.Single(reloaded.Answers);
    }

    // ---- Constructor + DI -------------------------------------------------------------------

    /// <summary>The constructor rejects a <c>null</c> context.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_context()
        => Assert.Throws<ArgumentNullException>(() => new DialogStore(null!));

    /// <summary><c>AddFlirty()</c> registers <see cref="IDialogStore"/> as a scoped <see cref="DialogStore"/>.</summary>
    [Fact]
    public void AddFlirty_registers_IDialogStore()
    {
        using var provider = new ServiceCollection()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDialogStore>();

        Assert.IsType<DialogStore>(store);
    }

    // ---- Test-data helpers ------------------------------------------------------------------

    private static Dialog PublishedDialog(string key, int version)
    {
        var dialog = TestDialogFactory.NewDialog(key, version, name: $"{key} v{version}");
        dialog.IsPublished = true;
        return dialog;
    }

    private static Dialog UnpublishedDialog(string key, int version)
        => TestDialogFactory.NewDialog(key, version, name: $"{key} v{version} (Entwurf)");

    private static TriggerDefinition NewTrigger(Guid dialogId, TriggerScope scope, string url) => new()
    {
        Id = Guid.NewGuid(),
        DialogId = dialogId,
        Scope = scope,
        Kind = TriggerKind.Webhook,
        Config = $"{{\"url\":\"{url}\"}}",
    };

    private static DialogSession NewSession(
        Guid dialogId,
        string externalUserKey,
        SessionStatus status = SessionStatus.InProgress,
        DateTimeOffset? startedAt = null,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        DialogId = dialogId,
        DialogVersion = 1,
        ExternalUserKey = externalUserKey,
        Status = status,
        StartedAt = startedAt ?? TestDialogFactory.SampleTime,
    };

    private static SessionAnswer Answer(
        Guid sessionId,
        Guid questionId,
        string value,
        int sequence,
        Guid? loopInstanceId = null,
        int? iterationIndex = null) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        QuestionId = questionId,
        Value = value,
        AnsweredAt = TestDialogFactory.SampleTime,
        Sequence = sequence,
        LoopInstanceId = loopInstanceId,
        IterationIndex = iterationIndex,
    };
}
