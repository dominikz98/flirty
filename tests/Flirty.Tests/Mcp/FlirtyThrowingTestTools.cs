using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Flirty.Runtime;
using Flirty.Validation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Flirty.Tests.Mcp;

/// <summary>
/// The exception kinds <see cref="FlirtyThrowingTestTools"/> can raise. Doubles as the evidence that an
/// enum parameter reaches the client as a <b>name</b> with an <c>enum</c> constraint in the input schema
/// rather than as a number – the C# member name verbatim, i.e. PascalCase.
/// </summary>
internal enum FlirtyTestThrowKind
{
    /// <summary>A runtime start against an unknown dialog key.</summary>
    DialogNotFound,

    /// <summary>A resume of an unknown session.</summary>
    SessionNotFound,

    /// <summary>An unknown configuration element.</summary>
    ConfigurationNotFound,

    /// <summary>An answer rejected by the answer validator.</summary>
    AnswerValidation,

    /// <summary>A request rejected by the pipeline validation.</summary>
    Validation,

    /// <summary>A key or state conflict.</summary>
    InvalidOperation,

    /// <summary>A graph change on a published dialog – an <c>InvalidOperationException</c> subtype.</summary>
    DialogPublished,

    /// <summary>An <c>McpException</c>, whose control flow the SDK owns.</summary>
    McpProtocol,

    /// <summary>A cancellation, whose control flow the SDK owns.</summary>
    Cancellation,

    /// <summary>A handler bug: an <c>ArgumentNullException</c> that must not be read as a binder failure.</summary>
    ArgumentNull,

    /// <summary>Any other exception – the catch-all branch.</summary>
    Unexpected,
}

/// <summary>
/// A test-only tool that raises a chosen engine exception through the real MCP pipeline. Registered only
/// by <see cref="FlirtyMcpTestHost"/> when asked for; it is not part of <c>Flirty.Mcp</c>.
/// </summary>
/// <remarks>
/// <para>
/// It began as a scope fact rather than a convenience: of the six engine exceptions the filter maps, only
/// three were reachable through the 27 admin tools of build-out stages 1 and 2, and without this seam the
/// acceptance criterion "maps all six" would silently have shrunk to three. That gap is now closed –
/// <see cref="FlirtyTestThrowKind.DialogPublished"/> gained a real counterpart in #127 (a graph change over
/// <c>flirty_question_create</c>), and #128 gave the remaining three one apiece over the session tools, so
/// <see cref="FlirtyMcpExceptionParityTests"/> no longer routes any of the six through here.
/// </para>
/// <para>
/// The seam stays all the same, and it is not scaffolding left behind:
/// <see cref="FlirtyTestThrowKind.McpProtocol"/>, <see cref="FlirtyTestThrowKind.Cancellation"/>,
/// <see cref="FlirtyTestThrowKind.ArgumentNull"/> and <see cref="FlirtyTestThrowKind.Unexpected"/> are
/// unreachable through any real tool <i>by design</i>, permanently – they are the branches
/// <see cref="FlirtyMcpExceptionFilterTests"/> exists for. The six engine kinds are kept beside them
/// because the mapping table is also asserted as a <i>table</i>, one row per exception, which is a
/// different claim from "this call path maps correctly" and is worth stating separately.
/// </para>
/// <para>
/// It raises each exception through its own <b>public factory</b>, the same one the engine uses, so the
/// message is identical to the one the real command produces by construction rather than by copying a
/// string. And it is driven through a real <c>McpClient</c>, so the assertions cover the real tool
/// registration, the SDK's real filter composition, the real Streamable-HTTP wire and the real ASP.NET
/// request scope – none of which a hand-built <c>RequestContext</c> would touch.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyThrowingTestTools
{
    [McpServerTool(Name = "flirty_test_throw")]
    [Description("Test-only: raises the chosen engine exception so the error mapping can be observed.")]
    internal static string Throw(
        FlirtyTestThrowKind kind,
        CancellationToken cancellationToken,
        string? key = null,
        Guid? id = null,
        string[]? errors = null)
        => kind switch
        {
            FlirtyTestThrowKind.DialogNotFound =>
                throw DialogNotFoundException.ForKey(key ?? "unknown"),
            FlirtyTestThrowKind.SessionNotFound =>
                throw SessionNotFoundException.ForId(id ?? Guid.Empty),
            FlirtyTestThrowKind.ConfigurationNotFound =>
                throw ConfigurationNotFoundException.ForDialog(id ?? Guid.Empty),
            FlirtyTestThrowKind.AnswerValidation =>
                throw AnswerValidationException.For(id ?? Guid.Empty, errors ?? ["The value must be a number."]),
            FlirtyTestThrowKind.Validation =>
                throw new ValidationException(key ?? "Validation of 'TestCommand' failed."),
            FlirtyTestThrowKind.InvalidOperation =>
                throw new InvalidOperationException(key ?? "A conflicting state was found."),
            FlirtyTestThrowKind.DialogPublished =>
                throw DialogPublishedException.ForGraphChange(key ?? "published", 1),
            FlirtyTestThrowKind.McpProtocol =>
                throw new McpException(key ?? "A protocol level failure."),
            FlirtyTestThrowKind.Cancellation =>
                throw Cancel(cancellationToken),
            FlirtyTestThrowKind.ArgumentNull =>
                throw new ArgumentNullException("command", "A handler bug, not a binder failure."),
            _ =>
                throw new NotSupportedException("A secret that must not reach the client."),
        };

    /// <summary>
    /// Cancels a token of its own and throws with it, so the filter's guard sees a cancellation whose
    /// token really is cancelled – the SDK's own predicate demands both halves.
    /// </summary>
    private static Exception Cancel(CancellationToken cancellationToken)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.Cancel();
        return new OperationCanceledException(source.Token);
    }
}
