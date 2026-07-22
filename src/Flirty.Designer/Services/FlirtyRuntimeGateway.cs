using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Flirty.Runtime;
using Flirty.Validation;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Führt die Laufzeit-Operationen der Engine (<see cref="IFlirtyEngine"/>) für den Test-Runner (#43) aus –
/// jede in einem eigenen, frischen DI-Scope. Begründung und Scope-Mechanik stehen in der Basis
/// <see cref="DesignerGateway"/>; Pendant zum <see cref="FlirtyAdminGateway"/> des Admin-CRUD.
/// </summary>
/// <remarks>
/// Das Fehler-Mapping deckt zusätzlich die Ausnahmen der Laufzeit ab, die im Admin-CRUD nicht vorkommen
/// (<see cref="DialogNotFoundException"/>, <see cref="SessionNotFoundException"/>,
/// <see cref="AnswerValidationException"/>). Ohne sie risse eine schlicht falsch eingetippte Antwort den
/// Blazor-Circuit – der Runner ist aber genau das Werkzeug, mit dem man solche Fälle provoziert.
/// </remarks>
internal sealed class FlirtyRuntimeGateway : DesignerGateway
{
    private readonly DesignerTriggerLog _log;

    /// <summary>Erstellt das Gateway.</summary>
    /// <param name="scopeFactory">Factory für den je Operation erzeugten Kind-Scope.</param>
    /// <param name="active">Das aktive Connection-Profil des aufrufenden Circuits.</param>
    /// <param name="log">Das Trigger-Protokoll des aufrufenden Circuits.</param>
    public FlirtyRuntimeGateway(
        IServiceScopeFactory scopeFactory, ActiveConnectionProfile active, DesignerTriggerLog log)
        : base(scopeFactory, active)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
    }

    /// <summary>
    /// Führt die angegebene Operation über eine frische <see cref="IFlirtyEngine"/> aus und bildet die von
    /// der Engine geworfenen Ausnahmen auf eine anzeigbare Meldung ab.
    /// </summary>
    /// <typeparam name="TValue">Der Ergebnistyp der Operation.</typeparam>
    /// <param name="operation">
    /// Die auszuführende Operation, z. B.
    /// <c>(engine, token) =&gt; engine.ResumeDialogAsync(sessionId, token)</c>.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen der Operation.</param>
    /// <returns>Das Ergebnis der Operation oder eine deutsche Fehlermeldung.</returns>
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
    /// Reicht das Trigger-Protokoll des Circuits in den Kind-Scope durch – sonst schrieben die dort
    /// konstruierten Notification-Handler in eine Wegwerf-Instanz.
    /// </summary>
    /// <param name="scopedProvider">Der Service-Provider des Kind-Scopes.</param>
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

            // Muss VOR ValidationException stehen (leitet davon ab). Bewusst die Einzelverstöße statt
            // exception.Message: die Meldung führt die rohe Frage-GUID mit, die im UI nur stört.
            AnswerValidationException answerValidation => DescribeInvalidAnswer(answerValidation),
            ValidationException => exception.Message,
            DbUpdateException => DescribeDatabaseError(exception),
            DbException => DescribeDatabaseError(exception),

            // Session nicht offen, Frage nicht die aktuelle, fehlkonfiguriertes Branching (kein
            // greifender Übergang), überlappende Schleifen – oder kein aktives Connection-Profil.
            InvalidOperationException => exception.Message,
            _ => null,
        };

    /// <summary>Formuliert die abgelehnte Antwort als Meldung ohne technische Bezeichner.</summary>
    /// <param name="exception">Die Ausnahme der Antwort-Validierung.</param>
    /// <returns>Die anzuzeigende Meldung.</returns>
    private static string DescribeInvalidAnswer(AnswerValidationException exception)
        => exception.Errors.Count == 0
            ? "Antwort ungültig."
            : $"Antwort ungültig: {string.Join(" ", exception.Errors)}";
}
