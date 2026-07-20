using System.ComponentModel.DataAnnotations;
using Flirty.Runtime;
using Flirty.Validation;
using Microsoft.AspNetCore.Http;

namespace Flirty.AspNetCore;

/// <summary>
/// Endpunkt-Filter, der die von der Engine geworfenen Ausnahmen einheitlich auf HTTP-Statuscodes samt
/// <c>ProblemDetails</c> abbildet. Wird auf die von <c>MapFlirtyEndpoints</c> (Laufzeit) und
/// <c>MapFlirtyAdminEndpoints</c> (Admin-CRUD) erzeugten Route-Gruppen gelegt, sodass das Paket ohne
/// eine globale Exception-Handling-Middleware der Host-App auskommt.
/// </summary>
/// <remarks>
/// Die Reihenfolge der <c>catch</c>-Zweige ist relevant: <see cref="AnswerValidationException"/> leitet
/// von <see cref="ValidationException"/> ab und muss daher zuerst behandelt werden. Not-Found-Zweige
/// (inkl. <see cref="ConfigurationNotFoundException"/> für das Admin-CRUD) stehen vor dem generischen
/// <see cref="InvalidOperationException"/>-Zweig, der Zustands-/Schlüsselkonflikte auf <c>409</c> abbildet.
/// </remarks>
internal sealed class FlirtyExceptionEndpointFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(context);
        }
        catch (DialogNotFoundException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Dialog nicht gefunden");
        }
        catch (SessionNotFoundException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Session nicht gefunden");
        }
        catch (ConfigurationNotFoundException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Nicht gefunden");
        }
        catch (AnswerValidationException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["value"] = [.. exception.Errors] },
                detail: exception.Message,
                title: "Antwort ungültig");
        }
        catch (ValidationException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Ungültige Anfrage");
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Konflikt");
        }
    }
}
