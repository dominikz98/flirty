using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Tests for the public provider mapping (#37): per <see cref="FlirtyDatabaseProvider"/>,
/// <see cref="FlirtyDatabaseProviderExtensions.UseFlirtyProvider"/> has to set the correct EF Core
/// provider and the matching <c>MigrationsAssembly</c>. On top, it is verified that the
/// <c>FlirtyOptions.Use*</c> methods still delegate to that same mapping.
/// </summary>
public sealed class FlirtyDatabaseProviderExtensionsTests
{
    [Theory]
    [InlineData(FlirtyDatabaseProvider.Sqlite, "Microsoft.EntityFrameworkCore.Sqlite", "Flirty.Migrations.Sqlite")]
    [InlineData(FlirtyDatabaseProvider.PostgreSql, "Npgsql.EntityFrameworkCore.PostgreSQL", "Flirty.Migrations.PostgreSql")]
    [InlineData(FlirtyDatabaseProvider.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer", "Flirty.Migrations.SqlServer")]
    public void UseFlirtyProvider_sets_the_provider_and_the_migrations_assembly(
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
    public void UseFlirtyProvider_throws_on_an_empty_connection_string()
    {
        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        Assert.Throws<ArgumentException>(() => builder.UseFlirtyProvider(FlirtyDatabaseProvider.Sqlite, "  "));
    }

    [Fact]
    public void UseSqlite_delegates_to_the_same_mapping()
    {
        var options = new FlirtyOptions();
        options.UseSqlite("Data Source=flirty.db");

        Assert.Equal("Flirty.Migrations.Sqlite", MigrationsAssemblyOf(BuildOptions(options)));
    }

    [Fact]
    public void UseProvider_sets_the_chosen_mapping()
    {
        var options = new FlirtyOptions();
        options.UseProvider(FlirtyDatabaseProvider.PostgreSql, "Host=localhost;Database=flirty");

        Assert.Equal("Flirty.Migrations.PostgreSql", MigrationsAssemblyOf(BuildOptions(options)));
    }

    private static DbContextOptions<FlirtyDbContext> BuildOptions(FlirtyOptions flirtyOptions)
    {
        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        var configure = flirtyOptions.ConfigureDbContext
            ?? throw new InvalidOperationException("ConfigureDbContext was not set.");
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
