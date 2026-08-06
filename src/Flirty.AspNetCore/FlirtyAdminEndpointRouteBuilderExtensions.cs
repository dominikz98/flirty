using Flirty.AspNetCore;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.AspNetCore.Mapping;
using Flirty.Runtime.Admin;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides the extension method <see cref="MapFlirtyAdminEndpoints"/>, which registers the optional
/// admin-CRUD endpoints of the Flirty dialog engine (management of dialogs, questions, options,
/// transitions and loop markers) as a minimal-API route group. Like the runtime endpoints they are a
/// thin layer over the Mediator commands (dispatch via <see cref="ISender"/>). The namespace
/// <c>Microsoft.AspNetCore.Builder</c> is chosen deliberately so that the method is discoverable without an
/// additional <c>using</c>.
/// </summary>
public static class FlirtyAdminEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registers the optional admin-CRUD endpoints under the given <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><description><c>POST/GET {prefix}/dialogs</c>, <c>GET/PUT/DELETE {prefix}/dialogs/{id}</c> – manage dialogs.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{id}/publish</c> or <c>/unpublish</c> – control publication.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{id}/versions</c> – derive a new version as a draft from this one.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{id}/abandon-sessions</c> – end running sessions of this version.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{dialogId}/questions</c>, <c>PUT/DELETE .../questions/{questionId}</c> – manage questions.</description></item>
    /// <item><description><c>POST .../questions/{questionId}/options</c>, <c>PUT/DELETE .../options/{optionId}</c> – manage answer options.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{dialogId}/transitions</c>, <c>PUT/DELETE .../transitions/{transitionId}</c> – manage transitions.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{dialogId}/loops</c>, <c>PUT/DELETE .../loops/{loopId}</c> – manage loop markers.</description></item>
    /// <item><description><c>POST {prefix}/dialogs/{dialogId}/triggers</c>, <c>PUT/DELETE .../triggers/{triggerId}</c> – manage triggers.</description></item>
    /// <item><description><c>PUT/DELETE {prefix}/dialogs/{dialogId}/layout</c> – set or discard canvas positions.</description></item>
    /// </list>
    /// The prerequisite is a Flirty stack previously registered via <c>services.AddFlirty(...)</c>. Exceptions
    /// thrown by the engine are mapped onto <c>ProblemDetails</c> via the same endpoint filter as the runtime
    /// endpoints (404 for unknown elements, 400 for invalid requests, 409 for key/state conflicts). Since the
    /// endpoints are write endpoints, securing them via <c>RequireAuthorization()</c> on the returned group is
    /// recommended.
    /// </summary>
    /// <param name="endpoints">The endpoint router of the host app (e.g. the <see cref="WebApplication"/>).</param>
    /// <param name="prefix">
    /// The route prefix under which the endpoints are registered (default: <c>"/flirty/admin"</c>).
    /// </param>
    /// <returns>
    /// The created <see cref="RouteGroupBuilder"/>, to configure the group further (e.g.
    /// <c>RequireAuthorization()</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prefix"/> is <see langword="null"/>, empty or only whitespace.
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
        MapLoopEndpoints(group);
        MapTriggerEndpoints(group);
        MapLayoutEndpoints(group);

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

        // Derive a new version from an existing one: the intended way to evolve a published
        // dialog without breaking running sessions.
        group.MapPost("/dialogs/{id:guid}/versions", async (
            Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new CreateDialogVersionCommand(id), cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created($"{prefix}/dialogs/{response.Id}", response);
        });

        group.MapPost("/dialogs/{id:guid}/abandon-sessions", async (
            Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new AbandonDialogSessionsCommand(id), cancellationToken);
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
                    request.Order, request.IsRequired, request.ValidationRules, request.CustomTypeKey),
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
                    request.Order, request.IsRequired, request.ValidationRules, request.CustomTypeKey),
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

    private static void MapLoopEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/dialogs/{dialogId:guid}/loops", async (
            Guid dialogId, CreateLoopRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new CreateLoopCommand(
                    dialogId, request.CollectionKey, request.EntryQuestionId, request.BreakingQuestionId),
                cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created($"/dialogs/{dialogId}/loops/{response.Id}", response);
        });

        group.MapPut("/dialogs/{dialogId:guid}/loops/{loopId:guid}", async (
            Guid dialogId, Guid loopId, UpdateLoopRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new UpdateLoopCommand(
                    dialogId, loopId, request.CollectionKey, request.EntryQuestionId, request.BreakingQuestionId),
                cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapDelete("/dialogs/{dialogId:guid}/loops/{loopId:guid}", async (
            Guid dialogId, Guid loopId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteLoopCommand(dialogId, loopId), cancellationToken);
            return TypedResults.NoContent();
        });
    }

    private static void MapTriggerEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/dialogs/{dialogId:guid}/triggers", async (
            Guid dialogId, CreateTriggerRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new CreateTriggerCommand(
                    dialogId, request.Scope, request.QuestionId, request.Kind, request.Config, request.Expression),
                cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created($"/dialogs/{dialogId}/triggers/{response.Id}", response);
        });

        group.MapPut("/dialogs/{dialogId:guid}/triggers/{triggerId:guid}", async (
            Guid dialogId, Guid triggerId, UpdateTriggerRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new UpdateTriggerCommand(
                    dialogId, triggerId, request.Scope, request.QuestionId, request.Kind, request.Config,
                    request.Expression),
                cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapDelete("/dialogs/{dialogId:guid}/triggers/{triggerId:guid}", async (
            Guid dialogId, Guid triggerId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteTriggerCommand(dialogId, triggerId), cancellationToken);
            return TypedResults.NoContent();
        });
    }

    /// <summary>
    /// Registers the endpoints for the designer's canvas positions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>PUT</c> is a merge here, not a replacement.</b> Only the elements named in the body are set;
    /// rows not named remain in place. Reason: a drag gesture in the designer usually moves a single
    /// element and should not have to send the entire layout for that. A full discard is done by
    /// <c>DELETE</c>.
    /// </para>
    /// <para>
    /// Both endpoints work <b>even for a published dialog</b> and return no 409 –
    /// coordinates are not part of the graph (ADR 0007). The publish lock of the other
    /// graph endpoints is unaffected by this.
    /// </para>
    /// </remarks>
    /// <param name="group">The admin route group.</param>
    private static void MapLayoutEndpoints(RouteGroupBuilder group)
    {
        group.MapPut("/dialogs/{dialogId:guid}/layout", async (
            Guid dialogId, SetDialogLayoutRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var entries = request.Entries is null
                ? []
                : request.Entries
                    .Select(entry => new DialogLayoutEntry(
                        entry.ElementKind, entry.ElementId, entry.X, entry.Y))
                    .ToArray();

            var result = await sender.Send(
                new SetDialogLayoutCommand(dialogId, entries), cancellationToken);

            return TypedResults.Ok(result.Select(layout => layout.ToResponse()).ToArray());
        });

        group.MapDelete("/dialogs/{dialogId:guid}/layout", async (
            Guid dialogId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ResetDialogLayoutCommand(dialogId), cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
