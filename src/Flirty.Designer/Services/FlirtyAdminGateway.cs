using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Flirty.Runtime;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Führt die Admin-CRUD-Nachrichten der Engine (<c>src/Flirty/Runtime/Admin</c>) für den Designer aus –
/// jede Operation in einem eigenen, frischen DI-Scope (#38). Begründung und Scope-Mechanik stehen in der
/// Basis <see cref="DesignerGateway"/>.
/// </summary>
internal sealed class FlirtyAdminGateway : DesignerGateway
{
    /// <summary>Erstellt das Gateway.</summary>
    /// <param name="scopeFactory">Factory für den je Operation erzeugten Kind-Scope.</param>
    /// <param name="active">Das aktive Connection-Profil des aufrufenden Circuits.</param>
    public FlirtyAdminGateway(IServiceScopeFactory scopeFactory, ActiveConnectionProfile active)
        : base(scopeFactory, active)
    {
    }

    /// <summary>
    /// Führt die angegebene Operation über einen frischen <see cref="ISender"/> aus und bildet die von
    /// der Engine geworfenen Ausnahmen auf eine anzeigbare Meldung ab.
    /// </summary>
    /// <typeparam name="TValue">Der Ergebnistyp der Operation.</typeparam>
    /// <param name="operation">
    /// Die auszuführende Operation, z. B. <c>(sender, token) =&gt; sender.Send(new ListDialogsQuery(), token)</c>.
    /// Bewusst als Delegat (statt <c>IRequest&lt;T&gt;</c>-Parameter), damit die stark typisierten
    /// <see cref="ISender"/>-Overloads gebunden werden – wie bei den ASP.NET-Endpunkten.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen der Operation.</param>
    /// <returns>Das Ergebnis der Operation oder eine deutsche Fehlermeldung.</returns>
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

            // Schlüsselkonflikt, Publish ohne Einstiegsfrage – oder kein aktives Connection-Profil.
            InvalidOperationException => exception.Message,
            _ => null,
        };
}
