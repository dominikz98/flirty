using Flirty.AspNetCore;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.AspNetCore.Mapping;
using Flirty.Runtime.Admin;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Stellt die Erweiterungsmethode <see cref="MapFlirtyAdminEndpoints"/> bereit, die die optionalen
/// Admin-CRUD-Endpunkte der Flirty-Dialog-Engine (Verwaltung von Dialogen, Fragen, Optionen und
/// Übergängen) als Minimal-API-Route-Gruppe registriert. Wie die Laufzeit-Endpunkte sind sie eine
/// dünne Schicht über die Mediator-Commands (Versand per <see cref="ISender"/>). Der Namespace
/// <c>Microsoft.AspNetCore.Builder</c> ist bewusst gewählt, damit die Methode ohne zusätzliches
/// <c>using</c> auffindbar ist.
/// </summary>
public static class FlirtyAdminEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registriert die optionalen Admin-CRUD-Endpunkte unter dem angegebenen <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><description><c>POST/GET {prefix}/dialogs</c>, <c>GET/PUT/DELETE {prefix}/dialogs/{id}</c> – Dialoge verwalten.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{id}/publish</c> bzw. <c>/unpublish</c> – Veröffentlichung steuern.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{dialogId}/questions</c>, <c>PUT/DELETE .../questions/{questionId}</c> – Fragen verwalten.</description></item>
    /// <item><description><c>POST .../questions/{questionId}/options</c>, <c>PUT/DELETE .../options/{optionId}</c> – Antwortoptionen verwalten.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{dialogId}/transitions</c>, <c>PUT/DELETE .../transitions/{transitionId}</c> – Übergänge verwalten.</description></item>
    /// </list>
    /// Voraussetzung ist ein zuvor per <c>services.AddFlirty(...)</c> registrierter Flirty-Stack. Von der
    /// Engine geworfene Ausnahmen werden über denselben Endpunkt-Filter wie bei den Laufzeit-Endpunkten
    /// auf <c>ProblemDetails</c> abgebildet (404 für unbekannte Elemente, 400 für ungültige Anfragen,
    /// 409 für Schlüssel-/Zustandskonflikte). Da die Endpunkte schreibend sind, empfiehlt sich eine
    /// Absicherung per <c>RequireAuthorization()</c> auf der zurückgegebenen Gruppe.
    /// </summary>
    /// <param name="endpoints">Der Endpunkt-Router der Host-App (z. B. die <see cref="WebApplication"/>).</param>
    /// <param name="prefix">
    /// Das Routen-Präfix, unter dem die Endpunkte registriert werden (Standard: <c>"/flirty/admin"</c>).
    /// </param>
    /// <returns>
    /// Die erzeugte <see cref="RouteGroupBuilder"/>, um die Gruppe weiter zu konfigurieren (z. B.
    /// <c>RequireAuthorization()</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> ist <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prefix"/> ist <see langword="null"/>, leer oder nur Leerraum.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapFlirtyEndpoints("/flirty");
    /// app.MapFlirtyAdminEndpoints("/flirty/admin").RequireAuthorization();
    /// </code>
    /// </example>
    public static RouteGroupBuilder MapFlirtyAdminEndpoints(
        this IEndpointRouteBuilder endpoints, string prefix = "/flirty/admin")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var group = endpoints.MapGroup(prefix).WithTags("Flirty Admin");
        group.AddEndpointFilter<FlirtyExceptionEndpointFilter>();

        MapDialogEndpoints(group, prefix);
        MapQuestionEndpoints(group);
        MapAnswerOptionEndpoints(group);
        MapTransitionEndpoints(group);

        return group;
    }

    private static void MapDialogEndpoints(RouteGroupBuilder group, string prefix)
    {
        group.MapPost("/dialogs", async (
            CreateDialogRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new CreateDialogCommand(request.Key, request.Name, request.Description), cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created($"{prefix}/dialogs/{response.Id}", response);
        });

        group.MapGet("/dialogs", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ListDialogsQuery(), cancellationToken);
            return TypedResults.Ok(result.Select(summary => summary.ToResponse()).ToArray());
        });

        group.MapGet("/dialogs/{id:guid}", async (
            Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetDialogQuery(id), cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapPut("/dialogs/{id:guid}", async (
            Guid id, UpdateDialogRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new UpdateDialogCommand(id, request.Key, request.Name, request.Description, request.StartQuestionId),
                cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapDelete("/dialogs/{id:guid}", async (
            Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteDialogCommand(id), cancellationToken);
            return TypedResults.NoContent();
        });

        group.MapPost("/dialogs/{id:guid}/publish", async (
            Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new PublishDialogCommand(id), cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapPost("/dialogs/{id:guid}/unpublish", async (
            Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new UnpublishDialogCommand(id), cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });
    }

    private static void MapQuestionEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/dialogs/{dialogId:guid}/questions", async (
            Guid dialogId, CreateQuestionRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new CreateQuestionCommand(
                    dialogId, request.Key, request.Text, request.Type,
                    request.Order, request.IsRequired, request.ValidationRules),
                cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created(
                $"/dialogs/{dialogId}/questions/{response.Id}", response);
        });

        group.MapPut("/dialogs/{dialogId:guid}/questions/{questionId:guid}", async (
            Guid dialogId, Guid questionId, UpdateQuestionRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new UpdateQuestionCommand(
                    dialogId, questionId, request.Key, request.Text, request.Type,
                    request.Order, request.IsRequired, request.ValidationRules),
                cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapDelete("/dialogs/{dialogId:guid}/questions/{questionId:guid}", async (
            Guid dialogId, Guid questionId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteQuestionCommand(dialogId, questionId), cancellationToken);
            return TypedResults.NoContent();
        });
    }

    private static void MapAnswerOptionEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/dialogs/{dialogId:guid}/questions/{questionId:guid}/options", async (
            Guid dialogId, Guid questionId, CreateAnswerOptionRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new CreateAnswerOptionCommand(
                    dialogId, questionId, request.Key, request.Label, request.Value, request.Order),
                cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created(
                $"/dialogs/{dialogId}/questions/{questionId}/options/{response.Id}", response);
        });

        group.MapPut("/dialogs/{dialogId:guid}/questions/{questionId:guid}/options/{optionId:guid}", async (
            Guid dialogId, Guid questionId, Guid optionId, UpdateAnswerOptionRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new UpdateAnswerOptionCommand(
                    dialogId, questionId, optionId, request.Key, request.Label, request.Value, request.Order),
                cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapDelete("/dialogs/{dialogId:guid}/questions/{questionId:guid}/options/{optionId:guid}", async (
            Guid dialogId, Guid questionId, Guid optionId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteAnswerOptionCommand(dialogId, questionId, optionId), cancellationToken);
            return TypedResults.NoContent();
        });
    }

    private static void MapTransitionEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/dialogs/{dialogId:guid}/transitions", async (
            Guid dialogId, CreateTransitionRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new CreateTransitionCommand(
                    dialogId, request.FromQuestionId, request.TargetQuestionId,
                    request.Expression, request.Priority, request.IsDefault),
                cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created($"/dialogs/{dialogId}/transitions/{response.Id}", response);
        });

        group.MapPut("/dialogs/{dialogId:guid}/transitions/{transitionId:guid}", async (
            Guid dialogId, Guid transitionId, UpdateTransitionRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new UpdateTransitionCommand(
                    dialogId, transitionId, request.FromQuestionId, request.TargetQuestionId,
                    request.Expression, request.Priority, request.IsDefault),
                cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapDelete("/dialogs/{dialogId:guid}/transitions/{transitionId:guid}", async (
            Guid dialogId, Guid transitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteTransitionCommand(dialogId, transitionId), cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
