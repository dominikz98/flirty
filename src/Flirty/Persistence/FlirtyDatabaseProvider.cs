namespace Flirty.Persistence;

/// <summary>
/// The EF Core database providers supported by Flirty. Each value is, via
/// <see cref="Microsoft.EntityFrameworkCore.FlirtyDatabaseProviderExtensions"/> (<c>UseFlirtyProvider</c>),
/// uniquely mapped to an EF Core provider registration and the matching <c>MigrationsAssembly</c>
/// (<c>Flirty.Migrations.Sqlite</c>/<c>PostgreSql</c>/<c>SqlServer</c>).
/// </summary>
/// <remarks>
/// Introduced in issue #37: allows choosing the provider as a <b>value</b> (instead of via separate
/// <c>Use*</c> methods) and thereby selecting the provider only <b>at runtime</b> - the basis
/// for the connection-profile management of the Blazor designer (multi-DB via
/// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>).
/// </remarks>
public enum FlirtyDatabaseProvider
{
    /// <summary>SQLite (migrations assembly <c>Flirty.Migrations.Sqlite</c>).</summary>
    Sqlite,

    /// <summary>PostgreSQL via Npgsql (migrations assembly <c>Flirty.Migrations.PostgreSql</c>).</summary>
    PostgreSql,

    /// <summary>Microsoft SQL Server (migrations assembly <c>Flirty.Migrations.SqlServer</c>).</summary>
    SqlServer,
}
