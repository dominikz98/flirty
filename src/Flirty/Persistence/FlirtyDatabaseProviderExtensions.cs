using Flirty.Persistence;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension-Methoden, die einen <see cref="FlirtyDatabaseProvider"/> auf die passende EF-Core-Provider-
/// Registrierung des <see cref="DbContextOptionsBuilder"/> inklusive korrekter <c>MigrationsAssembly</c>
/// abbilden.
/// </summary>
/// <remarks>
/// Eingeführt in Issue #37 als <b>einzige</b> Stelle, an der die drei Migrations-Assembly-Namen
/// (<c>Flirty.Migrations.Sqlite</c>/<c>PostgreSql</c>/<c>SqlServer</c>) verankert sind. Sowohl die
/// <c>FlirtyOptions.Use*</c>-Methoden (Provider-Wahl zur DI-Zeit) als auch die Laufzeit-Profilwahl des
/// Designers (Multi-DB) nutzen diese Abbildung, damit das Mapping nicht dupliziert wird.
/// </remarks>
public static class FlirtyDatabaseProviderExtensions
{
    /// <summary>
    /// Konfiguriert den <paramref name="builder"/> für den angegebenen <paramref name="provider"/> mit der
    /// Verbindungszeichenfolge <paramref name="connectionString"/> und der zum Provider gehörenden
    /// <c>MigrationsAssembly</c>.
    /// </summary>
    /// <param name="builder">Der zu konfigurierende Options-Builder.</param>
    /// <param name="provider">Der zu verwendende Datenbank-Provider.</param>
    /// <param name="connectionString">Die Verbindungszeichenfolge für den gewählten Provider.</param>
    /// <returns>Denselben <paramref name="builder"/>, um Aufrufe verketten zu können.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="builder"/> ist <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="connectionString"/> ist leer oder nur Leerraum.</exception>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException"><paramref name="provider"/> ist kein definierter Wert.</exception>
    public static DbContextOptionsBuilder UseFlirtyProvider(
        this DbContextOptionsBuilder builder,
        FlirtyDatabaseProvider provider,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return provider switch
        {
            FlirtyDatabaseProvider.Sqlite =>
                builder.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("Flirty.Migrations.Sqlite")),
            FlirtyDatabaseProvider.PostgreSql =>
                builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Flirty.Migrations.PostgreSql")),
            FlirtyDatabaseProvider.SqlServer =>
                builder.UseSqlServer(connectionString, sqlServer => sqlServer.MigrationsAssembly("Flirty.Migrations.SqlServer")),
            _ => throw new System.ComponentModel.InvalidEnumArgumentException(
                nameof(provider), (int)provider, typeof(FlirtyDatabaseProvider)),
        };
    }
}
