using Microsoft.Playwright;

namespace Flirty.E2E;

/// <summary>
/// A started Playwright driver including its browser. The shared foundation of both E2E suites (the web
/// sample's chat UI, #45/#47, and the designer, #46): if the driver or the browser is missing, the test
/// <b>skips</b> itself instead of failing.
/// </summary>
public sealed class PlaywrightSession : IAsyncDisposable
{
    private readonly IPlaywright _playwright;

    private PlaywrightSession(IPlaywright playwright, IBrowser browser)
    {
        _playwright = playwright;
        Browser = browser;
    }

    /// <summary>The started (headless) Chromium browser.</summary>
    public IBrowser Browser { get; }

    /// <summary>
    /// Starts the driver and browser. If either is unavailable, the calling test is skipped (installation
    /// e.g. via <c>pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium</c>).
    /// </summary>
    /// <returns>The session that the test must dispose.</returns>
    public static async Task<PlaywrightSession> LaunchAsync()
    {
        IPlaywright playwright;
        try
        {
            playwright = await Playwright.CreateAsync();
        }
        catch (PlaywrightException ex)
        {
            Skip.If(true, "Playwright driver not available: " + ex.Message);
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
    /// Opens a page in a fresh browser context (empty localStorage, its own cookies) – so every test
    /// starts with a clean state.
    /// </summary>
    /// <returns>The new page.</returns>
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
