using Flirty.Persistence;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods that map a <see cref="FlirtyDatabaseProvider"/> to the matching EF Core provider
/// registration of the <see cref="DbContextOptionsBuilder"/> including the correct <c>MigrationsAssembly</c>.
/// </summary>
/// <remarks>
/// Introduced in issue #37 as the <b>single</b> place where the three migrations assembly names
/// (<c>Flirty.Migrations.Sqlite</c>/<c>PostgreSql</c>/<c>SqlServer</c>) are anchored. Both the
/// <c>FlirtyOptions.Use*</c> methods (provider choice at DI time) and the runtime profile choice of the
/// designer (multi-DB) use this mapping so that it is not duplicated.
/// </remarks>
public static class FlirtyDatabaseProviderExtensions
{
    /// <summary>
    /// Configures the <paramref name="builder"/> for the given <paramref name="provider"/> with the
    /// connection string <paramref name="connectionString"/> and the <c>MigrationsAssembly</c> belonging
    /// to the provider.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="provider">The database provider to use.</param>
    /// <param name="connectionString">The connection string for the chosen provider.</param>
    /// <returns>The same <paramref name="builder"/> so calls can be chained.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="connectionString"/> is empty or whitespace only.</exception>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException"><paramref name="provider"/> is not a defined value.</exception>
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
