namespace Flirty.Designer.Services;

/// <summary>
/// Result of an engine operation executed via a <see cref="DesignerGateway"/>. Deliberately
/// result-based rather than exception-based (analogous to <see cref="ConnectionProfileOperations"/>), so
/// the Blazor pages can show errors as a message instead of crashing the circuit.
/// </summary>
/// <typeparam name="TValue">The result type of the operation.</typeparam>
/// <param name="Success">Indicates whether the operation was successful.</param>
/// <param name="Value">The result on success, otherwise <c>default</c>.</param>
/// <param name="Error">The error message on failure, otherwise <c>null</c>.</param>
internal sealed record GatewayResult<TValue>(bool Success, TValue? Value, string? Error)
{
    /// <summary>Creates a success result.</summary>
    /// <param name="value">The return value of the operation.</param>
    public static GatewayResult<TValue> Ok(TValue value) => new(true, value, null);

    /// <summary>Creates an error result.</summary>
    /// <param name="error">The error message to display.</param>
    public static GatewayResult<TValue> Failed(string error) => new(false, default, error);
}

/// <summary>
/// Shared base of the designer gateways (<see cref="FlirtyAdminGateway"/> for the admin CRUD,
/// <see cref="FlirtyRuntimeGateway"/> for the test runner): runs every engine operation in its
/// <b>own, fresh DI scope</b> and maps the exceptions thrown by the engine to a displayable message.
/// </summary>
/// <remarks>
/// <para>
/// Reason for the dedicated scope (#38): in Blazor Server a DI scope corresponds to a <i>circuit</i>. The
/// <c>FlirtyDbContext</c> registered scoped in <c>Program.cs</c> would thus live for the whole session
/// and would be pinned to whichever connection profile was active on first use – a later profile switch
/// under "Connections" would have no effect. In addition the change tracker would accumulate entities over
/// the whole session and the context (not thread-safe) would be shared by parallel render paths.
/// One scope per operation solves all three points.
/// </para>
/// <para>
/// The circuit's active profile is passed into the child scope via <see cref="ActiveConnectionProfile.Adopt"/>;
/// the store default alone is not enough, because multiple circuits can have different
/// profiles active. Further circuit state (such as the trigger log of the test runner) is passed along by
/// derived gateways via <see cref="Prepare"/>.
/// </para>
/// <para>
/// The error mapping of the derivations deliberately mirrors the <c>FlirtyExceptionEndpointFilter</c> from
/// <c>Flirty.AspNetCore</c> (same order of branches): not-found before validation before the generic
/// conflict branch. What <see cref="Describe"/> answers with <see langword="null"/> intentionally bubbles
/// on into the Blazor error UI.
/// </para>
/// </remarks>
internal abstract class DesignerGateway
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActiveConnectionProfile _active;

    /// <summary>Creates the gateway.</summary>
    /// <param name="scopeFactory">Factory for the child scope created per operation.</param>
    /// <param name="active">The active connection profile of the calling circuit.</param>
    protected DesignerGateway(IServiceScopeFactory scopeFactory, ActiveConnectionProfile active)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(active);

        _scopeFactory = scopeFactory;
        _active = active;
    }

    /// <summary>
    /// Runs the given operation in a fresh DI scope and maps known exceptions to a displayable message.
    /// </summary>
    /// <typeparam name="TValue">The result type of the operation.</typeparam>
    /// <param name="operation">The operation, which resolves its services from the child scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the operation or an error message.</returns>
    protected async Task<GatewayResult<TValue>> ExecuteInScopeAsync<TValue>(
        Func<IServiceProvider, CancellationToken, ValueTask<TValue>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = _scopeFactory.CreateAsyncScope();

        // Adopt the circuit's profile into the child scope. If none is active, the
        // FlirtyDesignerDbContextFactory below throws its already-worded message -> do not duplicate it.
        if (_active.Current is { } profile)
        {
            scope.ServiceProvider.GetRequiredService<ActiveConnectionProfile>().Adopt(profile);
        }

        Prepare(scope.ServiceProvider);

        try
        {
            return GatewayResult<TValue>.Ok(await operation(scope.ServiceProvider, cancellationToken));
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            return GatewayResult<TValue>.Failed(message);
        }
    }

    /// <summary>
    /// Hook to pass further state of the calling circuit into the freshly created child scope.
    /// The base only passes the active connection profile through.
    /// </summary>
    /// <param name="scopedProvider">The service provider of the child scope.</param>
    protected virtual void Prepare(IServiceProvider scopedProvider)
    {
    }

    /// <summary>
    /// Formulates the message to display for an exception thrown by the engine.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <returns>
    /// The message, or <see langword="null"/> if the exception should not be handled (it is then
    /// passed on).
    /// </returns>
    protected abstract string? Describe(Exception exception);

    /// <summary>
    /// Formulates a database error with the most common cause in the designer: the database of the active
    /// profile has not been migrated yet (a fresh SQLite file &#8594; "no such table").
    /// </summary>
    /// <param name="exception">The database exception that occurred.</param>
    /// <returns>The message to display.</returns>
    protected static string DescribeDatabaseError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return $"Database error: {(exception.InnerException ?? exception).Message} "
            + "Is the active profile's database migrated? (Connections → \"Migrate\")";
    }
}
