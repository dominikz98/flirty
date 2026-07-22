namespace Flirty.Designer.Services;

/// <summary>
/// Ergebnis einer über ein <see cref="DesignerGateway"/> ausgeführten Engine-Operation. Bewusst
/// ergebnis- statt ausnahmebasiert (analog <see cref="ConnectionProfileOperations"/>), damit die
/// Blazor-Seiten Fehler als Meldung anzeigen können, statt den Circuit abstürzen zu lassen.
/// </summary>
/// <typeparam name="TValue">Der Ergebnistyp der Operation.</typeparam>
/// <param name="Success">Gibt an, ob die Operation erfolgreich war.</param>
/// <param name="Value">Das Ergebnis bei Erfolg, sonst <c>default</c>.</param>
/// <param name="Error">Die deutsche Fehlermeldung bei Misserfolg, sonst <c>null</c>.</param>
internal sealed record GatewayResult<TValue>(bool Success, TValue? Value, string? Error)
{
    /// <summary>Erzeugt ein Erfolgsergebnis.</summary>
    /// <param name="value">Der Rückgabewert der Operation.</param>
    public static GatewayResult<TValue> Ok(TValue value) => new(true, value, null);

    /// <summary>Erzeugt ein Fehlerergebnis.</summary>
    /// <param name="error">Die anzuzeigende Fehlermeldung.</param>
    public static GatewayResult<TValue> Failed(string error) => new(false, default, error);
}

/// <summary>
/// Gemeinsame Basis der Designer-Gateways (<see cref="FlirtyAdminGateway"/> für das Admin-CRUD,
/// <see cref="FlirtyRuntimeGateway"/> für den Test-Runner): führt jede Engine-Operation in einem
/// <b>eigenen, frischen DI-Scope</b> aus und bildet die von der Engine geworfenen Ausnahmen auf eine
/// anzeigbare Meldung ab.
/// </summary>
/// <remarks>
/// <para>
/// Grund für den eigenen Scope (#38): In Blazor Server entspricht ein DI-Scope einem <i>Circuit</i>. Der in
/// <c>Program.cs</c> scoped registrierte <c>FlirtyDbContext</c> würde damit für die gesamte Sitzung leben
/// und wäre an dasjenige Connection-Profil gepinnt, das beim ersten Zugriff aktiv war – ein späterer
/// Profilwechsel unter „Verbindungen“ bliebe wirkungslos. Zusätzlich sammelte der Change-Tracker über die
/// ganze Sitzung Entities an und der Kontext (nicht threadsicher) würde von parallelen Renderpfaden geteilt.
/// Pro Operation ein Scope löst alle drei Punkte.
/// </para>
/// <para>
/// Das aktive Profil des Circuits wird dabei per <see cref="ActiveConnectionProfile.Adopt"/> in den
/// Kind-Scope durchgereicht; der Store-Default allein genügt nicht, weil mehrere Circuits unterschiedliche
/// Profile aktiv haben können. Weitere Circuit-Zustände (etwa das Trigger-Protokoll des Test-Runners)
/// reichen abgeleitete Gateways über <see cref="Prepare"/> nach.
/// </para>
/// <para>
/// Das Fehler-Mapping der Ableitungen spiegelt bewusst den <c>FlirtyExceptionEndpointFilter</c> aus
/// <c>Flirty.AspNetCore</c> (gleiche Reihenfolge der Zweige): Not-Found vor Validierung vor dem generischen
/// Konflikt-Zweig. Was <see cref="Describe"/> mit <see langword="null"/> beantwortet, blubbert absichtlich
/// weiter in die Blazor-Fehler-UI.
/// </para>
/// </remarks>
internal abstract class DesignerGateway
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActiveConnectionProfile _active;

    /// <summary>Erstellt das Gateway.</summary>
    /// <param name="scopeFactory">Factory für den je Operation erzeugten Kind-Scope.</param>
    /// <param name="active">Das aktive Connection-Profil des aufrufenden Circuits.</param>
    protected DesignerGateway(IServiceScopeFactory scopeFactory, ActiveConnectionProfile active)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(active);

        _scopeFactory = scopeFactory;
        _active = active;
    }

    /// <summary>
    /// Führt die angegebene Operation in einem frischen DI-Scope aus und bildet bekannte Ausnahmen auf
    /// eine anzeigbare Meldung ab.
    /// </summary>
    /// <typeparam name="TValue">Der Ergebnistyp der Operation.</typeparam>
    /// <param name="operation">Die Operation, die ihre Dienste aus dem Kind-Scope auflöst.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Operation.</param>
    /// <returns>Das Ergebnis der Operation oder eine deutsche Fehlermeldung.</returns>
    protected async Task<GatewayResult<TValue>> ExecuteInScopeAsync<TValue>(
        Func<IServiceProvider, CancellationToken, ValueTask<TValue>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = _scopeFactory.CreateAsyncScope();

        // Profil des Circuits in den Kind-Scope übernehmen. Ist keins aktiv, wirft die
        // FlirtyDesignerDbContextFactory unten ihre bereits formulierte Meldung -> nicht duplizieren.
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
    /// Hook, um weitere Zustände des aufrufenden Circuits in den frisch erzeugten Kind-Scope
    /// durchzureichen. Die Basis reicht nur das aktive Connection-Profil durch.
    /// </summary>
    /// <param name="scopedProvider">Der Service-Provider des Kind-Scopes.</param>
    protected virtual void Prepare(IServiceProvider scopedProvider)
    {
    }

    /// <summary>
    /// Formuliert die anzuzeigende Meldung zu einer von der Engine geworfenen Ausnahme.
    /// </summary>
    /// <param name="exception">Die aufgetretene Ausnahme.</param>
    /// <returns>
    /// Die deutsche Meldung oder <see langword="null"/>, wenn die Ausnahme nicht behandelt werden soll
    /// (sie wird dann weitergereicht).
    /// </returns>
    protected abstract string? Describe(Exception exception);

    /// <summary>
    /// Formuliert einen Datenbankfehler mit dem im Designer häufigsten Grund: die Datenbank des aktiven
    /// Profils ist noch nicht migriert (frische SQLite-Datei &#8594; „no such table“).
    /// </summary>
    /// <param name="exception">Die aufgetretene Datenbank-Ausnahme.</param>
    /// <returns>Die anzuzeigende Meldung.</returns>
    protected static string DescribeDatabaseError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return $"Datenbankfehler: {(exception.InnerException ?? exception).Message} "
            + "Ist die Datenbank des aktiven Profils migriert? (Verbindungen → „Migrieren“)";
    }
}
