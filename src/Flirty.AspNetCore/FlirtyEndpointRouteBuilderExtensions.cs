using Flirty.AspNetCore;
using Flirty.AspNetCore.Dtos;
using Flirty.AspNetCore.Mapping;
using Flirty.Runtime;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides the extension method <see cref="MapFlirtyEndpoints"/>, which registers the HTTP endpoints of the
/// Flirty dialog engine as a minimal-API route group. The endpoints are a thin layer over the Mediator
/// commands (the core stays ASP.NET-free) and send them directly via <see cref="ISender"/>. The namespace
/// <c>Microsoft.AspNetCore.Builder</c> is chosen deliberately so that the method is discoverable on an
/// <see cref="IEndpointRouteBuilder"/> without an additional <c>using</c>.
/// </summary>
public static class FlirtyEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registers the Flirty dialog endpoints under the given <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><description><c>POST {prefix}/sessions</c> – start or resume a dialog.</description></item>
    /// <item><description><c>GET {prefix}/sessions/{id}</c> – read the current session state.</description></item>
    /// <item><description><c>POST {prefix}/sessions/{id}/answers</c> – submit an answer to the open question.</description></item>
    /// <item><description><c>PUT {prefix}/sessions/{id}/answers/{questionId}</c> – edit an earlier answer.</description></item>
    /// </list>
    /// The prerequisite is a Flirty stack previously registered via <c>services.AddFlirty(...)</c>. Exceptions
    /// thrown by the engine are mapped uniformly onto <c>ProblemDetails</c> via an endpoint filter (404 for
    /// unknown dialogs/sessions, 400 for invalid requests/answers, 409 for state conflicts).
    /// </summary>
    /// <param name="endpoints">The endpoint router of the host app (e.g. the <see cref="WebApplication"/>).</param>
    /// <param name="prefix">
    /// The route prefix under which the endpoints are registered (default: <c>"/flirty"</c>).
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
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddFlirty(o => o.UseSqlServer(conn).ApplyMigrations());
    /// var app = builder.Build();
    /// app.MapFlirtyEndpoints("/flirty");
    /// app.Run();
    /// </code>
    /// </example>
    public static RouteGroupBuilder MapFlirtyEndpoints(
        this IEndpointRouteBuilder endpoints, string prefix = "/flirty")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var group = endpoints.MapGroup(prefix).WithTags("Flirty");
        group.AddEndpointFilter<FlirtyExceptionEndpointFilter>();

        group.MapPost("/sessions", async (
            StartSessionRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new StartDialogCommand(request.DialogKey, request.ExternalUserKey), cancellationToken);
            var response = result.ToResponse();
            return TypedResults.Created($"{prefix}/sessions/{response.SessionId}", response);
        });

        group.MapGet("/sessions/{id:guid}", async (
            Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ResumeDialogQuery(id), cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapPost("/sessions/{id:guid}/answers", async (
            Guid id, SubmitAnswerRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new SubmitAnswerCommand(id, request.QuestionId, request.Value), cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        group.MapPut("/sessions/{id:guid}/answers/{questionId:guid}", async (
            Guid id, Guid questionId, EditAnswerRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new EditAnswerCommand(id, questionId, request.Value, request.IterationIndex), cancellationToken);
            return TypedResults.Ok(result.ToResponse());
        });

        return group;
    }
}
