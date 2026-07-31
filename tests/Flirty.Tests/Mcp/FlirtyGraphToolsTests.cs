using System.Net;
using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos;
using Flirty.Domain;
using Flirty.Mcp;
using Flirty.Mcp.Tools;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Integration tests for the configuration-graph tools of <c>Flirty.Mcp</c> (#127): questions, answer
/// options, transitions, loop markers, triggers and the canvas layout, driven by a real <c>McpClient</c>
/// over an in-process TestServer against a SQLite in-memory database (Docker-free).
/// </summary>
/// <remarks>
/// <para>
/// The section banners are the same as in <see cref="AspNetCore.MapFlirtyAdminEndpointsTests"/> on purpose:
/// one tool class per <c>MapXxxEndpoints</c> counterpart is what makes the parity claim of #127 reviewable
/// file against file, and one test class with the same six banners is the literal mirror of that.
/// </para>
/// <para>
/// Every CRUD walk sets <b>every</b> field to a distinguishable value and reads the graph back. That is not
/// thoroughness for its own sake: a swapped <c>fromQuestionId</c>/<c>targetQuestionId</c> or
/// <c>entryQuestionId</c>/<c>breakingQuestionId</c> is type-correct, so the compiler cannot see it and only
/// a read-back can.
/// </para>
/// </remarks>
public sealed class FlirtyGraphToolsTests
{
    // ---- Question/option CRUD + graph ----

    /// <summary>Question CRUD: creating, changing and deleting a question over the tools.</summary>
    [Fact]
    public async Task Question_CRUD_walks_create_update_and_delete_over_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("questions");

        var created = await host.CallAsync<QuestionDetail>(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "role",
                ["text"] = "Which role?",
                ["type"] = nameof(QuestionType.SingleChoice),
                ["order"] = 0,
                ["isRequired"] = true,
            });

        Assert.Equal(dialog.Id, created.DialogId);
        Assert.Equal("role", created.Key);
        Assert.Equal("Which role?", created.Text);
        Assert.Equal(QuestionType.SingleChoice, created.Type);
        Assert.True(created.IsRequired);
        Assert.Null(created.ValidationRules);
        Assert.Empty(created.Options);

        var updated = await host.CallAsync<QuestionDetail>(
            FlirtyToolNames.QuestionUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["questionId"] = created.Id,
                ["key"] = "job",
                ["text"] = "Which job?",
                ["type"] = nameof(QuestionType.FreeText),
                ["order"] = 3,
                ["isRequired"] = false,
                ["validationRules"] = "{\"minLength\":3,\"maxLength\":50}",
            });

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("job", updated.Key);
        Assert.Equal("Which job?", updated.Text);
        Assert.Equal(QuestionType.FreeText, updated.Type);
        Assert.Equal(3, updated.Order);
        Assert.False(updated.IsRequired);
        Assert.Contains("minLength", updated.ValidationRules);

        Assert.True((await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.QuestionDelete,
            new Dictionary<string, object?> { ["dialogId"] = dialog.Id, ["questionId"] = created.Id }))
            .Succeeded);

        var detail = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Empty(detail.Questions);
    }

    /// <summary>An omitted <c>validationRules</c> on an update clears the stored rules.</summary>
    /// <remarks>
    /// The behaviour the parameter description warns about, pinned: the update is a full overwrite, so this
    /// is a data loss a client can walk into without an error anywhere.
    /// </remarks>
    [Fact]
    public async Task UpdateQuestion_without_validation_rules_clears_the_stored_rules()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("rules");
        var created = await host.CallAsync<QuestionDetail>(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "amount",
                ["text"] = "How many?",
                ["type"] = nameof(QuestionType.Number),
                ["order"] = 0,
                ["isRequired"] = true,
                ["validationRules"] = "{\"min\":0,\"max\":10}",
            });
        Assert.Contains("max", created.ValidationRules);

        var updated = await host.CallAsync<QuestionDetail>(
            FlirtyToolNames.QuestionUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["questionId"] = created.Id,
                ["key"] = "amount",
                ["text"] = "How many?",
                ["type"] = nameof(QuestionType.Number),
                ["order"] = 0,
                ["isRequired"] = true,
            });

        Assert.Null(updated.ValidationRules);
    }

    /// <summary>Creating a question under an unknown dialog is mapped to 404.</summary>
    [Fact]
    public async Task CreateQuestion_under_an_unknown_dialog_reports_404()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = Guid.NewGuid(),
                ["key"] = "role",
                ["text"] = "Which role?",
                ["type"] = nameof(QuestionType.FreeText),
                ["order"] = 0,
                ["isRequired"] = false,
            });

        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    /// <summary>A duplicate question key within the dialog is mapped to 409.</summary>
    [Fact]
    public async Task CreateQuestion_with_a_duplicate_key_reports_409()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("dup-question");
        await host.CreateQuestionAsync(dialog.Id, "role", QuestionType.FreeText, 0);

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "role",
                ["text"] = "Again?",
                ["type"] = nameof(QuestionType.FreeText),
                ["order"] = 1,
                ["isRequired"] = false,
            });

        Assert.Equal(409, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    /// <summary>Answer-option CRUD: creating, changing and deleting an option over the tools.</summary>
    [Fact]
    public async Task AnswerOption_CRUD_walks_create_update_and_delete_over_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("options");
        var question = await host.CreateQuestionAsync(dialog.Id, "role", QuestionType.SingleChoice, 0);

        var created = await host.CreateOptionAsync(
            dialog.Id, question.Id, "dev", "Developer", "dev", 0);
        Assert.Equal(question.Id, created.QuestionId);
        Assert.Equal("Developer", created.Label);
        Assert.Equal("dev", created.Value);

        var updated = await host.CallAsync<AnswerOptionDetail>(
            FlirtyToolNames.OptionUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["questionId"] = question.Id,
                ["optionId"] = created.Id,
                ["key"] = "dev",
                ["label"] = "Software developer",
                ["value"] = "developer",
                ["order"] = 2,
            });

        Assert.Equal("Software developer", updated.Label);
        Assert.Equal("developer", updated.Value);
        Assert.Equal(2, updated.Order);

        await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.OptionDelete,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["questionId"] = question.Id,
                ["optionId"] = created.Id,
            });

        var detail = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Empty(Assert.Single(detail.Questions).Options);
    }

    /// <summary>Creating an option under an unknown question is mapped to 404.</summary>
    [Fact]
    public async Task CreateOption_under_an_unknown_question_reports_404()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("orphan-option");

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.OptionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["questionId"] = Guid.NewGuid(),
                ["key"] = "dev",
                ["label"] = "Developer",
                ["value"] = "dev",
                ["order"] = 0,
            });

        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    /// <summary>
    /// Deleting a question takes its transitions, loop markers, triggers and layout row with it, and clears
    /// the dialog's entry question if it pointed there.
    /// </summary>
    /// <remarks>
    /// The cascade lives in <c>DeleteQuestionCommand</c> and the tool must not re-implement any of it; this
    /// test is the proof that it does not have to. Everything is checked in the one read afterwards.
    /// </remarks>
    [Fact]
    public async Task DeleteQuestion_cleans_up_the_transitions_loops_triggers_and_the_layout_row()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("cascade");
        var first = await host.CreateQuestionAsync(dialog.Id, "first", QuestionType.FreeText, 0);
        var second = await host.CreateQuestionAsync(dialog.Id, "second", QuestionType.FreeText, 1);
        await host.CreateTransitionAsync(dialog.Id, first.Id, second.Id, isDefault: true);
        await host.CreateTransitionAsync(dialog.Id, second.Id, first.Id, isDefault: true);
        await host.CallAsync<LoopDetail>(
            FlirtyToolNames.LoopCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["collectionKey"] = "rounds",
                ["entryQuestionId"] = first.Id,
                ["breakingQuestionId"] = second.Id,
            });
        await host.CallAsync<TriggerDetail>(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.AfterQuestion),
                ["questionId"] = first.Id,
                ["kind"] = nameof(TriggerKind.Webhook),
                ["config"] = "{\"url\":\"https://example.test/hook\"}",
            });
        await host.CallAsync<FlirtyDialogLayoutView>(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[] { LayoutEntry(first.Id, 100, 200) },
            });
        await host.SetStartQuestionAsync(dialog, first.Id);

        await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.QuestionDelete,
            new Dictionary<string, object?> { ["dialogId"] = dialog.Id, ["questionId"] = first.Id });

        var detail = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Equal("second", Assert.Single(detail.Questions).Key);
        Assert.Empty(detail.Transitions);
        Assert.Empty(detail.Loops);
        Assert.Empty(detail.Triggers);
        Assert.Empty(detail.Layout);
        Assert.Null(detail.Dialog.StartQuestionId);
    }

    // ---- Transition CRUD ----

    /// <summary>Transition CRUD: creating, changing and deleting a transition over the tools.</summary>
    [Fact]
    public async Task Transition_CRUD_walks_create_update_and_delete_over_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("transitions");
        var role = await host.CreateQuestionAsync(dialog.Id, "role", QuestionType.FreeText, 0);
        var detailQuestion = await host.CreateQuestionAsync(dialog.Id, "detail", QuestionType.FreeText, 1);
        var other = await host.CreateQuestionAsync(dialog.Id, "other", QuestionType.FreeText, 2);

        var created = await host.CreateTransitionAsync(
            dialog.Id, role.Id, detailQuestion.Id, isDefault: false, expression: "role == \"dev\"");
        Assert.Equal(role.Id, created.FromQuestionId);
        Assert.Equal(detailQuestion.Id, created.TargetQuestionId);
        Assert.Equal("role == \"dev\"", created.Expression);
        Assert.Equal(0, created.Priority);
        Assert.False(created.IsDefault);

        var updated = await host.CallAsync<TransitionDetail>(
            FlirtyToolNames.TransitionUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["transitionId"] = created.Id,
                ["fromQuestionId"] = role.Id,
                ["targetQuestionId"] = other.Id,
                ["priority"] = 5,
                ["isDefault"] = true,
                ["expression"] = "role == \"ops\"",
            });

        Assert.Equal(role.Id, updated.FromQuestionId);
        Assert.Equal(other.Id, updated.TargetQuestionId);
        Assert.Equal(5, updated.Priority);
        Assert.True(updated.IsDefault);
        Assert.Equal("role == \"ops\"", updated.Expression);

        await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.TransitionDelete,
            new Dictionary<string, object?> { ["dialogId"] = dialog.Id, ["transitionId"] = created.Id });

        var graph = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Empty(graph.Transitions);
    }

    /// <summary>Changing an unknown transition is mapped to 404.</summary>
    [Fact]
    public async Task UpdateTransition_of_an_unknown_transition_reports_404()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("unknown-transition");

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.TransitionUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["transitionId"] = Guid.NewGuid(),
                ["fromQuestionId"] = Guid.NewGuid(),
                ["targetQuestionId"] = Guid.NewGuid(),
                ["priority"] = 0,
                ["isDefault"] = true,
            });

        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    // ---- Loop CRUD (#41) ----

    /// <summary>Loop CRUD: creating, changing and deleting a loop marker over the tools.</summary>
    [Fact]
    public async Task Loop_CRUD_walks_create_update_and_delete_over_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("loops");
        var entry = await host.CreateQuestionAsync(dialog.Id, "position", QuestionType.FreeText, 0);
        var breaking = await host.CreateQuestionAsync(dialog.Id, "more", QuestionType.Boolean, 1);

        var created = await host.CallAsync<LoopDetail>(
            FlirtyToolNames.LoopCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["collectionKey"] = "positions",
                ["entryQuestionId"] = entry.Id,
                ["breakingQuestionId"] = breaking.Id,
            });

        Assert.Equal(dialog.Id, created.DialogId);
        Assert.Equal("positions", created.CollectionKey);
        Assert.Equal(entry.Id, created.EntryQuestionId);
        Assert.Equal(breaking.Id, created.BreakingQuestionId);

        var updated = await host.CallAsync<LoopDetail>(
            FlirtyToolNames.LoopUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["loopId"] = created.Id,
                ["collectionKey"] = "roles",
                ["entryQuestionId"] = breaking.Id,
                ["breakingQuestionId"] = entry.Id,
            });

        Assert.Equal("roles", updated.CollectionKey);
        Assert.Equal(breaking.Id, updated.EntryQuestionId);
        Assert.Equal(entry.Id, updated.BreakingQuestionId);

        await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.LoopDelete,
            new Dictionary<string, object?> { ["dialogId"] = dialog.Id, ["loopId"] = created.Id });

        var detail = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Empty(detail.Loops);
    }

    /// <summary>Changing an unknown loop marker is mapped to 404.</summary>
    [Fact]
    public async Task UpdateLoop_of_an_unknown_loop_reports_404()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("unknown-loop");

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.LoopUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["loopId"] = Guid.NewGuid(),
                ["collectionKey"] = "positions",
                ["entryQuestionId"] = Guid.NewGuid(),
                ["breakingQuestionId"] = Guid.NewGuid(),
            });

        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    /// <summary>A duplicate collection key within the dialog is mapped to 409.</summary>
    [Fact]
    public async Task CreateLoop_with_a_duplicate_collection_key_reports_409()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("dup-loop");
        var entry = await host.CreateQuestionAsync(dialog.Id, "position", QuestionType.FreeText, 0);
        var breaking = await host.CreateQuestionAsync(dialog.Id, "more", QuestionType.Boolean, 1);
        await host.CallAsync<LoopDetail>(
            FlirtyToolNames.LoopCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["collectionKey"] = "positions",
                ["entryQuestionId"] = entry.Id,
                ["breakingQuestionId"] = breaking.Id,
            });

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.LoopCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["collectionKey"] = "positions",
                ["entryQuestionId"] = entry.Id,
                ["breakingQuestionId"] = breaking.Id,
            });

        Assert.Equal(409, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    // ---- Trigger CRUD (#42) ----

    /// <summary>
    /// Trigger CRUD: creating, changing and deleting a trigger over the tools. The enums come back as
    /// <b>names</b> – the deliberate divergence from the HTTP surface, where they are integers – and the
    /// configuration JSON survives the round trip verbatim.
    /// </summary>
    [Fact]
    public async Task Trigger_CRUD_walks_create_update_and_delete_over_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("triggers");
        var question = await host.CreateQuestionAsync(dialog.Id, "role", QuestionType.FreeText, 0);

        var created = await host.CallAsync<TriggerDetail>(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.OnDialogCompleted),
                ["kind"] = nameof(TriggerKind.Webhook),
                ["config"] = "{\"url\":\"https://example.test/hook\"}",
            });

        Assert.Equal(TriggerScope.OnDialogCompleted, created.Scope);
        Assert.Equal(TriggerKind.Webhook, created.Kind);
        Assert.Null(created.QuestionId);
        Assert.Equal("{\"url\":\"https://example.test/hook\"}", created.Config);
        Assert.Null(created.Expression);

        var updated = await host.CallAsync<TriggerDetail>(
            FlirtyToolNames.TriggerUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["triggerId"] = created.Id,
                ["scope"] = nameof(TriggerScope.AfterQuestion),
                ["questionId"] = question.Id,
                ["kind"] = nameof(TriggerKind.Webhook),
                ["config"] = "{\"url\":\"https://example.test/other\",\"name\":\"done\"}",
                ["expression"] = "now.Year >= 2026",
            });

        Assert.Equal(TriggerScope.AfterQuestion, updated.Scope);
        Assert.Equal(question.Id, updated.QuestionId);
        Assert.Contains("other", updated.Config);
        Assert.Equal("now.Year >= 2026", updated.Expression);

        await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.TriggerDelete,
            new Dictionary<string, object?> { ["dialogId"] = dialog.Id, ["triggerId"] = created.Id });

        var detail = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Empty(detail.Triggers);
    }

    /// <summary>
    /// The scope enum arrives as a name on the wire, and the returned <c>structuredContent</c> carries a name
    /// too, not the integer the HTTP DTO would serialize.
    /// </summary>
    [Fact]
    public async Task CreateTrigger_reports_its_enums_as_names_in_the_structured_content()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("trigger-enums");

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.OnDialogStarted),
                ["kind"] = nameof(TriggerKind.InProcess),
                ["config"] = "{}",
            });

        Assert.NotNull(result.StructuredContent);
        Assert.Equal(
            nameof(TriggerScope.OnDialogStarted),
            result.StructuredContent.Value.GetProperty("scope").GetString());
        Assert.Equal(
            nameof(TriggerKind.InProcess),
            result.StructuredContent.Value.GetProperty("kind").GetString());
    }

    /// <summary>
    /// Inconsistent trigger requests are mapped to 400 by the commands' own cross-field validation.
    /// </summary>
    /// <remarks>
    /// Three of the five rows the HTTP suite carries. The two left out differ only in <i>which</i>
    /// <c>TriggerConfig</c> rule fires, which is a core concern covered there and in the core tests, not a
    /// transport one. Keeping the same parameter shape makes that shrink visible rather than silent.
    /// </remarks>
    [Theory]
    [InlineData(nameof(TriggerScope.OnDialogCompleted), false, nameof(TriggerKind.Webhook), "not json")]
    [InlineData(nameof(TriggerScope.AfterQuestion), false, nameof(TriggerKind.InProcess), "{}")]
    [InlineData(nameof(TriggerScope.OnDialogStarted), true, nameof(TriggerKind.InProcess), "{}")]
    public async Task CreateTrigger_with_an_inconsistent_request_reports_400(
        string scope, bool withQuestion, string kind, string config)
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync($"bad-{scope}-{withQuestion}-{config.Length}");
        Guid? questionId = withQuestion
            ? (await host.CreateQuestionAsync(dialog.Id, "q", QuestionType.FreeText, 0)).Id
            : null;

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = scope,
                ["questionId"] = questionId,
                ["kind"] = kind,
                ["config"] = config,
            });

        Assert.Equal(400, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    /// <summary>
    /// An <c>InProcess</c> trigger needs <c>{}</c> as its configuration, and an empty string is refused –
    /// the one thing about this area a client cannot guess from the schema, since both are strings.
    /// </summary>
    [Fact]
    public async Task CreateTrigger_accepts_an_empty_json_object_but_not_an_empty_string()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("in-process");

        var accepted = await host.CallAsync<TriggerDetail>(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.OnDialogStarted),
                ["kind"] = nameof(TriggerKind.InProcess),
                ["config"] = "{}",
            });
        Assert.Equal(TriggerKind.InProcess, accepted.Kind);

        var refused = await host.Mcp.CallToolAsync(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.OnDialogStarted),
                ["kind"] = nameof(TriggerKind.InProcess),
                ["config"] = string.Empty,
            });

        Assert.Equal(400, FlirtyMcpExceptionParityTests.ReadProblem(refused).Status);
    }

    /// <summary>Changing an unknown trigger is mapped to 404.</summary>
    [Fact]
    public async Task UpdateTrigger_of_an_unknown_trigger_reports_404()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("unknown-trigger");

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.TriggerUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["triggerId"] = Guid.NewGuid(),
                ["scope"] = nameof(TriggerScope.OnDialogCompleted),
                ["kind"] = nameof(TriggerKind.Webhook),
                ["config"] = "{\"url\":\"https://example.test/hook\"}",
            });

        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(result).Status);
    }

    // ---- Graph read ----

    /// <summary>
    /// One read carries all six collections of the graph. The HTTP suite proves the projection per area;
    /// over MCP the only new risk is that <c>DialogDetail</c> serializes them all, and one read covers that.
    /// </summary>
    [Fact]
    public async Task GetDialog_carries_the_options_transitions_loops_triggers_and_layout_along()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("graph");
        var role = await host.CreateQuestionAsync(dialog.Id, "role", QuestionType.SingleChoice, 0);
        await host.CreateOptionAsync(dialog.Id, role.Id, "dev", "Developer", "dev", 0);
        var more = await host.CreateQuestionAsync(dialog.Id, "more", QuestionType.Boolean, 1);
        await host.CreateTransitionAsync(dialog.Id, role.Id, more.Id, isDefault: true);
        await host.CreateTransitionAsync(dialog.Id, more.Id, role.Id, isDefault: true);
        await host.CallAsync<LoopDetail>(
            FlirtyToolNames.LoopCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["collectionKey"] = "roles",
                ["entryQuestionId"] = role.Id,
                ["breakingQuestionId"] = more.Id,
            });
        await host.CallAsync<TriggerDetail>(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.OnDialogCompleted),
                ["kind"] = nameof(TriggerKind.Webhook),
                ["config"] = "{\"url\":\"https://example.test/hook\"}",
            });
        await host.CallAsync<FlirtyDialogLayoutView>(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[] { LayoutEntry(role.Id, 40, 80) },
            });

        var detail = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });

        Assert.Equal(2, detail.Questions.Count);
        Assert.Equal("dev", Assert.Single(Assert.Single(detail.Questions, q => q.Key == "role").Options).Key);
        Assert.Equal(2, detail.Transitions.Count);
        Assert.Equal("roles", Assert.Single(detail.Loops).CollectionKey);
        Assert.Equal(TriggerKind.Webhook, Assert.Single(detail.Triggers).Kind);
        Assert.Equal(40, Assert.Single(detail.Layout).X);
    }

    // ---- Canvas layout (#102) ----

    /// <summary>
    /// Setting the layout is a <b>merge</b>: an element not named in the batch keeps its position. Resetting
    /// discards everything.
    /// </summary>
    [Fact]
    public async Task Layout_set_merges_per_element_and_reset_clears_them()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("layout");
        var first = await host.CreateQuestionAsync(dialog.Id, "one", QuestionType.FreeText, 0);
        var second = await host.CreateQuestionAsync(dialog.Id, "two", QuestionType.FreeText, 1);

        var set = await host.CallAsync<FlirtyDialogLayoutView>(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[] { LayoutEntry(first.Id, 100, 200), LayoutEntry(second.Id, 300, 400) },
            });
        Assert.Equal(2, set.Entries.Count);

        // Only the first question is named; the second must keep its position.
        var moved = await host.CallAsync<FlirtyDialogLayoutView>(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[] { LayoutEntry(first.Id, 140, 260) },
            });

        Assert.Equal(2, moved.Entries.Count);
        Assert.Equal(140, Assert.Single(moved.Entries, row => row.ElementId == first.Id).X);
        Assert.Equal(300, Assert.Single(moved.Entries, row => row.ElementId == second.Id).X);
        Assert.Equal(LayoutElementKind.Question, Assert.Single(moved.Entries, row => row.ElementId == first.Id).ElementKind);

        await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.LayoutReset, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });

        var detail = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Empty(detail.Layout);
    }

    /// <summary>
    /// The layout of a <b>published</b> dialog stays editable while a graph change on the very same dialog
    /// is refused as a conflict.
    /// </summary>
    /// <remarks>
    /// One test with both halves, because the pair <i>is</i> ADR 0007. Written separately, the first half
    /// would claim "layout works" without showing that the lock it bypasses actually holds – and a future
    /// guard accidentally added to <c>SetDialogLayoutCommand</c> would then look like a passing test.
    /// </remarks>
    [Fact]
    public async Task Layout_is_editable_on_a_published_dialog_while_a_graph_change_reports_conflict()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var (dialog, question) = await host.CreatePublishedDialogAsync("locked");

        var set = await host.CallAsync<FlirtyDialogLayoutView>(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[] { LayoutEntry(question.Id, 120, 40) },
            });

        Assert.Equal(120, Assert.Single(set.Entries).X);

        var refused = await host.Mcp.CallToolAsync(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "second",
                ["text"] = "And?",
                ["type"] = nameof(QuestionType.FreeText),
                ["order"] = 1,
                ["isRequired"] = false,
            });

        Assert.Equal(409, FlirtyMcpExceptionParityTests.ReadProblem(refused).Status);

        // And resetting is guard-free too - the other half of the exception.
        await host.CallAsync<FlirtyAck>(
            FlirtyToolNames.LayoutReset, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
    }

    /// <summary>
    /// The batch validation of <c>SetDialogLayoutCommand</c> is reachable over MCP and reports 400: an empty
    /// batch, a duplicate element and a negative coordinate.
    /// </summary>
    /// <remarks>
    /// All three exist only because the tool exposes the batch rather than one element per call. There is
    /// deliberately no guard in the tool – it would produce the same 400 by a longer road and duplicate a
    /// rule that has one home.
    /// </remarks>
    [Fact]
    public async Task SetLayout_rejects_an_empty_batch_a_duplicate_element_and_a_negative_coordinate()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("bad-layout");
        var question = await host.CreateQuestionAsync(dialog.Id, "one", QuestionType.FreeText, 0);

        var empty = await host.Mcp.CallToolAsync(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = Array.Empty<object>(),
            });
        Assert.Equal(400, FlirtyMcpExceptionParityTests.ReadProblem(empty).Status);

        var duplicate = await host.Mcp.CallToolAsync(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[] { LayoutEntry(question.Id, 10, 10), LayoutEntry(question.Id, 20, 20) },
            });
        Assert.Equal(400, FlirtyMcpExceptionParityTests.ReadProblem(duplicate).Status);

        var negative = await host.Mcp.CallToolAsync(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[] { LayoutEntry(question.Id, -1, 10) },
            });
        Assert.Equal(400, FlirtyMcpExceptionParityTests.ReadProblem(negative).Status);
    }

    /// <summary>The layout of an unknown dialog is a 404 on both tools.</summary>
    [Fact]
    public async Task Layout_of_an_unknown_dialog_reports_404_on_set_and_on_reset()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var unknown = Guid.NewGuid();

        var set = await host.Mcp.CallToolAsync(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = unknown,
                ["entries"] = new[] { LayoutEntry(Guid.NewGuid(), 10, 10) },
            });
        var reset = await host.Mcp.CallToolAsync(
            FlirtyToolNames.LayoutReset, new Dictionary<string, object?> { ["dialogId"] = unknown });

        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(set).Status);
        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(reset).Status);
    }

    // ---- End-to-End ----

    /// <summary>
    /// A dialog built <b>purely</b> over MCP – questions, options, transitions, a loop marker, a trigger,
    /// layout, entry question, publish – can then be started and played through over the runtime.
    /// </summary>
    /// <remarks>
    /// This is the honest proof of AC 1 of #127: <c>tools/list</c> only shows that the tools exist. The
    /// runtime half runs over HTTP because the runtime tools arrive in the next stage (#128) – which is also
    /// what keeps this from duplicating the round-trip test that stage 5 (#130) owns, where both halves are
    /// MCP.
    /// </remarks>
    [Fact]
    public async Task Dialog_built_purely_over_mcp_is_startable_over_the_runtime()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var dialog = await host.CreateDialogAsync("end-to-end");
        var role = await host.CreateQuestionAsync(dialog.Id, "role", QuestionType.SingleChoice, 0);
        await host.CreateOptionAsync(dialog.Id, role.Id, "dev", "Developer", "dev", 0);
        await host.CreateOptionAsync(dialog.Id, role.Id, "ops", "Operations", "ops", 1);
        var detailQuestion = await host.CreateQuestionAsync(dialog.Id, "detail", QuestionType.FreeText, 1);
        var more = await host.CreateQuestionAsync(dialog.Id, "more", QuestionType.Boolean, 2);
        var summary = await host.CreateQuestionAsync(dialog.Id, "summary", QuestionType.FreeText, 3);

        await host.CreateTransitionAsync(dialog.Id, role.Id, detailQuestion.Id, isDefault: true);
        await host.CreateTransitionAsync(dialog.Id, detailQuestion.Id, more.Id, isDefault: true);
        await host.CreateTransitionAsync(
            dialog.Id, more.Id, detailQuestion.Id, isDefault: false, expression: "more == true");
        await host.CreateTransitionAsync(dialog.Id, more.Id, summary.Id, isDefault: true, priority: 1);

        await host.CallAsync<LoopDetail>(
            FlirtyToolNames.LoopCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["collectionKey"] = "details",
                ["entryQuestionId"] = detailQuestion.Id,
                ["breakingQuestionId"] = more.Id,
            });
        await host.CallAsync<TriggerDetail>(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.OnDialogStarted),
                ["kind"] = nameof(TriggerKind.InProcess),
                ["config"] = "{\"name\":\"started\"}",
            });
        await host.CallAsync<FlirtyDialogLayoutView>(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["entries"] = new[]
                {
                    LayoutEntry(role.Id, 0, 0),
                    LayoutEntry(detailQuestion.Id, 0, 120),
                    LayoutEntry(more.Id, 0, 240),
                    LayoutEntry(summary.Id, 0, 360),
                },
            });

        await host.SetStartQuestionAsync(dialog, role.Id);
        var published = await host.CallAsync<DialogSummary>(
            FlirtyToolNames.DialogPublish, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.True(published.IsPublished);

        // From here on the runtime, over HTTP: the engine sees a dialog no designer ever touched.
        var start = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("end-to-end", "mcp-user"));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var session = (await start.Content.ReadFromJsonAsync<StartSessionResponse>())!;
        Assert.Equal("role", session.CurrentQuestion.Key);
        Assert.Equal(2, session.CurrentQuestion.Options.Count);

        var answered = await SubmitAsync(host, session.SessionId, role.Id, "dev");
        Assert.Equal("detail", answered.NextQuestion!.Key);

        answered = await SubmitAsync(host, session.SessionId, detailQuestion.Id, "Backend");
        Assert.Equal("more", answered.NextQuestion!.Key);

        // "more == true" jumps back into the loop, so the detail question comes round a second time.
        answered = await SubmitAsync(host, session.SessionId, more.Id, "true");
        Assert.Equal("detail", answered.NextQuestion!.Key);

        answered = await SubmitAsync(host, session.SessionId, detailQuestion.Id, "Frontend");
        Assert.Equal("more", answered.NextQuestion!.Key);

        // "false" takes the default out of the loop, and the summary question has no outgoing transition,
        // so answering it completes the dialog.
        answered = await SubmitAsync(host, session.SessionId, more.Id, "false");
        Assert.Equal("summary", answered.NextQuestion!.Key);

        answered = await SubmitAsync(host, session.SessionId, summary.Id, "Done");
        Assert.True(answered.IsCompleted);
        Assert.Null(answered.NextQuestion);

        // The loop marker did its job: both detail answers survived instead of the second overwriting the
        // first.
        var state = await host.Client.GetFromJsonAsync<SessionStateResponse>(
            $"/flirty/sessions/{session.SessionId}");
        Assert.NotNull(state);
        Assert.Equal(2, state.Answers.Count(answer => answer.QuestionId == detailQuestion.Id));
    }

    // ---- Helpers ----

    private static async Task<SubmitAnswerResponse> SubmitAsync(
        FlirtyMcpTestHost host, Guid sessionId, Guid questionId, string value)
    {
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers", new SubmitAnswerRequest(questionId, value));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitAnswerResponse>())!;
    }

    /// <summary>
    /// One entry of the layout batch, in the wire shape the generated schema advertises: camelCase members
    /// and the element kind as a name.
    /// </summary>
    private static Dictionary<string, object?> LayoutEntry(Guid elementId, int x, int y)
        => new()
        {
            ["elementKind"] = nameof(LayoutElementKind.Question),
            ["elementId"] = elementId,
            ["x"] = x,
            ["y"] = y,
        };

    /// <summary>
    /// Read model of the <c>flirty_layout_set</c> result. The production wrapper
    /// <c>Flirty.Mcp.FlirtyDialogLayout</c> is <c>internal</c> and visible here, but deserializing into a
    /// test-local record keeps the assertion independent of its member names – and the wrapper's own shape is
    /// already pinned by the output-schema test.
    /// </summary>
    private sealed record FlirtyDialogLayoutView(IReadOnlyList<DialogLayoutDetail> Entries);
}
