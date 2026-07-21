using Microsoft.Playwright;

namespace Flirty.E2E;

/// <summary>
/// Playwright-E2E der Web-Sample-Chat-UI (#45/#47) gegen ein echtes, in-Prozess gehostetes Kestrel
/// (<see cref="WebSampleAppFixture"/>). Deckt einen vollständigen Durchlauf (Branching + Loop über Liste +
/// Abschluss inkl. In-Process-Trigger und vollem Outbound→Inbound-Webhook-Rundlauf), Reload→Resume sowie
/// das Editieren einer Antwort ab. Sind keine Playwright-Browser installiert, überspringen sich die Tests
/// (<see cref="SkippableFactAttribute"/>) – Installation z. B. via
/// <c>pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium</c>.
/// </summary>
public sealed class WebSampleE2ETests : IClassFixture<WebSampleAppFixture>
{
    private static readonly LocatorAssertionsToContainTextOptions SlowContains = new() { Timeout = 15_000 };

    private readonly WebSampleAppFixture _fixture;

    /// <summary>Initialisiert den Test mit dem gemeinsam genutzten App-Host.</summary>
    /// <param name="fixture">Der in-Prozess gehostete Sample-App-Host.</param>
    public WebSampleE2ETests(WebSampleAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Durchlauf_Branching_Loop_und_Trigger_Rundlauf()
    {
        await using var session = await LaunchBrowserAsync();
        var page = await NewPageAsync(session.Browser);
        await page.GotoAsync(_fixture.BaseUrl);

        // Branching: dev-Zweig -> Freitext language.
        await page.GetByRole(AriaRole.Button, new() { Name = "Entwickler" }).ClickAsync();
        await FillAndSendAsync(page, "C#");

        // Loop über Liste: zwei skill-Iterationen (Ja = Loop-Back), dann Nein = Exit.
        await FillAndSendAsync(page, "EF Core");
        await page.GetByRole(AriaRole.Button, new() { Name = "Ja" }).ClickAsync();
        await FillAndSendAsync(page, "Blazor");
        await page.GetByRole(AriaRole.Button, new() { Name = "Nein" }).ClickAsync();

        // Abschlussfrage (Boolean) -> Dialog abgeschlossen.
        await page.GetByRole(AriaRole.Button, new() { Name = "Ja" }).ClickAsync();

        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("abgeschlossen", SlowContains);
        await Assertions.Expect(page.Locator("#skillsList li")).ToHaveCountAsync(2);

        // In-Process-Handler und Outbound→Inbound-Webhook-Rundlauf werden im Panel sichtbar (Polling).
        await Assertions.Expect(page.Locator("#triggersList")).ToContainTextAsync("web-onboarding", SlowContains);
        await Assertions.Expect(page.Locator("#webhooksList")).ToContainTextAsync("OnDialogCompleted", SlowContains);
    }

    [SkippableFact]
    public async Task Reload_stellt_die_Session_wieder_her()
    {
        await using var session = await LaunchBrowserAsync();
        var page = await NewPageAsync(session.Browser);
        await page.GotoAsync(_fixture.BaseUrl);

        await page.GetByRole(AriaRole.Button, new() { Name = "Entwickler" }).ClickAsync();
        await FillAndSendAsync(page, "C#");
        await Assertions.Expect(page.Locator(".msg--user").First).ToContainTextAsync("Entwickler", SlowContains);

        // Reload -> die App stellt den Verlauf über GET /flirty/sessions/{id} wieder her (Resume).
        await page.ReloadAsync();

        await Assertions.Expect(page.Locator(".msg--user").First).ToContainTextAsync("Entwickler", SlowContains);
        await Assertions.Expect(page.Locator("#statusLine")).ToContainTextAsync("Resume", SlowContains);
    }

    [SkippableFact]
    public async Task Editieren_einer_Antwort_verwirft_nachgelagerte_Antworten()
    {
        await using var session = await LaunchBrowserAsync();
        var page = await NewPageAsync(session.Browser);
        await page.GotoAsync(_fixture.BaseUrl);

        await page.GetByRole(AriaRole.Button, new() { Name = "Entwickler" }).ClickAsync();
        await FillAndSendAsync(page, "C#");
        await FillAndSendAsync(page, "EF Core");

        // Die language-Antwort editieren -> nachgelagerte Antworten (skill) werden verworfen.
        await page.Locator(".msg--user:has-text('language') .msg__edit").ClickAsync();
        await page.Locator(".chat__input input.field").FillAsync("Rust");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();

        await Assertions.Expect(page.Locator("#statusLine")).ToContainTextAsync("editiert", SlowContains);
        await Assertions.Expect(page.Locator(".msg--user:has-text('language')")).ToContainTextAsync("Rust", SlowContains);
    }

    private static async Task FillAndSendAsync(IPage page, string text)
    {
        await page.Locator(".chat__input input.field").FillAsync(text);
        await page.GetByRole(AriaRole.Button, new() { Name = "Senden" }).ClickAsync();
    }

    private static async Task<IPage> NewPageAsync(IBrowser browser)
    {
        // Frischer Context pro Test = leeres localStorage -> neuer externalUserKey -> saubere Session.
        var context = await browser.NewContextAsync();
        return await context.NewPageAsync();
    }

    private static async Task<BrowserSession> LaunchBrowserAsync()
    {
        IPlaywright playwright;
        try
        {
            playwright = await Playwright.CreateAsync();
        }
        catch (PlaywrightException ex)
        {
            Skip.If(true, "Playwright-Treiber nicht verfügbar: " + ex.Message);
            throw;
        }

        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return new BrowserSession(playwright, browser);
        }
        catch (PlaywrightException ex)
        {
            playwright.Dispose();
            Skip.If(true,
                "Playwright-Browser nicht installiert. Installation via " +
                "'pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium'. Detail: " + ex.Message);
            throw;
        }
    }

    private sealed class BrowserSession(IPlaywright playwright, IBrowser browser) : IAsyncDisposable
    {
        public IBrowser Browser { get; } = browser;

        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            playwright.Dispose();
        }
    }
}
