using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="FlirtyAdminGateway"/> (#38): executing the admin CRUD messages against
/// the active connection profile, the error mapping onto displayable messages and – as a regression –
/// that a profile switch takes effect immediately (otherwise the scoped
/// <see cref="FlirtyDbContext"/> lives for the whole Blazor circuit and would stay pinned to the
/// first profile used).
/// </summary>
public sealed class FlirtyAdminGatewayTests
{
    [Fact]
    public async Task ExecuteAsync_creates_the_dialog_and_returns_it_in_the_list()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);

            var created = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateDialogCommand("onboarding", "Onboarding", "Beschreibung"), token));

            Assert.True(created.Success, created.Error);
            Assert.Equal("onboarding", created.Value!.Key);
            Assert.False(created.Value.IsPublished);

            var listed = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));

            Assert.True(listed.Success, listed.Error);
            Assert.Contains(listed.Value!, dialog => dialog.Id == created.Value.Id);
        });
    }

    [Fact]
    public async Task ExecuteAsync_reports_a_conflict_on_a_duplicate_key()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateDialogCommand("onboarding", "Onboarding", null), token));

            var second = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateDialogCommand("onboarding", "Noch mal", null), token));

            Assert.False(second.Success);
            Assert.Contains("onboarding", second.Error);
        });
    }

    [Fact]
    public async Task ExecuteAsync_reports_an_unknown_dialog()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);

            var result = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new GetDialogQuery(Guid.NewGuid()), token));

            Assert.False(result.Success);
            Assert.Contains("No dialog", result.Error);
        });
    }

    [Fact]
    public async Task ExecuteAsync_reports_a_missing_connection_profile()
    {
        await RunWithTempDbAsync(async (gateway, _, _) =>
        {
            // Deliberately NO Activate: the FlirtyDesignerDbContextFactory has to report understandably.
            var result = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));

            Assert.False(result.Success);
            Assert.Contains("Connections", result.Error);
        });
    }

    [Fact]
    public async Task ExecuteAsync_reports_a_database_that_was_not_migrated()
    {
        var store = new DesignerTestHost.InMemoryConnectionProfileStore();
        using var provider = DesignerTestHost.BuildProvider(store);
        using var scope = provider.CreateScope();

        // A reachable but empty database (Mode=Memory -> no file litter): the schema is missing
        // because it was never migrated. SQLite reports "no such table: Dialogs" -> that has to
        // arrive as a hint pointing at "Migrate".
        var profile = new ConnectionProfile
        {
            Name = "Nicht migriert",
            Provider = FlirtyDatabaseProvider.Sqlite,
            ConnectionString = $"Data Source=nicht-migriert-{Guid.NewGuid():N};Mode=Memory;Pooling=False",
        };
        scope.ServiceProvider.GetRequiredService<ActiveConnectionProfile>().Activate(profile);

        var result = await scope.ServiceProvider.GetRequiredService<FlirtyAdminGateway>()
            .ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));

        Assert.False(result.Success);
        Assert.Contains("Migrate", result.Error);
    }

    /// <summary>
    /// Regression for the Blazor circuit problem: after a profile switch the next operation has to
    /// run against the <b>new</b> database. That only works because the gateway opens a fresh DI
    /// scope (and thereby a fresh <see cref="FlirtyDbContext"/>) per operation.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_uses_the_new_database_after_a_profile_switch()
    {
        await RunWithTempDbAsync(async (gateway, active, profilA) =>
        {
            await RunWithTempDbAsync(async (_, _, profilB) =>
            {
                active.Activate(profilA);
                var created = await gateway.ExecuteAsync((sender, token) =>
                    sender.Send(new CreateDialogCommand("nur-in-a", "Nur in A", null), token));
                Assert.True(created.Success, created.Error);

                active.Activate(profilB);
                var inB = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
                Assert.True(inB.Success, inB.Error);
                Assert.Empty(inB.Value!);

                active.Activate(profilA);
                var inA = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
                Assert.True(inA.Success, inA.Error);
                Assert.Single(inA.Value!);
            });
        });
    }

    /// <summary>
    /// Question editor (#39): questions and answer options run over the same admin commands and have
    /// to reappear sorted in the <c>GetDialogQuery</c> – the question list's display builds on that.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_creates_a_question_with_options_and_returns_them_sorted_in_the_dialog_graph()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");

            var question = await gateway.ExecuteAsync((sender, token) => sender.Send(
                new CreateQuestionCommand(
                    dialogId, "farbe", "Welche Farbe?", QuestionType.SingleChoice, 0, true, null),
                token));
            Assert.True(question.Success, question.Error);

            // Deliberately created in a twisted order: the projection has to sort by Order.
            foreach (var (key, order) in new[] { ("gruen", 1), ("rot", 0) })
            {
                var option = await gateway.ExecuteAsync((sender, token) => sender.Send(
                    new CreateAnswerOptionCommand(dialogId, question.Value!.Id, key, key, key, order), token));
                Assert.True(option.Success, option.Error);
            }

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            var loaded = Assert.Single(detail.Value!.Questions);
            Assert.Equal(QuestionType.SingleChoice, loaded.Type);
            Assert.Equal(["rot", "gruen"], loaded.Options.Select(option => option.Key));
        });
    }

    /// <summary>
    /// When sorting, the question editor writes several <c>UpdateQuestionCommand</c>s in <b>one</b>
    /// <see cref="FlirtyAdminGateway.ExecuteAsync{TValue}"/> call (one scope, one error path).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_swaps_the_order_of_two_questions_in_one_operation()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");

            var first = await CreateQuestionAsync(gateway, dialogId, "firstname", 0);
            var second = await CreateQuestionAsync(gateway, dialogId, "age", 1);

            var swapped = await gateway.ExecuteAsync(async (sender, token) =>
            {
                _ = await sender.Send(
                    new UpdateQuestionCommand(
                        dialogId, second.Id, second.Key, second.Text, second.Type, 0, second.IsRequired, null),
                    token);
                return await sender.Send(
                    new UpdateQuestionCommand(
                        dialogId, first.Id, first.Key, first.Text, first.Type, 1, first.IsRequired, null),
                    token);
            });
            Assert.True(swapped.Success, swapped.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Equal(["age", "firstname"], detail.Value!.Questions.Select(question => question.Key));
        });
    }

    /// <summary>
    /// If the question editor deletes the entry question, the dialog has to stand without an entry
    /// question again – the view then locks publishing.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_resets_the_entry_question_when_it_is_deleted()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var question = await CreateQuestionAsync(gateway, dialogId, "firstname", 0);

            var assigned = await gateway.ExecuteAsync((sender, token) => sender.Send(
                new UpdateDialogCommand(dialogId, "onboarding", "Onboarding", null, question.Id), token));
            Assert.True(assigned.Success, assigned.Error);
            Assert.Equal(question.Id, assigned.Value!.StartQuestionId);

            var deleted = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteQuestionCommand(dialogId, question.Id), token));
            Assert.True(deleted.Success, deleted.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Null(detail.Value!.Dialog.StartQuestionId);
            Assert.Empty(detail.Value.Questions);
        });
    }

    /// <summary>
    /// Branching editor (#40): transitions run over the same admin commands and have to reappear in
    /// the <c>GetDialogQuery</c> with their condition, priority and default flag – the dialog
    /// editor's transition list builds on that.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_creates_a_transition_and_deletes_it_again()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var role = await CreateQuestionAsync(gateway, dialogId, "role", 0);
            var language = await CreateQuestionAsync(gateway, dialogId, "language", 1);

            var created = await gateway.ExecuteAsync((sender, token) => sender.Send(
                new CreateTransitionCommand(dialogId, role.Id, language.Id, "role == \"dev\"", 0, false), token));
            Assert.True(created.Success, created.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));
            Assert.True(detail.Success, detail.Error);

            var loaded = Assert.Single(detail.Value!.Transitions);
            Assert.Equal(role.Id, loaded.FromQuestionId);
            Assert.Equal(language.Id, loaded.TargetQuestionId);
            Assert.Equal("role == \"dev\"", loaded.Expression);
            Assert.False(loaded.IsDefault);

            var deleted = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteTransitionCommand(dialogId, loaded.Id), token));
            Assert.True(deleted.Success, deleted.Error);

            var afterwards = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));
            Assert.Empty(afterwards.Value!.Transitions);
        });
    }

    /// <summary>
    /// The up/down buttons write the position index as the new <c>Priority</c> – in <b>one</b>
    /// gateway operation. The test deliberately starts with gappy priorities (5/9): merely swapping
    /// the numbers would have no effect here.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_reassigns_the_transition_priorities_in_one_operation()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var role = await CreateQuestionAsync(gateway, dialogId, "role", 0);
            var language = await CreateQuestionAsync(gateway, dialogId, "language", 1);
            var product = await CreateQuestionAsync(gateway, dialogId, "product", 2);

            var conditional = await CreateTransitionAsync(gateway, dialogId, role.Id, language.Id, "role == \"dev\"", 5, false);
            var standard = await CreateTransitionAsync(gateway, dialogId, role.Id, product.Id, null, 9, true);

            var sorted = await gateway.ExecuteAsync(async (sender, token) =>
            {
                _ = await sender.Send(
                    new UpdateTransitionCommand(
                        dialogId, standard.Id, standard.FromQuestionId, standard.TargetQuestionId,
                        standard.Expression, 0, standard.IsDefault),
                    token);
                return await sender.Send(
                    new UpdateTransitionCommand(
                        dialogId, conditional.Id, conditional.FromQuestionId, conditional.TargetQuestionId,
                        conditional.Expression, 1, conditional.IsDefault),
                    token);
            });
            Assert.True(sorted.Success, sorted.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Equal([standard.Id, conditional.Id], detail.Value!.Transitions.Select(transition => transition.Id));
            Assert.Equal([0, 1], detail.Value.Transitions.Select(transition => transition.Priority));
        });
    }

    /// <summary>
    /// The expression validation's sample context needs the loop collections, otherwise
    /// <c>skills.Count &gt; 0</c> would count as an unknown identifier in the designer. That is why
    /// the whole path of the loop CRUD (#41) is checked: create, find again in the dialog graph,
    /// change and delete.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_creates_a_loop_marker_changes_it_and_deletes_it()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var skill = await CreateQuestionAsync(gateway, dialogId, "skill", 0);
            var more = await CreateQuestionAsync(gateway, dialogId, "more", 1);

            var created = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", skill.Id, more.Id), token));

            Assert.True(created.Success, created.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            var loop = Assert.Single(detail.Value!.Loops);
            Assert.Equal("skills", loop.CollectionKey);
            Assert.Equal(skill.Id, loop.EntryQuestionId);
            Assert.Equal(more.Id, loop.BreakingQuestionId);

            var changed = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new UpdateLoopCommand(dialogId, loop.Id, "faehigkeiten", skill.Id, more.Id), token));

            Assert.True(changed.Success, changed.Error);
            Assert.Equal("faehigkeiten", changed.Value!.CollectionKey);

            var deleted = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteLoopCommand(dialogId, loop.Id), token));

            Assert.True(deleted.Success, deleted.Error);

            var afterwards = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(afterwards.Success, afterwards.Error);
            Assert.Empty(afterwards.Value!.Loops);
        });
    }

    /// <summary>
    /// Two markers with the same collection key would silently overwrite each other at runtime (the
    /// one built last wins in the expression context) – which is why the handler rejects it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_reports_a_conflict_on_a_duplicate_collection_key()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var skill = await CreateQuestionAsync(gateway, dialogId, "skill", 0);
            var more = await CreateQuestionAsync(gateway, dialogId, "more", 1);

            await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", skill.Id, more.Id), token));

            var second = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", more.Id, skill.Id), token));

            Assert.False(second.Success);
            Assert.Contains("skills", second.Error);
        });
    }

    /// <summary>
    /// <c>LoopDefinition</c> references questions FK-free. If a marker stayed on a deleted question,
    /// the <c>LoopResolver</c> would compute at runtime against a range that no longer exists in the
    /// graph – which is why <c>DeleteQuestionCommand</c> clears it along, like the transitions.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_removes_the_loop_marker_when_the_question_is_deleted()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var skill = await CreateQuestionAsync(gateway, dialogId, "skill", 0);
            var more = await CreateQuestionAsync(gateway, dialogId, "more", 1);

            await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", skill.Id, more.Id), token));

            var deleted = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteQuestionCommand(dialogId, more.Id), token));

            Assert.True(deleted.Success, deleted.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Empty(detail.Value!.Loops);
        });
    }

    /// <summary>Creates a dialog over the gateway and returns its id.</summary>
    /// <param name="gateway">The gateway to use.</param>
    /// <param name="key">The dialog's key.</param>
    /// <returns>The id of the created dialog.</returns>
    private static async Task<Guid> CreateDialogAsync(FlirtyAdminGateway gateway, string key)
    {
        var created = await gateway.ExecuteAsync(
            (sender, token) => sender.Send(new CreateDialogCommand(key, key, null), token));

        Assert.True(created.Success, created.Error);
        return created.Value!.Id;
    }

    /// <summary>Creates a free-text question over the gateway.</summary>
    /// <param name="gateway">The gateway to use.</param>
    /// <param name="dialogId">The dialog's id.</param>
    /// <param name="key">The question's key.</param>
    /// <param name="order">The order index.</param>
    /// <returns>The created question.</returns>
    private static async Task<QuestionDetail> CreateQuestionAsync(
        FlirtyAdminGateway gateway, Guid dialogId, string key, int order)
    {
        var created = await gateway.ExecuteAsync((sender, token) => sender.Send(
            new CreateQuestionCommand(dialogId, key, $"Frage {key}?", QuestionType.FreeText, order, false, null),
            token));

        Assert.True(created.Success, created.Error);
        return created.Value!;
    }

    /// <summary>Creates a transition over the gateway.</summary>
    /// <param name="gateway">The gateway to use.</param>
    /// <param name="dialogId">The dialog's id.</param>
    /// <param name="fromQuestionId">The source question.</param>
    /// <param name="targetQuestionId">The target question.</param>
    /// <param name="expression">The optional condition expression.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="isDefault">Whether it is the default transition.</param>
    /// <returns>The created transition.</returns>
    private static async Task<TransitionDetail> CreateTransitionAsync(
        FlirtyAdminGateway gateway, Guid dialogId, Guid fromQuestionId, Guid targetQuestionId,
        string? expression, int priority, bool isDefault)
    {
        var created = await gateway.ExecuteAsync((sender, token) => sender.Send(
            new CreateTransitionCommand(dialogId, fromQuestionId, targetQuestionId, expression, priority, isDefault),
            token));

        Assert.True(created.Success, created.Error);
        return created.Value!;
    }

    /// <summary>
    /// Adapter onto <see cref="DesignerTestHost.RunWithTempDbAsync"/>: resolves the two services used
    /// throughout here from the circuit scope. The DI stack and the temp database live in the shared
    /// <see cref="DesignerTestHost"/>, so that they do not have to be caught up per test class.
    /// </summary>
    /// <param name="test">The test body (gateway, the scope's active profile, the migrated profile).</param>
    private static Task RunWithTempDbAsync(
        Func<FlirtyAdminGateway, ActiveConnectionProfile, ConnectionProfile, Task> test)
        => DesignerTestHost.RunWithTempDbAsync((services, profile) => test(
            services.GetRequiredService<FlirtyAdminGateway>(),
            services.GetRequiredService<ActiveConnectionProfile>(),
            profile));
}
