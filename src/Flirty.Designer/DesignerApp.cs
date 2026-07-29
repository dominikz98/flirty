using System.Globalization;
using Flirty.Designer.Components;
using Flirty.Designer.Services;
using Flirty.Persistence;
using Flirty.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer;

/// <summary>
/// Central, reusable composition of the designer. <see cref="ConfigureServices"/> wires up the
/// engine, connection profiles and gateways, <see cref="Configure"/> builds the HTTP pipeline together with
/// the Blazor components. Both are used by <c>Program.cs</c> (real Kestrel) and by the
/// Playwright E2E (<c>DesignerAppFixture</c>, #46), so that app and test share the same setup.
/// </summary>
public static class DesignerApp
{
    /// <summary>File name of the local profile storage (relative to the ContentRoot).</summary>
    public const string ConnectionProfilesFileName = "connection-profiles.json";

    /// <summary>
    /// Culture in which the designer formats numbers, date and time. Fixed, because the
    /// UI is entirely German: without this setting the formatting follows the culture of the
    /// host, and on an English system "7/27/2026 10:38 AM" would stand in the middle of German text.
    /// Affects only the <b>display</b> – the answer values are encoded invariantly by <c>AnswerValueCodec</c>
    /// independently of this.
    /// </summary>
    public const string DisplayCulture = "de-DE";

    /// <summary>
    /// Wires up all services of the designer: Blazor (server-interactive), the Flirty engine without a
    /// hard-wired provider, the connection-profile management together with the context factory as well as the
    /// two gateways and the trigger log of the test runner.
    /// </summary>
    /// <param name="builder">The host builder of the web app.</param>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Set the display culture of the whole tool (see DisplayCulture). Via the process's default culture
        // rather than via RequestLocalization: in Blazor Server the rendering runs in the circuit,
        // not in an HTTP request – a request middleware does not reach it at all.
        var culture = CultureInfo.GetCultureInfo(DisplayCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Flirty engine WITHOUT a hard-wired provider (parameterless AddFlirty): the FlirtyDbContext is
        // instead created per active connection profile via the designer factory (multi-DB, issue #37).
        builder.Services.AddFlirty();

        // Connection-profile management: store (persisted as JSON in the ContentRoot), active profile (per circuit),
        // factory (IDbContextFactory against the active profile) and the test/migrate operations.
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

        // Admin CRUD (#38): runs the Mediator commands/queries per operation in a fresh DI scope,
        // so that the FlirtyDbContext does not live across the whole circuit and profile switches take effect immediately.
        builder.Services.AddScoped<FlirtyAdminGateway>();

        // Test runner (#43): the same scope pattern for the runtime operations (IFlirtyEngine) plus the
        // trigger log of the run. The four handlers write into it what the engine publishes; the
        // gateway passes the log of the circuit through into the respective child scope.
        builder.Services.AddScoped<DesignerTriggerLog>();
        builder.Services.AddScoped<FlirtyRuntimeGateway>();
        builder.Services
            .AddFlirtyHandler<DialogStartedNotification, DesignerTriggerLogHandlers.DialogStarted>()
            .AddFlirtyHandler<AnswerSubmittedNotification, DesignerTriggerLogHandlers.AnswerSubmitted>()
            .AddFlirtyHandler<QuestionAnsweredNotification, DesignerTriggerLogHandlers.QuestionAnswered>()
            .AddFlirtyHandler<DialogCompletedNotification, DesignerTriggerLogHandlers.DialogCompleted>();
    }

    /// <summary>
    /// Builds the HTTP pipeline and registers the Blazor components (server-interactive).
    /// </summary>
    /// <param name="app">The built web app.</param>
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
