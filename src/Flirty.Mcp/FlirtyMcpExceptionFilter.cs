using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Flirty.Runtime;
using Flirty.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Flirty.Mcp;

/// <summary>
/// Call-tool filter that maps the exceptions thrown by the engine uniformly onto a
/// <see cref="FlirtyProblem"/> payload. Registered exactly once by <c>AddFlirtyMcp</c>, so the package
/// works without the host having to wrap every tool.
/// </summary>
/// <remarks>
/// <para>
/// This is the structural analogue of <c>group.AddEndpointFilter&lt;FlirtyExceptionEndpointFilter&gt;()</c>
/// on the two route groups of <c>Flirty.AspNetCore</c>: one <c>try</c>, one registration, so
/// "mirrors the HTTP filter" is true by construction instead of by one <c>try</c> per tool.
/// </para>
/// <para>
/// It exists because the SDK swallows exception messages: anything that does not derive from
/// <see cref="McpException"/> reaches the client as a generic <c>"An error occurred invoking 'x'."</c>, so
/// Flirty's messages would be lost. A call-tool filter is composed <b>inside</b> the SDK's own try/catch
/// and therefore sees the original exception first.
/// </para>
/// <para>
/// The order of the <c>catch</c> branches is copied verbatim from <c>FlirtyExceptionEndpointFilter</c> and
/// is load-bearing for the same reason: <see cref="AnswerValidationException"/> derives from
/// <see cref="ValidationException"/>, and <c>DialogPublishedException</c> from
/// <see cref="InvalidOperationException"/>. The compiler enforces it – a wrong order is CS0160, not a
/// warning. Two MCP-only branches follow the six deliberately, so those six read verbatim like the HTTP
/// filter.
/// </para>
/// </remarks>
internal static class FlirtyMcpExceptionFilter
{
    /// <summary>The logger category of the catch-all branch.</summary>
    private const string LoggerCategory = "Flirty.Mcp.FlirtyMcpExceptionFilter";

    /// <summary>
    /// The single filter delegate. Registered as the first call-tool filter, which the SDK composes as
    /// the outermost one – so it also wraps tools and filters a host adds to the returned builder.
    /// </summary>
    internal static McpRequestFilter<CallToolRequestParams, CallToolResult> Instance { get; } =
        next => (request, cancellationToken) => InvokeAsync(request, next, cancellationToken);

    private static async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }
        // A guard, not a mapping: the SDK owns this control flow. Placed first for readability, so the
        // six below read as branches 2-7 in the HTTP filter's order; it is in fact order-neutral,
        // because none of the three types derives from ValidationException or InvalidOperationException.
        catch (Exception exception) when (IsOwnedBySdk(exception, cancellationToken))
        {
            throw;
        }
        catch (DialogNotFoundException exception)
        {
            return Problem(StatusCodes.Status404NotFound, "Dialog not found", exception.Message);
        }
        catch (SessionNotFoundException exception)
        {
            return Problem(StatusCodes.Status404NotFound, "Session not found", exception.Message);
        }
        catch (ConfigurationNotFoundException exception)
        {
            return Problem(StatusCodes.Status404NotFound, "Not found", exception.Message);
        }
        catch (AnswerValidationException exception)
        {
            // Same key as TypedResults.ValidationProblem uses over HTTP, so the individual errors
            // survive the transport change instead of being flattened into prose.
            return Problem(
                StatusCodes.Status400BadRequest,
                "Invalid answer",
                exception.Message,
                new Dictionary<string, string[]> { ["value"] = [.. exception.Errors] });
        }
        catch (ValidationException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid request", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message);
        }
        // MCP-only: over HTTP the {id:guid} route constraint rejects an unbindable value at routing, so
        // the HTTP filter never sees this class of failure.
        catch (Exception exception) when (IsArgumentBindingFailure(exception))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Invalid request",
                $"One or more arguments of the tool '{request.Params?.Name}' are invalid: {exception.Message}");
        }
        // MCP-only: reproduces what ASP.NET Core does with an unhandled exception – a generic body plus a
        // server-side log. Without it the failure would carry no status at all, because the SDK's own
        // fallback has none.
        catch (Exception exception)
        {
            // Resolved inside the catch only, so the happy path never touches DI. GetService and not
            // GetRequiredService: a stdio host or a hand-built server may legitimately have no services,
            // and a logger is never worth throwing over inside an error handler.
            (request.Services?.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance)
                .CreateLogger(LoggerCategory)
                .LogError(
                    exception,
                    "Unhandled exception while invoking the Flirty MCP tool '{ToolName}'.",
                    request.Params?.Name);

            return Problem(
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                $"An unexpected error occurred while invoking the tool '{request.Params?.Name}'.");
        }
    }

    /// <summary>
    /// Indicates whether the SDK, not this filter, owns the exception's control flow.
    /// </summary>
    /// <remarks>
    /// The first clause is character-for-character the SDK's own predicate, including the
    /// <c>IsCancellationRequested</c> half: an <see cref="OperationCanceledException"/> <b>without</b> a
    /// cancelled token is a genuine bug and belongs in the 500 branch, exactly as the SDK treats it.
    /// Without the clause a client disconnect would be logged as a server error.
    /// <see cref="McpException"/> is the wider of the two candidates on purpose – the SDK's own rethrow set
    /// names only <c>McpProtocolException</c>, but for an <see cref="McpException"/> the SDK's fallback
    /// already preserves the message, so the problem this filter exists to solve does not arise there. The
    /// wider clause covers all protocol subtypes in one line and gives a host that adds its own tools a
    /// documented way out of Flirty's mapping.
    /// </remarks>
    private static bool IsOwnedBySdk(Exception exception, CancellationToken cancellationToken)
        => (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        || exception is McpException
        || exception is InputRequiredException;

    /// <summary>
    /// Indicates whether the exception is the SDK marshalling a tool argument, rather than a tool failing.
    /// </summary>
    /// <remarks>
    /// An unbindable value (<c>"dialogId": "not-a-guid"</c>) surfaces as a
    /// <see cref="JsonException"/>: the argument marshaller deserializes the raw <c>JsonElement</c>
    /// without a guard of its own. A missing required argument surfaces as an
    /// <see cref="ArgumentException"/> whose <c>ParamName</c> is <c>"arguments"</c> – a precise
    /// discriminator, and the reason this is not widened to a bare <see cref="ArgumentException"/>:
    /// <c>ArgumentNullException.ThrowIfNull(command)</c> fires in every Flirty handler with
    /// <c>ParamName == "command"</c>, and over HTTP such a bug is a 500.
    /// Catching <see cref="JsonException"/> as a bad request is safe because it never escapes the Flirty
    /// core – its only two deserialization sites wrap it (<c>AnswerValidator</c> into an
    /// <see cref="InvalidOperationException"/>, <c>TriggerConfig</c> into a bool plus a message).
    /// </remarks>
    private static bool IsArgumentBindingFailure(Exception exception)
        => exception is JsonException
        || exception is FormatException
        || (exception is ArgumentException { ParamName: "arguments" } and not ArgumentNullException);

    /// <summary>
    /// Builds the error result: <c>isError</c> with the message in the text block <b>and</b> a
    /// <see cref="FlirtyProblem"/> in <c>structuredContent</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately <c>isError: true</c> rather than <c>throw new McpException(...)</c> in every branch:
    /// <c>isError</c> is how the protocol reports a tool <i>execution</i> failure the model can react to,
    /// whereas a JSON-RPC error reports a <i>protocol</i> failure. It is also the shape the SDK's own
    /// fallback produces, so clients already expect it from this server.
    /// </remarks>
    private static CallToolResult Problem(
        int status,
        string title,
        string detail,
        IReadOnlyDictionary<string, string[]>? errors = null)
        => new()
        {
            IsError = true,
            // One rule for all eight branches, no per-branch prose. The LLM consumer reads this text;
            // structuredContent is for the machine reader.
            Content = [new TextContentBlock { Text = $"{title}: {detail}" }],
            StructuredContent = JsonSerializer.SerializeToElement(
                new FlirtyProblem(status, title, detail, errors), McpJsonUtilities.DefaultOptions),
        };
}
