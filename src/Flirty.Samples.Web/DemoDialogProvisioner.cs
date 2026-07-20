using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.Domain;
using Flirty.Persistence;
using Microsoft.Extensions.Logging;

namespace Flirty.Samples.Web;

/// <summary>
/// Baut den veröffentlichten Demo-Dialog idempotent auf – bewusst <b>über die Admin-CRUD-API</b>
/// (HTTP-Aufrufe gegen <c>/flirty/admin/...</c>), damit die Sample das Anlegen von Dialogen/Fragen/
/// Optionen/Übergängen über die öffentliche Endpunkt-Fläche demonstriert.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bewusste Ausnahme:</b> Der Loop-Marker (<see cref="LoopDefinition"/>) lässt sich <em>nicht</em> über
/// die Admin-CRUD-API erzeugen – das Endpunkt-Set deckt kein Loop-CRUD ab (siehe
/// <c>docs/GETTING-STARTED-WebApi.md</c>). Der Zyklus entsteht über eine Loop-Back-<see cref="Transition"/>
/// (via Admin-API), die <see cref="LoopDefinition"/> (Sammlung je Iteration unter
/// <see cref="LoopDefinition.CollectionKey"/>) wird jedoch einmalig direkt über den
/// <see cref="FlirtyDbContext"/> angehängt.
/// </para>
/// </remarks>
public static class DemoDialogProvisioner
{
    /// <summary>
    /// Stellt sicher, dass der Demo-Dialog existiert und veröffentlicht ist. Ist er bereits vorhanden
    /// (Wiederanlauf gegen eine persistente DB), passiert nichts.
    /// </summary>
    /// <param name="client">HTTP-Client, dessen Basis-Adresse auf diese App zeigt (Admin-Endpunkte).</param>
    /// <param name="services">Service-Provider für einen <see cref="FlirtyDbContext"/>-Scope (Loop-Marker).</param>
    /// <param name="logger">Logger für das Ergebnis.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
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
            logger.LogInformation("Demo-Dialog '{DialogKey}' existiert bereits – Provisioning übersprungen.", DemoDialog.DialogKey);
            return;
        }

        // 1) Dialog anlegen (Version 1, unveröffentlicht).
        var dialog = await PostAsync<CreateDialogRequest, DialogResponse>(
            client, "/flirty/admin/dialogs",
            new CreateDialogRequest(DemoDialog.DialogKey, DemoDialog.DialogName, "Web-Sample: Branching + Loop über Liste."),
            cancellationToken);
        var dialogId = dialog.Id;

        // 2) Fragen (+ Optionen) anlegen.
        var roleId = await CreateQuestionAsync(client, dialogId, DemoDialog.RoleKey, "Welche Rolle hast du?", QuestionType.SingleChoice, 0, cancellationToken);
        await CreateOptionAsync(client, dialogId, roleId, "dev", "Entwickler", "dev", 0, cancellationToken);
        await CreateOptionAsync(client, dialogId, roleId, "pm", "Product Manager", "pm", 1, cancellationToken);

        var languageId = await CreateQuestionAsync(client, dialogId, DemoDialog.LanguageKey, "Welche Programmiersprache nutzt du am liebsten?", QuestionType.FreeText, 1, cancellationToken);
        var productId = await CreateQuestionAsync(client, dialogId, DemoDialog.ProductKey, "Welches Produkt betreust du?", QuestionType.FreeText, 2, cancellationToken);

        var skillId = await CreateQuestionAsync(client, dialogId, DemoDialog.SkillKey, "Nenne eine Fähigkeit (Skill).", QuestionType.FreeText, 3, cancellationToken);
        var moreId = await CreateQuestionAsync(client, dialogId, DemoDialog.MoreKey, "Möchtest du eine weitere Fähigkeit hinzufügen?", QuestionType.SingleChoice, 4, cancellationToken);
        await CreateOptionAsync(client, dialogId, moreId, "yes", "Ja", "yes", 0, cancellationToken);
        await CreateOptionAsync(client, dialogId, moreId, "no", "Nein", "no", 1, cancellationToken);

        var summaryId = await CreateQuestionAsync(client, dialogId, DemoDialog.SummaryKey, "Passt alles so?", QuestionType.Boolean, 5, cancellationToken);

        // 3) Einstiegsfrage setzen.
        await PutAsync(client, $"/flirty/admin/dialogs/{dialogId}",
            new UpdateDialogRequest(DemoDialog.DialogKey, DemoDialog.DialogName, dialog.Description, roleId),
            cancellationToken);

        // 4) Übergänge (Branching + Loop-Zyklus) anlegen.
        await CreateTransitionAsync(client, dialogId, roleId, languageId, "role == \"dev\"", 0, false, cancellationToken); // dev-Zweig
        await CreateTransitionAsync(client, dialogId, roleId, productId, null, 1, true, cancellationToken);               // Default-Zweig
        await CreateTransitionAsync(client, dialogId, languageId, skillId, null, 0, true, cancellationToken);             // -> Loop-Entry
        await CreateTransitionAsync(client, dialogId, productId, skillId, null, 0, true, cancellationToken);              // -> Loop-Entry
        await CreateTransitionAsync(client, dialogId, skillId, moreId, null, 0, true, cancellationToken);                 // Entry -> Breaking
        await CreateTransitionAsync(client, dialogId, moreId, skillId, "more == \"yes\"", 0, false, cancellationToken);   // Loop-Back
        await CreateTransitionAsync(client, dialogId, moreId, summaryId, null, 1, true, cancellationToken);               // Exit -> Abschlussfrage

        // 5) Loop-Marker anhängen (NICHT über Admin-CRUD möglich -> direkt über den DbContext).
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

        // 6) Veröffentlichen -> ab jetzt über POST /flirty/sessions startbar.
        var publish = await client.PostAsync($"/flirty/admin/dialogs/{dialogId}/publish", content: null, cancellationToken);
        publish.EnsureSuccessStatusCode();

        logger.LogInformation("Demo-Dialog '{DialogKey}' angelegt und veröffentlicht (Id {DialogId}).", DemoDialog.DialogKey, dialogId);
    }

    private static async Task<Guid> CreateQuestionAsync(
        HttpClient client, Guid dialogId, string key, string text, QuestionType type, int order, CancellationToken cancellationToken)
    {
        var question = await PostAsync<CreateQuestionRequest, QuestionResponse>(
            client, $"/flirty/admin/dialogs/{dialogId}/questions",
            new CreateQuestionRequest(key, text, type, order, IsRequired: true, ValidationRules: null),
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
            ?? throw new InvalidOperationException($"Leere Antwort von {url}.");
    }

    private static async Task PutAsync<TRequest>(
        HttpClient client, string url, TRequest request, CancellationToken cancellationToken)
    {
        var response = await client.PutAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
