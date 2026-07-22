using Microsoft.Playwright;

namespace Flirty.E2E;

/// <summary>
/// Ein gestarteter Playwright-Treiber samt Browser. Gemeinsame Grundlage beider E2E-Suiten
/// (Chat-UI der Web-Sample, #45/#47, und Designer, #46): Fehlt der Treiber oder der Browser,
/// <b>überspringt</b> sich der Test statt zu scheitern.
/// </summary>
public sealed class PlaywrightSession : IAsyncDisposable
{
    private readonly IPlaywright _playwright;

    private PlaywrightSession(IPlaywright playwright, IBrowser browser)
    {
        _playwright = playwright;
        Browser = browser;
    }

    /// <summary>Der gestartete (headless) Chromium-Browser.</summary>
    public IBrowser Browser { get; }

    /// <summary>
    /// Startet Treiber und Browser. Ist eines von beidem nicht verfügbar, wird der aufrufende Test
    /// übersprungen (Installation z. B. via
    /// <c>pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium</c>).
    /// </summary>
    /// <returns>Die Sitzung, die der Test entsorgen muss.</returns>
    public static async Task<PlaywrightSession> LaunchAsync()
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
            return new PlaywrightSession(playwright, browser);
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

    /// <summary>
    /// Öffnet eine Seite in einem frischen Browser-Context (leeres localStorage, eigene Cookies) –
    /// so startet jeder Test mit einem sauberen Zustand.
    /// </summary>
    /// <returns>Die neue Seite.</returns>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync();
        return await context.NewPageAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        _playwright.Dispose();
    }
}
