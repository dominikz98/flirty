using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Flirty.Runtime;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Ergebnis einer über den <see cref="FlirtyAdminGateway"/> ausgeführten Admin-Operation. Bewusst
/// ergebnis- statt ausnahmebasiert (analog <see cref="ConnectionProfileOperations"/>), damit die
/// Blazor-Seiten Fehler als Meldung anzeigen können, statt den Circuit abstürzen zu lassen.
/// </summary>
/// <typeparam name="TValue">Der Ergebnistyp der Operation.</typeparam>
/// <param name="Success">Gibt an, ob die Operation erfolgreich war.</param>
/// <param name="Value">Das Ergebnis bei Erfolg, sonst <c>default</c>.</param>
/// <param name="Error">Die deutsche Fehlermeldung bei Misserfolg, sonst <c>null</c>.</param>
internal sealed record AdminResult<TValue>(bool Success, TValue? Value, string? Error)
{
    /// <summary>Erzeugt ein Erfolgsergebnis.</summary>
    /// <param name="value">Der Rückgabewert der Operation.</param>
    public static AdminResult<TValue> Ok(TValue value) => new(true, value, null);

    /// <summary>Erzeugt ein Fehlerergebnis.</summary>
    /// <param name="error">Die anzuzeigende Fehlermeldung.</param>
    public static AdminResult<TValue> Failed(string error) => new(false, default, error);
}

/// <summary>
/// Führt die Admin-CRUD-Nachrichten der Engine (<c>src/Flirty/Runtime/Admin</c>) für den Designer aus –
/// jede Operation in einem <b>eigenen, frischen DI-Scope</b> (#38).
/// </summary>
/// <remarks>
/// <para>
/// Grund für den eigenen Scope: In Blazor Server entspricht ein DI-Scope einem <i>Circuit</i>. Der in
/// <c>Program.cs</c> scoped registrierte <c>FlirtyDbContext</c> würde damit für die gesamte Sitzung leben
/// und wäre an dasjenige Connection-Profil gepinnt, das beim ersten Zugriff aktiv war – ein späterer
/// Profilwechsel unter „Verbindungen“ bliebe wirkungslos. Zusätzlich sammelte der Change-Tracker über die
/// ganze Sitzung Entities an und der Kontext (nicht threadsicher) würde von parallelen Renderpfaden geteilt.
/// Pro Operation ein Scope löst alle drei Punkte.
/// </para>
/// <para>
/// Das aktive Profil des Circuits wird dabei per <see cref="ActiveConnectionProfile.Adopt"/> in den
/// Kind-Scope durchgereicht; der Store-Default allein genügt nicht, weil mehrere Circuits unterschiedliche
/// Profile aktiv haben können.
/// </para>
/// <para>
/// Das Fehler-Mapping spiegelt bewusst den <c>FlirtyExceptionEndpointFilter</c> aus
/// <c>Flirty.AspNetCore</c> (gleiche Reihenfolge der <c>catch</c>-Zweige): Not-Found vor Validierung vor
/// dem generischen Konflikt-Zweig. Ergänzt um Datenbankfehler, die im Designer typischerweise eine noch
/// nicht migrierte Datenbank bedeuten. Alles Übrige blubbert absichtlich in die Blazor-Fehler-UI.
/// </para>
/// </remarks>
internal sealed class FlirtyAdminGateway
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActiveConnectionProfile _active;

    /// <summary>Erstellt das Gateway.</summary>
    /// <param name="scopeFactory">Factory für den je Operation erzeugten Kind-Scope.</param>
    /// <param name="active">Das aktive Connection-Profil des aufrufenden Circuits.</param>
    public FlirtyAdminGateway(IServiceScopeFactory scopeFactory, ActiveConnectionProfile active)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(active);

        _scopeFactory = scopeFactory;
        _active = active;
    }

    /// <summary>
    /// Führt die angegebene Operation über einen frischen <see cref="ISender"/> aus und bildet die von
    /// der Engine geworfenen Ausnahmen auf eine anzeigbare Meldung ab.
    /// </summary>
    /// <typeparam name="TValue">Der Ergebnistyp der Operation.</typeparam>
    /// <param name="operation">
    /// Die auszuführende Operation, z. B. <c>(sender, token) =&gt; sender.Send(new ListDialogsQuery(), token)</c>.
    /// Bewusst als Delegat (statt <c>IRequest&lt;T&gt;</c>-Parameter), damit die stark typisierten
    /// <see cref="ISender"/>-Overloads gebunden werden – wie bei den ASP.NET-Endpunkten.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen der Operation.</param>
    /// <returns>Das Ergebnis der Operation oder eine deutsche Fehlermeldung.</returns>
    public async Task<AdminResult<TValue>> ExecuteAsync<TValue>(
        Func<ISender, CancellationToken, ValueTask<TValue>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = _scopeFactory.CreateAsyncScope();

        // Profil des Circuits in den Kind-Scope übernehmen. Ist keins aktiv, wirft die
        // FlirtyDesignerDbContextFactory unten ihre bereits formulierte Meldung -> nicht duplizieren.
        if (_active.Current is { } profile)
        {
            scope.ServiceProvider.GetRequiredService<ActiveConnectionProfile>().Adopt(profile);
        }

        try
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            return AdminResult<TValue>.Ok(await operation(sender, cancellationToken));
        }
        catch (ConfigurationNotFoundException exception)
        {
            return AdminResult<TValue>.Failed(exception.Message);
        }
        catch (ValidationException exception)
        {
            return AdminResult<TValue>.Failed(exception.Message);
        }
        catch (DbUpdateException exception)
        {
            return AdminResult<TValue>.Failed(DescribeDatabaseError(exception));
        }
        catch (DbException exception)
        {
            return AdminResult<TValue>.Failed(DescribeDatabaseError(exception));
        }
        catch (InvalidOperationException exception)
        {
            // Schlüsselkonflikt, Publish ohne Einstiegsfrage – oder kein aktives Connection-Profil.
            return AdminResult<TValue>.Failed(exception.Message);
        }
    }

    /// <summary>
    /// Formuliert einen Datenbankfehler mit dem im Designer häufigsten Grund: die Datenbank des aktiven
    /// Profils ist noch nicht migriert (frische SQLite-Datei &#8594; „no such table“).
    /// </summary>
    /// <param name="exception">Die aufgetretene Datenbank-Ausnahme.</param>
    /// <returns>Die anzuzeigende Meldung.</returns>
    private static string DescribeDatabaseError(Exception exception)
        => $"Datenbankfehler: {(exception.InnerException ?? exception).Message} "
            + "Ist die Datenbank des aktiven Profils migriert? (Verbindungen → „Migrieren“)";
}
