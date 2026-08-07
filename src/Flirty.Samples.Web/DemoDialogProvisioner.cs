using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.Domain;
using Flirty.Persistence;
using Microsoft.Extensions.Logging;

namespace Flirty.Samples.Web;

/// <summary>
/// Builds the published demo dialog idempotently – deliberately <b>via the admin CRUD API</b>
/// (HTTP calls against <c>/flirty/admin/...</c>), so that the sample demonstrates creating dialogs/questions/
/// options/transitions over the public endpoint surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberate exception:</b> the loop marker (<see cref="LoopDefinition"/>) <em>cannot</em> be created via
/// the admin CRUD API – the endpoint set covers no loop CRUD (see
/// <c>docs/GETTING-STARTED-WebApi.md</c>). The cycle arises via a loop-back <see cref="Transition"/>
/// (via the admin API), but the <see cref="LoopDefinition"/> (collection per iteration under
/// <see cref="LoopDefinition.CollectionKey"/>) is attached once directly through the
/// <see cref="FlirtyDbContext"/>.
/// </para>
/// </remarks>
public static class DemoDialogProvisioner
{
    /// <summary>
    /// Ensures the demo dialog exists and is published. If it already exists
    /// (restart against a persistent DB), nothing happens.
    /// </summary>
    /// <param name="client">HTTP client whose base address points at this app (admin endpoints).</param>
    /// <param name="services">Service provider for a <see cref="FlirtyDbContext"/> scope (loop marker).</param>
    /// <param name="logger">Logger for the result.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public static async Task EnsureProvisionedAsync(
        HttpClient client, IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        var existing = await client.GetFromJsonAsync<IReadOnlyList<DialogResponse>>(
            "/flirty/admin/dialogs", cancellationToken) ?? [];
        if (existing.Any(dialog => dialog.Key == DemoDialog.DialogKey))
        {
            logger.LogInformation("Demo dialog '{DialogKey}' already exists – provisioning skipped.", DemoDialog.DialogKey);
            return;
        }

        // 1) Create the dialog (version 1, unpublished).
        var dialog = await PostAsync<CreateDialogRequest, DialogResponse>(
            client, "/flirty/admin/dialogs",
            new CreateDialogRequest(DemoDialog.DialogKey, DemoDialog.DialogName, "Web sample: branching + loop over list."),
            cancellationToken);
        var dialogId = dialog.Id;

        // 2) Create the questions (+ options).
        // The entry question greets by name with the {{user-name}} placeholder (#140); the host's
        // UserNamePlaceholderFiller replaces it with a live value at delivery time.
        var roleId = await CreateQuestionAsync(client, dialogId, DemoDialog.RoleKey, "Hi {{user-name}}! What is your role?", QuestionType.SingleChoice, 0, cancellationToken);
        await CreateOptionAsync(client, dialogId, roleId, "dev", "Developer", "dev", 0, cancellationToken);
        await CreateOptionAsync(client, dialogId, roleId, "pm", "Product Manager", "pm", 1, cancellationToken);

        var languageId = await CreateQuestionAsync(client, dialogId, DemoDialog.LanguageKey, "Which programming language do you prefer?", QuestionType.FreeText, 1, cancellationToken);
        var productId = await CreateQuestionAsync(client, dialogId, DemoDialog.ProductKey, "Which product do you look after?", QuestionType.FreeText, 2, cancellationToken);

        var skillId = await CreateQuestionAsync(client, dialogId, DemoDialog.SkillKey, "Name a skill.", QuestionType.FreeText, 3, cancellationToken);
        var moreId = await CreateQuestionAsync(client, dialogId, DemoDialog.MoreKey, "Would you like to add another skill?", QuestionType.SingleChoice, 4, cancellationToken);
        await CreateOptionAsync(client, dialogId, moreId, "yes", "Yes", "yes", 0, cancellationToken);
        await CreateOptionAsync(client, dialogId, moreId, "no", "No", "no", 1, cancellationToken);

        // The two host-declared custom types (#136). Both are QuestionType.Json and pick their type by
        // key; the engine checks well-formedness, the host's own validator checks the rest.
        var colourId = await CreateQuestionAsync(
            client, dialogId, DemoDialog.ColourKey, "Pick your favourite colour.", QuestionType.Json, 5,
            cancellationToken, DemoDialog.ColourTypeKey);
        var addressId = await CreateQuestionAsync(
            client, dialogId, DemoDialog.AddressKey, "Where should we send your welcome package?",
            QuestionType.Json, 6, cancellationToken, DemoDialog.AddressTypeKey);

        // The final question stamps the delivery date with the {{today}} placeholder (#140).
        var summaryId = await CreateQuestionAsync(client, dialogId, DemoDialog.SummaryKey, "Thanks, {{user-name}}! Does everything look right (today is {{today}})?", QuestionType.Boolean, 7, cancellationToken);

        // 3) Set the entry question.
        await PutAsync(client, $"/flirty/admin/dialogs/{dialogId}",
            new UpdateDialogRequest(DemoDialog.DialogKey, DemoDialog.DialogName, dialog.Description, roleId),
            cancellationToken);

        // 4) Create the transitions (branching + loop cycle).
        await CreateTransitionAsync(client, dialogId, roleId, languageId, "role == \"dev\"", 0, false, cancellationToken); // dev branch
        await CreateTransitionAsync(client, dialogId, roleId, productId, null, 1, true, cancellationToken);               // default branch
        await CreateTransitionAsync(client, dialogId, languageId, skillId, null, 0, true, cancellationToken);             // -> loop entry
        await CreateTransitionAsync(client, dialogId, productId, skillId, null, 0, true, cancellationToken);              // -> loop entry
        await CreateTransitionAsync(client, dialogId, skillId, moreId, null, 0, true, cancellationToken);                 // entry -> breaking
        await CreateTransitionAsync(client, dialogId, moreId, skillId, "more == \"yes\"", 0, false, cancellationToken);   // loop-back
        await CreateTransitionAsync(client, dialogId, moreId, colourId, null, 1, true, cancellationToken);                // exit -> colour
        await CreateTransitionAsync(client, dialogId, colourId, addressId, null, 0, true, cancellationToken);             // colour -> address
        await CreateTransitionAsync(client, dialogId, addressId, summaryId, null, 0, true, cancellationToken);            // address -> final question

        // 5) Attach the loop marker (NOT possible via admin CRUD -> directly through the DbContext).
        using (var scope = services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            context.Set<LoopDefinition>().Add(new LoopDefinition
            {
                Id = Guid.NewGuid(),
                DialogId = dialogId,
                CollectionKey = DemoDialog.CollectionKey,
                EntryQuestionId = skillId,
                BreakingQuestionId = moreId,
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        // 6) Publish -> from now on startable via POST /flirty/sessions.
        var publish = await client.PostAsync($"/flirty/admin/dialogs/{dialogId}/publish", content: null, cancellationToken);
        publish.EnsureSuccessStatusCode();

        logger.LogInformation("Demo dialog '{DialogKey}' created and published (id {DialogId}).", DemoDialog.DialogKey, dialogId);
    }

    private static async Task<Guid> CreateQuestionAsync(
        HttpClient client, Guid dialogId, string key, string text, QuestionType type, int order,
        CancellationToken cancellationToken, string? customTypeKey = null)
    {
        var question = await PostAsync<CreateQuestionRequest, QuestionResponse>(
            client, $"/flirty/admin/dialogs/{dialogId}/questions",
            new CreateQuestionRequest(
                key, text, type, order, IsRequired: true, ValidationRules: null,
                CustomTypeKey: customTypeKey),
            cancellationToken);
        return question.Id;
    }

    private static async Task CreateOptionAsync(
        HttpClient client, Guid dialogId, Guid questionId, string key, string label, string value, int order, CancellationToken cancellationToken)
    {
        await PostAsync<CreateAnswerOptionRequest, AnswerOptionResponse>(
            client, $"/flirty/admin/dialogs/{dialogId}/questions/{questionId}/options",
            new CreateAnswerOptionRequest(key, label, value, order),
            cancellationToken);
    }

    private static async Task CreateTransitionAsync(
        HttpClient client, Guid dialogId, Guid fromQuestionId, Guid targetQuestionId,
        string? expression, int priority, bool isDefault, CancellationToken cancellationToken)
    {
        await PostAsync<CreateTransitionRequest, TransitionResponse>(
            client, $"/flirty/admin/dialogs/{dialogId}/transitions",
            new CreateTransitionRequest(fromQuestionId, targetQuestionId, expression, priority, isDefault),
            cancellationToken);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(
        HttpClient client, string url, TRequest request, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidOperationException($"Empty response from {url}.");
    }

    private static async Task PutAsync<TRequest>(
        HttpClient client, string url, TRequest request, CancellationToken cancellationToken)
    {
        var response = await client.PutAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
