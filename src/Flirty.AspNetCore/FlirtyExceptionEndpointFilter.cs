using System.ComponentModel.DataAnnotations;
using Flirty.Runtime;
using Flirty.Validation;
using Microsoft.AspNetCore.Http;

namespace Flirty.AspNetCore;

/// <summary>
/// Endpoint filter that maps the exceptions thrown by the engine uniformly onto HTTP status codes together
/// with <c>ProblemDetails</c>. Applied to the route groups created by <c>MapFlirtyEndpoints</c> (runtime) and
/// <c>MapFlirtyAdminEndpoints</c> (admin CRUD), so the package works without a global exception-handling
/// middleware in the host app.
/// </summary>
/// <remarks>
/// The order of the <c>catch</c> branches matters: <see cref="AnswerValidationException"/> derives from
/// <see cref="ValidationException"/> and must therefore be handled first. Not-found branches
/// (including <see cref="ConfigurationNotFoundException"/> for the admin CRUD) come before the generic
/// <see cref="InvalidOperationException"/> branch, which maps state/key conflicts onto <c>409</c>.
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
                title: "Dialog not found");
        }
        catch (SessionNotFoundException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Session not found");
        }
        catch (ConfigurationNotFoundException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Not found");
        }
        catch (AnswerValidationException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["value"] = [.. exception.Errors] },
                detail: exception.Message,
                title: "Invalid answer");
        }
        catch (ValidationException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict");
        }
    }
}
