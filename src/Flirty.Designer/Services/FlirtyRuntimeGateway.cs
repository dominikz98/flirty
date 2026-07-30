using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Flirty.Runtime;
using Flirty.Validation;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Runs the engine's runtime operations (<see cref="IFlirtyEngine"/>) for the test runner (#43) –
/// each in its own, fresh DI scope. Rationale and scope mechanics are in the base
/// <see cref="DesignerGateway"/>; counterpart to the <see cref="FlirtyAdminGateway"/> of the admin CRUD.
/// </summary>
/// <remarks>
/// The error mapping additionally covers the runtime exceptions that do not occur in the admin CRUD
/// (<see cref="DialogNotFoundException"/>, <see cref="SessionNotFoundException"/>,
/// <see cref="AnswerValidationException"/>). Without them a simply mistyped answer would tear down the
/// Blazor circuit – but the runner is exactly the tool with which one provokes such cases.
/// </remarks>
internal sealed class FlirtyRuntimeGateway : DesignerGateway
{
    private readonly DesignerTriggerLog _log;

    /// <summary>Creates the gateway.</summary>
    /// <param name="scopeFactory">Factory for the child scope created per operation.</param>
    /// <param name="active">The active connection profile of the calling circuit.</param>
    /// <param name="log">The trigger log of the calling circuit.</param>
    public FlirtyRuntimeGateway(
        IServiceScopeFactory scopeFactory, ActiveConnectionProfile active, DesignerTriggerLog log)
        : base(scopeFactory, active)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
    }

    /// <summary>
    /// Runs the given operation via a fresh <see cref="IFlirtyEngine"/> and maps the exceptions thrown by
    /// the engine to a displayable message.
    /// </summary>
    /// <typeparam name="TValue">The result type of the operation.</typeparam>
    /// <param name="operation">
    /// The operation to run, e.g.
    /// <c>(engine, token) =&gt; engine.ResumeDialogAsync(sessionId, token)</c>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the operation or an error message.</returns>
    public Task<GatewayResult<TValue>> ExecuteAsync<TValue>(
        Func<IFlirtyEngine, CancellationToken, Task<TValue>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteInScopeAsync<TValue>(
            async (provider, token) => await operation(provider.GetRequiredService<IFlirtyEngine>(), token),
            cancellationToken);
    }

    /// <summary>
    /// Passes the circuit's trigger log into the child scope – otherwise the notification handlers
    /// constructed there would write into a throwaway instance.
    /// </summary>
    /// <param name="scopedProvider">The service provider of the child scope.</param>
    protected override void Prepare(IServiceProvider scopedProvider)
    {
        ArgumentNullException.ThrowIfNull(scopedProvider);

        scopedProvider.GetRequiredService<DesignerTriggerLog>().Adopt(_log);
    }

    /// <inheritdoc />
    protected override string? Describe(Exception exception)
        => exception switch
        {
            DialogNotFoundException => exception.Message,
            SessionNotFoundException => exception.Message,
            ConfigurationNotFoundException => exception.Message,

            // Must come BEFORE ValidationException (which it derives from). Deliberately the individual
            // violations instead of exception.Message: that message carries the raw question GUID, which
            // only clutters the UI.
            AnswerValidationException answerValidation => DescribeInvalidAnswer(answerValidation),
            ValidationException => exception.Message,
            DbUpdateException => DescribeDatabaseError(exception),
            DbException => DescribeDatabaseError(exception),

            // Session not open, question not the current one, misconfigured branching (no matching
            // transition), overlapping loops – or no active connection profile.
            InvalidOperationException => exception.Message,
            _ => null,
        };

    /// <summary>Formulates the rejected answer as a message without technical identifiers.</summary>
    /// <param name="exception">The answer validation exception.</param>
    /// <returns>The message to display.</returns>
    private static string DescribeInvalidAnswer(AnswerValidationException exception)
        => exception.Errors.Count == 0
            ? "Answer invalid."
            : $"Answer invalid: {string.Join(" ", exception.Errors)}";
}
