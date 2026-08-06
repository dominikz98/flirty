using System.Globalization;
using Flirty.Designer.Components;
using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;
using Flirty.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer;

/// <summary>
/// Central, reusable composition of the designer. <see cref="ConfigureServices"/> wires up the engine,
/// connection profiles and gateways, <see cref="Configure"/> builds the HTTP pipeline including the Blazor
/// components. Both are used by <c>Program.cs</c> (real Kestrel) and by the Playwright E2E
/// (<c>DesignerAppFixture</c>, #46), so app and test share the same setup.
/// </summary>
public static class DesignerApp
{
    /// <summary>File name of the local profile store (relative to the ContentRoot).</summary>
    public const string ConnectionProfilesFileName = "connection-profiles.json";

    /// <summary>
    /// File name of the local question-type descriptor file (relative to the ContentRoot), #137.
    /// </summary>
    /// <remarks>
    /// Optional. Its absence is the normal case and leaves the designer behaving exactly as it did after
    /// #136: a host-declared type then shows its raw key instead of a display name.
    /// </remarks>
    public const string QuestionTypesFileName = "question-types.json";

    /// <summary>
    /// Culture in which the designer formats numbers, dates and times. Fixed on purpose: without it the
    /// formatting follows the host's culture, and the display would then vary from machine to machine.
    /// Affects only the <b>display</b> – the answer values are encoded invariantly by
    /// <c>AnswerValueCodec</c> regardless of this.
    /// </summary>
    public const string DisplayCulture = "en-US";

    /// <summary>
    /// Wires up all services of the designer: Blazor (server-interactive), the Flirty engine without a
    /// hard-wired provider, the connection-profile management including the context factory, and the two
    /// gateways plus the trigger log of the test runner.
    /// </summary>
    /// <param name="builder">The host builder of the web app.</param>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Set the display culture of the whole tool (see DisplayCulture). Via the process default culture
        // instead of RequestLocalization: in Blazor Server the rendering runs in the circuit, not in an
        // HTTP request – a request middleware would not reach it at all.
        var culture = CultureInfo.GetCultureInfo(DisplayCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Flirty engine WITHOUT a hard-wired provider: the FlirtyDbContext is instead created per active
        // connection profile via the designer factory (multi-DB, issue #37). The options overload is used
        // only to declare the question-type descriptors (#137) - without a Use* call it registers no
        // DbContext, so the providerless setup is unchanged.
        //
        // Reading the descriptors here rather than in a service is what makes the CORE registry the
        // designer's single source: o.AddQuestionType(...) is evaluated once at startup, exactly as in a
        // host (ADR 0012). The declarations carry no validator - that is code and stays in the host - so
        // the semantic delta stays open and the test runner states it.
        var questionTypesPath = Path.Combine(builder.Environment.ContentRootPath, QuestionTypesFileName);
        var (descriptors, problems) = QuestionTypeDescriptorFile.Read(questionTypesPath);
        var declarationProblems = new List<string>(problems);

        builder.Services.AddFlirty(options =>
            declarationProblems.AddRange(DesignerQuestionTypes.Declare(options, descriptors)));

        builder.Services.AddSingleton(new DesignerQuestionTypeSource(
            questionTypesPath, File.Exists(questionTypesPath), declarationProblems));

        // Connection-profile management: store (persisted as JSON in the ContentRoot), active profile (per
        // circuit), factory (IDbContextFactory against the active profile) and the test/migrate operations.
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

        // Admin CRUD (#38): runs the mediator commands/queries per operation in a fresh DI scope, so the
        // FlirtyDbContext does not live across the whole circuit and profile switches take effect at once.
        builder.Services.AddScoped<FlirtyAdminGateway>();

        // Test runner (#43): the same scope pattern for the runtime operations (IFlirtyEngine) plus the
        // trigger log of the run. The four handlers write into it what the engine publishes; the gateway
        // passes the circuit's log through into the respective child scope.
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
