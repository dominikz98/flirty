namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Konfigurationsobjekt für <see cref="FlirtyServiceCollectionExtensions.AddFlirty(IServiceCollection, System.Action{FlirtyOptions})"/>.
/// </summary>
/// <remarks>
/// Bewusst minimal in Issue #20 gehalten: bislang nur <see cref="ApplyMigrations"/>. Issue #34 erweitert
/// dieselbe Klasse <b>additiv</b> um die Provider-Wahl (<c>UseSqlite</c>/<c>UsePostgreSql</c>/<c>UseSqlServer</c>
/// inkl. <see cref="Flirty.Persistence.FlirtyDbContext"/>-Registrierung), Outbound-Webhooks und einen
/// austauschbaren Condition-Evaluator – ohne die bestehende Oberfläche zu brechen.
/// </remarks>
public sealed class FlirtyOptions
{
    /// <summary>
    /// Gibt an, ob beim Host-Start automatisch migriert werden soll. Wird über
    /// <see cref="ApplyMigrations"/> gesetzt und von
    /// <see cref="FlirtyServiceCollectionExtensions.AddFlirty(IServiceCollection, System.Action{FlirtyOptions})"/>
    /// ausgewertet.
    /// </summary>
    internal bool MigrationsEnabled { get; private set; }

    /// <summary>
    /// Aktiviert die Auto-Migration: registriert den
    /// <see cref="Flirty.Hosting.FlirtyMigrationHostedService"/>, der beim Host-Start alle ausstehenden
    /// EF-Core-Migrationen auf den registrierten <see cref="Flirty.Persistence.FlirtyDbContext"/> anwendet.
    /// </summary>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    public FlirtyOptions ApplyMigrations()
    {
        MigrationsEnabled = true;
        return this;
    }
}
