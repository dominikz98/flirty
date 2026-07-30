using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the dialog versioning: deriving a new version
/// (<see cref="CreateDialogVersionCommandHandler"/>), the immutability of a published version
/// (<see cref="DialogEditGuard"/>) and – as the <b>core check</b> – that a running session runs to
/// completion untouched by a newly published version. That is exactly what <c>docs/RUNTIME.md</c> and
/// <c>docs/ARCHITECTURE.md</c> promise; before, the promise did not hold, because there was no way to
/// a second version at all and changes hit the same row the session loads its graph from.
/// </summary>
public sealed class DialogVersioningTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Opens the SQLite in-memory connection and creates the schema.</summary>
    public DialogVersioningTests()
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

    // ---- Deriving a new version -------------------------------------------------------------

    /// <summary>
    /// The copy carries the same key, the next version number and is a <b>draft</b> – two published
    /// versions would not be unambiguous for <c>StartDialogCommand</c>.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_creates_the_follow_up_version_as_a_draft()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out _));

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        Assert.Equal("onboarding", copy.Dialog.Key);
        Assert.Equal(2, copy.Dialog.Version);
        Assert.False(copy.Dialog.IsPublished);
        Assert.NotEqual(dialogId, copy.Dialog.Id);

        // The source stays published, unchanged.
        using var assert = CreateContext();
        var source = await assert.Dialogs.AsNoTracking().FirstAsync(dialog => dialog.Id == dialogId);
        Assert.True(source.IsPublished);
        Assert.Equal(1, source.Version);
    }

    /// <summary>
    /// The whole graph is copied – with <b>new</b> ids throughout, so that the source stays untouched.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_clones_the_whole_graph_with_new_ids()
    {
        var dialogId = Guid.NewGuid();
        var source = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        Seed(source);

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        var question = Assert.Single(copy.Questions);
        Assert.Equal("role", question.Key);
        Assert.NotEqual(questionId, question.Id);
        Assert.Equal(copy.Dialog.Id, question.DialogId);
        Assert.Equal("{\"maxLength\":50}", question.ValidationRules);
        Assert.Equal(2, question.Options.Count);
        Assert.All(question.Options, option => Assert.Equal(question.Id, option.QuestionId));
        Assert.Equal(["dev", "pm"], question.Options.Select(option => option.Value));

        Assert.Single(copy.Transitions);
        Assert.Single(copy.Loops);
        var trigger = Assert.Single(copy.Triggers);
        Assert.Equal("{\"url\":\"https://example.test/hook\"}", trigger.Config);

        // Not a single child shares its id with the source.
        using var assert = CreateContext();
        var sourceIds = await assert.Set<Question>().AsNoTracking()
            .Where(entity => entity.DialogId == dialogId).Select(entity => entity.Id).ToListAsync();
        Assert.DoesNotContain(question.Id, sourceIds);
    }

    /// <summary>
    /// After the clone, all question references (entry question, transitions, loop markers, triggers)
    /// point at the <b>copies</b>. Without that rewriting the new version would point at the old
    /// version's questions – the dialog would be unusable.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_rewrites_the_question_references_onto_the_copies()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildLoopDialog(dialogId, out var ids));

        // A trigger with a question reference, so that this reference is checked too.
        using (var arrange = CreateContext())
        {
            arrange.Set<TriggerDefinition>().Add(new TriggerDefinition
            {
                Id = Guid.NewGuid(),
                DialogId = dialogId,
                Scope = TriggerScope.AfterQuestion,
                QuestionId = ids.MoreQuestionId,
                Kind = TriggerKind.InProcess,
                Config = "{}",
            });
            arrange.SaveChanges();
        }

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        var copiedIds = copy.Questions.Select(question => question.Id).ToHashSet();
        var position = copy.Questions.Single(question => question.Key == "position");
        var more = copy.Questions.Single(question => question.Key == "more");

        Assert.Equal(position.Id, copy.Dialog.StartQuestionId);
        Assert.All(copy.Transitions, transition =>
        {
            Assert.Contains(transition.FromQuestionId, copiedIds);
            Assert.Contains(transition.TargetQuestionId, copiedIds);
        });

        var loop = Assert.Single(copy.Loops);
        Assert.Equal("positions", loop.CollectionKey);
        Assert.Equal(position.Id, loop.EntryQuestionId);
        Assert.Equal(more.Id, loop.BreakingQuestionId);

        var trigger = copy.Triggers.Single(entry => entry.Scope == TriggerScope.AfterQuestion);
        Assert.Equal(more.Id, trigger.QuestionId);

        // The cycle is preserved: the back jump from 'more' to 'position' incl. its condition.
        Assert.Contains(
            copy.Transitions,
            transition => transition.FromQuestionId == more.Id
                       && transition.TargetQuestionId == position.Id
                       && transition.Expression == "more == \"yes\"");
    }

    /// <summary>Deriving repeatedly keeps counting up (version 2, then 3).</summary>
    [Fact]
    public async Task CreateDialogVersion_counts_on_from_the_highest_version()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out _));

        DialogDetail zweite;
        DialogDetail dritte;
        using (var act = CreateContext())
        {
            var handler = new CreateDialogVersionCommandHandler(new DialogAdminStore(act));
            zweite = await handler.Handle(new CreateDialogVersionCommand(dialogId), default);
            dritte = await handler.Handle(new CreateDialogVersionCommand(zweite.Dialog.Id), default);
        }

        Assert.Equal(2, zweite.Dialog.Version);
        Assert.Equal(3, dritte.Dialog.Version);
    }

    /// <summary>An unknown dialog is reported as not-found.</summary>
    [Fact]
    public async Task CreateDialogVersion_reports_an_unknown_dialog()
    {
        using var act = CreateContext();
        var handler = new CreateDialogVersionCommandHandler(new DialogAdminStore(act));

        await Assert.ThrowsAsync<ConfigurationNotFoundException>(
            async () => await handler.Handle(new CreateDialogVersionCommand(Guid.NewGuid()), default));
    }

    // ---- The published version's lock -------------------------------------------------------

    /// <summary>
    /// Every graph change on a published version is rejected. One command per element kind is
    /// checked – all of them run through the <see cref="DialogEditGuard"/>.
    /// </summary>
    [Fact]
    public async Task Graph_changes_on_a_published_version_are_rejected()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        var optionId = dialog.Questions.Single().Options.First().Id;
        var transitionId = dialog.Transitions.Single().Id;
        var loopId = dialog.Loops.Single().Id;
        var triggerId = dialog.Triggers.Single().Id;
        Seed(dialog);

        using var act = CreateContext();
        var store = new DialogAdminStore(act);

        await AssertPublishedAsync(() => new CreateQuestionCommandHandler(store)
            .Handle(new CreateQuestionCommand(dialogId, "fresh", "Fresh?", QuestionType.FreeText, 9, false, null), default));
        await AssertPublishedAsync(() => new UpdateQuestionCommandHandler(store)
            .Handle(new UpdateQuestionCommand(dialogId, questionId, "role", "Changed?", QuestionType.FreeText, 0, true, null), default));
        await AssertPublishedAsync(() => new DeleteQuestionCommandHandler(store)
            .Handle(new DeleteQuestionCommand(dialogId, questionId), default));
        await AssertPublishedAsync(() => new CreateAnswerOptionCommandHandler(store)
            .Handle(new CreateAnswerOptionCommand(dialogId, questionId, "x", "X", "x", 2), default));
        await AssertPublishedAsync(() => new UpdateAnswerOptionCommandHandler(store)
            .Handle(new UpdateAnswerOptionCommand(dialogId, questionId, optionId, "dev", "Dev", "dev", 0), default));
        await AssertPublishedAsync(() => new DeleteAnswerOptionCommandHandler(store)
            .Handle(new DeleteAnswerOptionCommand(dialogId, questionId, optionId), default));
        await AssertPublishedAsync(() => new CreateTransitionCommandHandler(store)
            .Handle(new CreateTransitionCommand(dialogId, questionId, questionId, null, 0, true), default));
        await AssertPublishedAsync(() => new UpdateTransitionCommandHandler(store)
            .Handle(new UpdateTransitionCommand(dialogId, transitionId, questionId, questionId, null, 0, true), default));
        await AssertPublishedAsync(() => new DeleteTransitionCommandHandler(store)
            .Handle(new DeleteTransitionCommand(dialogId, transitionId), default));
        await AssertPublishedAsync(() => new CreateLoopCommandHandler(store)
            .Handle(new CreateLoopCommand(dialogId, "weitere", questionId, questionId), default));
        await AssertPublishedAsync(() => new UpdateLoopCommandHandler(store)
            .Handle(new UpdateLoopCommand(dialogId, loopId, "positions", questionId, questionId), default));
        await AssertPublishedAsync(() => new DeleteLoopCommandHandler(store)
            .Handle(new DeleteLoopCommand(dialogId, loopId), default));
        await AssertPublishedAsync(() => new CreateTriggerCommandHandler(store)
            .Handle(new CreateTriggerCommand(dialogId, TriggerScope.OnDialogStarted, null, TriggerKind.InProcess, "{}", null), default));
        await AssertPublishedAsync(() => new UpdateTriggerCommandHandler(store)
            .Handle(new UpdateTriggerCommand(dialogId, triggerId, TriggerScope.OnDialogStarted, null, TriggerKind.InProcess, "{}", null), default));
        await AssertPublishedAsync(() => new DeleteTriggerCommandHandler(store)
            .Handle(new DeleteTriggerCommand(dialogId, triggerId), default));
    }

    /// <summary>
    /// The same changes take effect on the <b>draft</b> – the lock hangs on the publication status,
    /// not on the version.
    /// </summary>
    [Fact]
    public async Task Graph_changes_on_a_draft_take_effect()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out _);
        dialog.IsPublished = false;
        Seed(dialog);

        using var act = CreateContext();
        var created = await new CreateQuestionCommandHandler(new DialogAdminStore(act))
            .Handle(new CreateQuestionCommand(dialogId, "fresh", "Fresh?", QuestionType.FreeText, 9, false, null), default);

        Assert.Equal("fresh", created.Key);
    }

    /// <summary>
    /// Name and description stay changeable on a published version too (purely descriptive), changing
    /// the <b>entry question</b> does not (it is part of the flow).
    /// </summary>
    [Fact]
    public async Task UpdateDialog_allows_metadata_and_locks_the_entry_question()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out var questionId));

        using var act = CreateContext();
        var handler = new UpdateDialogCommandHandler(new DialogAdminStore(act));

        var renamed = await handler.Handle(
            new UpdateDialogCommand(dialogId, "onboarding", "Neuer Name", "Neue Beschreibung", questionId), default);
        Assert.Equal("Neuer Name", renamed.Name);

        await AssertPublishedAsync(() => handler.Handle(
            new UpdateDialogCommand(dialogId, "onboarding", "Neuer Name", null, null), default));
    }

    /// <summary>
    /// The key identifies the dialog family. Changing it on only one of several versions would tear
    /// the series apart – that is rejected.
    /// </summary>
    [Fact]
    public async Task UpdateDialog_rejects_a_rename_with_several_versions()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        dialog.IsPublished = false;
        Seed(dialog);

        using var act = CreateContext();
        var store = new DialogAdminStore(act);
        await new CreateDialogVersionCommandHandler(store)
            .Handle(new CreateDialogVersionCommand(dialogId), default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new UpdateDialogCommandHandler(store).Handle(
                new UpdateDialogCommand(dialogId, "anders", "Onboarding", null, questionId), default));

        Assert.Contains("multiple versions", exception.Message);
    }

    /// <summary>A single dialog can still be renamed.</summary>
    [Fact]
    public async Task UpdateDialog_allows_a_rename_with_a_single_version()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        dialog.IsPublished = false;
        Seed(dialog);

        using var act = CreateContext();
        var updated = await new UpdateDialogCommandHandler(new DialogAdminStore(act)).Handle(
            new UpdateDialogCommand(dialogId, "anders", "Onboarding", null, questionId), default);

        Assert.Equal("anders", updated.Key);
    }

    // ---- Publishing --------------------------------------------------------------------------

    /// <summary>
    /// At most one version per key is in production: publishing the new version retires the previous
    /// one (otherwise it would carry a status that no longer has any effect).
    /// </summary>
    [Fact]
    public async Task Publish_retires_the_previous_version()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out _));

        Guid secondId;
        using (var act = CreateContext())
        {
            var store = new DialogAdminStore(act);
            var copy = await new CreateDialogVersionCommandHandler(store)
                .Handle(new CreateDialogVersionCommand(dialogId), default);
            secondId = copy.Dialog.Id;

            await new PublishDialogCommandHandler(store).Handle(new PublishDialogCommand(secondId), default);
        }

        using var assert = CreateContext();
        var versions = await assert.Dialogs.AsNoTracking()
            .Where(dialog => dialog.Key == "onboarding")
            .ToDictionaryAsync(dialog => dialog.Version, dialog => dialog.IsPublished);

        Assert.False(versions[1]);
        Assert.True(versions[2]);
    }

    /// <summary>
    /// <b>The core check of the promise:</b> a running session of version 1 runs to completion
    /// unchanged while version 2 is derived, changed and published. A new user, by contrast, starts
    /// on version 2.
    /// </summary>
    [Fact]
    public async Task A_running_session_survives_a_newly_published_version()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));

        // 1. Start a session on version 1 (it sits on the choice question 'role').
        StartDialogResult start;
        using (var act = CreateContext())
        {
            start = await new StartDialogCommandHandler(new DialogStore(act), new SpyPublisher())
                .Handle(new StartDialogCommand("branching", "user-1"), default);
        }

        Assert.Equal("role", start.CurrentQuestion!.Key);

        // 2. Derive version 2, rebuild the choice question there and publish it.
        Guid secondId;
        using (var act = CreateContext())
        {
            var store = new DialogAdminStore(act);
            var copy = await new CreateDialogVersionCommandHandler(store)
                .Handle(new CreateDialogVersionCommand(dialogId), default);
            secondId = copy.Dialog.Id;

            var roleCopy = copy.Questions.Single(question => question.Key == "role");
            await new UpdateQuestionCommandHandler(store).Handle(
                new UpdateQuestionCommand(
                    secondId, roleCopy.Id, "role", "A completely different question?", QuestionType.FreeText, 0, true, null),
                default);

            await new PublishDialogCommandHandler(store).Handle(new PublishDialogCommand(secondId), default);
        }

        // 3. The running session keeps answering – on its old graph.
        SubmitAnswerResult submit;
        using (var act = CreateContext())
        {
            submit = await new SubmitAnswerCommandHandler(
                    new DialogStore(act), new DynamicExpressoExpressionEvaluator(), new SpyPublisher())
                .Handle(new SubmitAnswerCommand(start.SessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.Equal("devDetail", submit.NextQuestion!.Key);

        // The session is reachable for reading unchanged too (that used to be the 409 case).
        ResumeDialogResult resumed;
        using (var act = CreateContext())
        {
            resumed = await new ResumeDialogQueryHandler(new DialogStore(act))
                .Handle(new ResumeDialogQuery(start.SessionId), default);
        }

        Assert.Equal(SessionStatus.InProgress, resumed.Status);
        Assert.Equal("devDetail", resumed.CurrentQuestion!.Key);

        // 4. A new user lands on version 2 – with the changed question text.
        StartDialogResult freshRun;
        using (var act = CreateContext())
        {
            freshRun = await new StartDialogCommandHandler(new DialogStore(act), new SpyPublisher())
                .Handle(new StartDialogCommand("branching", "user-2"), default);
        }

        Assert.Equal("A completely different question?", freshRun.CurrentQuestion!.Text);
    }

    // ---- Deleting and ending sessions --------------------------------------------------------

    /// <summary>
    /// As long as sessions are running, deletion is rejected – they would survive the dialog as
    /// unreadable orphans.
    /// </summary>
    [Fact]
    public async Task DeleteDialog_is_rejected_with_a_running_session()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.InProgress);

        using var act = CreateContext();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new DeleteDialogCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteDialogCommand(dialogId), default));

        Assert.Contains("1 session(s)", exception.Message);
    }

    /// <summary>Completed and abandoned sessions do not block the deletion.</summary>
    [Fact]
    public async Task DeleteDialog_works_with_finished_sessions()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.Completed);
        SeedSession(dialogId, SessionStatus.Abandoned);

        using (var act = CreateContext())
        {
            await new DeleteDialogCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteDialogCommand(dialogId), default);
        }

        using var assert = CreateContext();
        Assert.Empty(await assert.Dialogs.AsNoTracking().Where(dialog => dialog.Id == dialogId).ToListAsync());
    }

    /// <summary>
    /// The abandon ends the running sessions (their answers are preserved) and thereby clears the way
    /// for the deletion.
    /// </summary>
    [Fact]
    public async Task AbandonDialogSessions_ends_the_sessions_and_releases_the_deletion()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.InProgress);
        SeedSession(dialogId, SessionStatus.InProgress);

        AbandonSessionsResult result;
        using (var act = CreateContext())
        {
            result = await new AbandonDialogSessionsCommandHandler(new DialogAdminStore(act))
                .Handle(new AbandonDialogSessionsCommand(dialogId), default);
        }

        Assert.Equal(2, result.AbandonedSessions);

        using (var assert = CreateContext())
        {
            var sessions = await assert.DialogSessions.AsNoTracking()
                .Where(session => session.DialogId == dialogId).ToListAsync();
            Assert.All(sessions, session => Assert.Equal(SessionStatus.Abandoned, session.Status));
            Assert.All(sessions, session => Assert.NotNull(session.CompletedAt));
        }

        using (var act = CreateContext())
        {
            await new DeleteDialogCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteDialogCommand(dialogId), default);
        }

        using var assertDeleted = CreateContext();
        Assert.Empty(await assertDeleted.Dialogs.AsNoTracking().Where(dialog => dialog.Id == dialogId).ToListAsync());
    }

    /// <summary>Without running sessions the abandon is a no-op.</summary>
    [Fact]
    public async Task AbandonDialogSessions_reports_zero_when_no_session_is_running()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));

        using var act = CreateContext();
        var result = await new AbandonDialogSessionsCommandHandler(new DialogAdminStore(act))
            .Handle(new AbandonDialogSessionsCommand(dialogId), default);

        Assert.Equal(0, result.AbandonedSessions);
    }

    /// <summary>The counting query considers only running sessions.</summary>
    [Fact]
    public async Task CountActiveSessions_counts_only_running_sessions()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.InProgress);
        SeedSession(dialogId, SessionStatus.Completed);
        SeedSession(dialogId, SessionStatus.Abandoned);

        using var act = CreateContext();
        var count = await new CountActiveSessionsQueryHandler(new DialogAdminStore(act))
            .Handle(new CountActiveSessionsQuery(dialogId), default);

        Assert.Equal(1, count);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private void Seed(Dialog dialog)
    {
        using var arrange = CreateContext();
        arrange.Dialogs.Add(dialog);
        arrange.SaveChanges();
    }

    private void SeedSession(Guid dialogId, SessionStatus status)
    {
        using var arrange = CreateContext();
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = Guid.NewGuid(),
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = $"user-{Guid.NewGuid():N}",
            Status = status,
            StartedAt = TestDialogFactory.SampleTime,
            CompletedAt = status == SessionStatus.InProgress ? null : TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();
    }

    private static async Task AssertPublishedAsync<TResult>(Func<ValueTask<TResult>> operation)
    {
        var exception = await Assert.ThrowsAsync<DialogPublishedException>(async () => await operation());
        Assert.Contains("published", exception.Message);
    }
}
