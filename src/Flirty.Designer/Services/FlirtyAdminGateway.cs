using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Flirty.Runtime;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Runs the admin CRUD messages of the engine (<c>src/Flirty/Runtime/Admin</c>) for the designer –
/// each operation in its own, fresh DI scope (#38). Rationale and scope mechanics are in the
/// base <see cref="DesignerGateway"/>.
/// </summary>
internal sealed class FlirtyAdminGateway : DesignerGateway
{
    /// <summary>Creates the gateway.</summary>
    /// <param name="scopeFactory">Factory for the child scope created per operation.</param>
    /// <param name="active">The active connection profile of the calling circuit.</param>
    public FlirtyAdminGateway(IServiceScopeFactory scopeFactory, ActiveConnectionProfile active)
        : base(scopeFactory, active)
    {
    }

    /// <summary>
    /// Runs the given operation via a fresh <see cref="ISender"/> and maps the exceptions thrown by
    /// the engine onto a displayable message.
    /// </summary>
    /// <typeparam name="TValue">The result type of the operation.</typeparam>
    /// <param name="operation">
    /// The operation to run, e.g. <c>(sender, token) =&gt; sender.Send(new ListDialogsQuery(), token)</c>.
    /// Deliberately as a delegate (rather than an <c>IRequest&lt;T&gt;</c> parameter), so that the strongly typed
    /// <see cref="ISender"/> overloads are bound – as with the ASP.NET endpoints.
    /// </param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>The result of the operation or a German error message.</returns>
    public Task<GatewayResult<TValue>> ExecuteAsync<TValue>(
        Func<ISender, CancellationToken, ValueTask<TValue>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteInScopeAsync(
            (provider, token) => operation(provider.GetRequiredService<ISender>(), token),
            cancellationToken);
    }

    /// <inheritdoc />
    protected override string? Describe(Exception exception)
        => exception switch
        {
            ConfigurationNotFoundException => exception.Message,
            ValidationException => exception.Message,
            DbUpdateException => DescribeDatabaseError(exception),
            DbException => DescribeDatabaseError(exception),

            // Key conflict, publish without entry question – or no active connection profile.
            InvalidOperationException => exception.Message,
            _ => null,
        };
}
