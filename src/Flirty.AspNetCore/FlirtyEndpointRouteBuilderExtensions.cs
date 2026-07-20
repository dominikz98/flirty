using Flirty.AspNetCore;
using Flirty.AspNetCore.Dtos;
using Flirty.AspNetCore.Mapping;
using Flirty.Runtime;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Stellt die Erweiterungsmethode <see cref="MapFlirtyEndpoints"/> bereit, die die HTTP-Endpunkte der
/// Flirty-Dialog-Engine als Minimal-API-Route-Gruppe registriert. Die Endpunkte sind eine dünne Schicht
/// über die Mediator-Commands (der Core bleibt ASP.NET-frei) und senden diese direkt per
/// <see cref="ISender"/>. Der Namespace <c>Microsoft.AspNetCore.Builder</c> ist bewusst gewählt, damit
/// die Methode ohne zusätzliches <c>using</c> auf einem <see cref="IEndpointRouteBuilder"/> auffindbar ist.
/// </summary>
public static class FlirtyEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registriert die Flirty-Dialog-Endpunkte unter dem angegebenen <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><description><c>POST {prefix}/sessions</c> – Dialog starten bzw. fortsetzen (Resume).</description></item>
    /// <item><description><c>GET {prefix}/sessions/{id}</c> – aktuellen Session-Zustand lesen.</description></item>
    /// <item><description><c>POST {prefix}/sessions/{id}/answers</c> – Antwort auf die offene Frage einreichen.</description></item>
    /// <item><description><c>PUT {prefix}/sessions/{id}/answers/{questionId}</c> – frühere Antwort editieren.</description></item>
    /// </list>
    /// Voraussetzung ist ein zuvor per <c>services.AddFlirty(...)</c> registrierter Flirty-Stack. Von der
    /// Engine geworfene Ausnahmen werden über einen Endpunkt-Filter einheitlich auf <c>ProblemDetails</c>
    /// abgebildet (404 für unbekannte Dialoge/Sessions, 400 für ungültige Anfragen/Antworten, 409 für
    /// Zustands-Konflikte).
    /// </summary>
    /// <param name="endpoints">Der Endpunkt-Router der Host-App (z. B. die <see cref="WebApplication"/>).</param>
    /// <param name="prefix">
    /// Das Routen-Präfix, unter dem die Endpunkte registriert werden (Standard: <c>"/flirty"</c>).
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
