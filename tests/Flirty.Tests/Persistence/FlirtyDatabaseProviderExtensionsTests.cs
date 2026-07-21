using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Tests für die öffentliche Provider-Abbildung (#37): <see cref="FlirtyDatabaseProviderExtensions.UseFlirtyProvider"/>
/// muss je <see cref="FlirtyDatabaseProvider"/> den korrekten EF-Core-Provider und die passende
/// <c>MigrationsAssembly</c> setzen. Zusätzlich wird verifiziert, dass die <c>FlirtyOptions.Use*</c>-Methoden
/// weiterhin auf dieselbe Abbildung delegieren.
/// </summary>
public sealed class FlirtyDatabaseProviderExtensionsTests
{
    [Theory]
    [InlineData(FlirtyDatabaseProvider.Sqlite, "Microsoft.EntityFrameworkCore.Sqlite", "Flirty.Migrations.Sqlite")]
    [InlineData(FlirtyDatabaseProvider.PostgreSql, "Npgsql.EntityFrameworkCore.PostgreSQL", "Flirty.Migrations.PostgreSql")]
    [InlineData(FlirtyDatabaseProvider.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer", "Flirty.Migrations.SqlServer")]
    public void UseFlirtyProvider_setzt_Provider_und_MigrationsAssembly(
        FlirtyDatabaseProvider provider,
        string expectedProviderName,
        string expectedMigrationsAssembly)
    {
        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        builder.UseFlirtyProvider(provider, ConnectionStringFor(provider));

        var options = builder.Options;

        using var context = new FlirtyDbContext(options);
        Assert.Equal(expectedProviderName, context.Database.ProviderName);
        Assert.Equal(expectedMigrationsAssembly, MigrationsAssemblyOf(options));
    }

    [Fact]
    public void UseFlirtyProvider_wirft_bei_leerem_ConnectionString()
    {
        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        Assert.Throws<ArgumentException>(() => builder.UseFlirtyProvider(FlirtyDatabaseProvider.Sqlite, "  "));
    }

    [Fact]
    public void UseSqlite_delegiert_auf_dieselbe_Abbildung()
    {
        var options = new FlirtyOptions();
        options.UseSqlite("Data Source=flirty.db");

        Assert.Equal("Flirty.Migrations.Sqlite", MigrationsAssemblyOf(BuildOptions(options)));
    }

    [Fact]
    public void UseProvider_setzt_die_gewaehlte_Abbildung()
    {
        var options = new FlirtyOptions();
        options.UseProvider(FlirtyDatabaseProvider.PostgreSql, "Host=localhost;Database=flirty");

        Assert.Equal("Flirty.Migrations.PostgreSql", MigrationsAssemblyOf(BuildOptions(options)));
    }

    private static DbContextOptions<FlirtyDbContext> BuildOptions(FlirtyOptions flirtyOptions)
    {
        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        var configure = flirtyOptions.ConfigureDbContext
            ?? throw new InvalidOperationException("ConfigureDbContext wurde nicht gesetzt.");
        configure(builder);
        return builder.Options;
    }

    private static string? MigrationsAssemblyOf(DbContextOptions options)
        => options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Select(extension => extension.MigrationsAssembly)
            .FirstOrDefault(assembly => assembly is not null);

    private static string ConnectionStringFor(FlirtyDatabaseProvider provider) => provider switch
    {
        FlirtyDatabaseProvider.Sqlite => "Data Source=flirty.db",
        FlirtyDatabaseProvider.PostgreSql => "Host=localhost;Database=flirty;Username=flirty;Password=flirty",
        FlirtyDatabaseProvider.SqlServer => "Server=localhost;Database=flirty;Trusted_Connection=True;TrustServerCertificate=True",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
