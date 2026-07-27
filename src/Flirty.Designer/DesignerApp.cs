using System.Globalization;
using Flirty.Designer.Components;
using Flirty.Designer.Services;
using Flirty.Persistence;
using Flirty.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer;

/// <summary>
/// Zentrale, wiederverwendbare Komposition des Designers. <see cref="ConfigureServices"/> verdrahtet
/// Engine, Connection-Profile und Gateways, <see cref="Configure"/> baut die HTTP-Pipeline samt
/// Blazor-Komponenten auf. Beide werden von <c>Program.cs</c> (echtes Kestrel) und von der
/// Playwright-E2E (<c>DesignerAppFixture</c>, #46) genutzt, damit App und Test denselben Aufbau teilen.
/// </summary>
public static class DesignerApp
{
    /// <summary>Dateiname der lokalen Profil-Ablage (relativ zum ContentRoot).</summary>
    public const string ConnectionProfilesFileName = "connection-profiles.json";

    /// <summary>
    /// Kultur, in der der Designer Zahlen, Datum und Uhrzeit formatiert. Fest gesetzt, weil die
    /// Oberfläche durchgängig deutsch ist: Ohne diese Festlegung folgt die Formatierung der Kultur des
    /// Hosts, und auf einem englischen System stünde „7/27/2026 10:38 AM" mitten im deutschen Text.
    /// Betrifft nur die <b>Anzeige</b> – die Antwortwerte kodiert <c>AnswerValueCodec</c> unabhängig davon
    /// invariant.
    /// </summary>
    public const string DisplayCulture = "de-DE";

    /// <summary>
    /// Verdrahtet alle Dienste des Designers: Blazor (server-interaktiv), die Flirty-Engine ohne fest
    /// verdrahteten Provider, die Connection-Profil-Verwaltung samt Kontext-Factory sowie die beiden
    /// Gateways und das Trigger-Protokoll des Test-Runners.
    /// </summary>
    /// <param name="builder">Der Host-Builder der Web-App.</param>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Anzeige-Kultur des gesamten Werkzeugs festlegen (siehe DisplayCulture). Über die Default-Kultur
        // des Prozesses statt über RequestLocalization: In Blazor Server läuft das Rendern im Circuit,
        // nicht in einem HTTP-Request – eine Request-Middleware erreicht es gar nicht.
        var culture = CultureInfo.GetCultureInfo(DisplayCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Flirty-Engine OHNE fest verdrahteten Provider (parameterloses AddFlirty): der FlirtyDbContext wird
        // stattdessen pro aktivem Connection-Profil über die Designer-Factory erzeugt (Multi-DB, Issue #37).
        builder.Services.AddFlirty();

        // Connection-Profil-Verwaltung: Store (persistiert als JSON im ContentRoot), aktives Profil (pro Circuit),
        // Factory (IDbContextFactory gegen das aktive Profil) und die Test-/Migrate-Operationen.
        builder.Services.AddSingleton<IConnectionProfileStore>(sp =>
        {
            var environment = sp.GetRequiredService<IWebHostEnvironment>();
            var filePath = Path.Combine(environment.ContentRootPath, ConnectionProfilesFileName);
            return new JsonConnectionProfileStore(filePath);
        });
        builder.Services.AddSingleton<ConnectionProfileOperations>();
        builder.Services.AddScoped<ActiveConnectionProfile>();
        builder.Services.AddScoped<IDbContextFactory<FlirtyDbContext>, FlirtyDesignerDbContextFactory>();
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FlirtyDbContext>>().CreateDbContext());

        // Admin-CRUD (#38): führt die Mediator-Commands/Queries je Operation in einem frischen DI-Scope aus,
        // damit der FlirtyDbContext nicht über den ganzen Circuit lebt und Profilwechsel sofort greifen.
        builder.Services.AddScoped<FlirtyAdminGateway>();

        // Test-Runner (#43): dasselbe Scope-Muster für die Laufzeit-Operationen (IFlirtyEngine) plus das
        // Trigger-Protokoll des Laufs. Die vier Handler schreiben hinein, was die Engine publiziert; das
        // Gateway reicht den Log des Circuits in den jeweiligen Kind-Scope durch.
        builder.Services.AddScoped<DesignerTriggerLog>();
        builder.Services.AddScoped<FlirtyRuntimeGateway>();
        builder.Services
            .AddFlirtyHandler<DialogStartedNotification, DesignerTriggerLogHandlers.DialogStarted>()
            .AddFlirtyHandler<AnswerSubmittedNotification, DesignerTriggerLogHandlers.AnswerSubmitted>()
            .AddFlirtyHandler<QuestionAnsweredNotification, DesignerTriggerLogHandlers.QuestionAnswered>()
            .AddFlirtyHandler<DialogCompletedNotification, DesignerTriggerLogHandlers.DialogCompleted>();
    }

    /// <summary>
    /// Baut die HTTP-Pipeline auf und registriert die Blazor-Komponenten (server-interaktiv).
    /// </summary>
    /// <param name="app">Die gebaute Web-App.</param>
    public static void Configure(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
