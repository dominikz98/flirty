namespace Flirty.Persistence;

/// <summary>
/// Die von Flirty unterstützten EF-Core-Datenbank-Provider. Jeder Wert ist über
/// <see cref="Microsoft.EntityFrameworkCore.FlirtyDatabaseProviderExtensions"/> (<c>UseFlirtyProvider</c>)
/// eindeutig einer EF-Core-Provider-Registrierung und der passenden <c>MigrationsAssembly</c>
/// (<c>Flirty.Migrations.Sqlite</c>/<c>PostgreSql</c>/<c>SqlServer</c>) zugeordnet.
/// </summary>
/// <remarks>
/// Eingeführt in Issue #37: erlaubt die Provider-Wahl als <b>Wert</b> (statt über getrennte
/// <c>Use*</c>-Methoden) und damit die Auswahl des Providers erst <b>zur Laufzeit</b> – die Grundlage
/// für die Connection-Profil-Verwaltung des Blazor-Designers (Multi-DB via
/// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>).
/// </remarks>
public enum FlirtyDatabaseProvider
{
    /// <summary>SQLite (Migrations-Assembly <c>Flirty.Migrations.Sqlite</c>).</summary>
    Sqlite,

    /// <summary>PostgreSQL via Npgsql (Migrations-Assembly <c>Flirty.Migrations.PostgreSql</c>).</summary>
    PostgreSql,

    /// <summary>Microsoft SQL Server (Migrations-Assembly <c>Flirty.Migrations.SqlServer</c>).</summary>
    SqlServer,
}
