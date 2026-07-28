using System.Net;
using System.Net.Sockets;
using Flirty.Designer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Flirty.E2E.Spike;

/// <summary>
/// SPIKE #100 (Wegwerf, wird NICHT gemergt): hostet den Designer in-Prozess und schiebt einen
/// <see cref="LatencyProxy"/> davor, damit die Canvas-Prototypen über einen künstlich gedrosselten
/// Circuit gemessen werden können.
/// </summary>
/// <remarks>
/// Bewusst eine <b>eigene</b> Fixture statt einer Erweiterung von <c>DesignerAppFixture</c>: Die
/// bestehende Suite (#46) soll von Wegwerf-Code nicht berührt werden. Die Spike-Seiten brauchen kein
/// Connection-Profil (ihr Graph ist synthetisch), deshalb entfällt hier auch das Seeden der Datenbank.
/// </remarks>
public sealed class CanvasSpikeFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private LatencyProxy? _proxy;
    private string? _contentRoot;

    /// <summary>Die Basis-URL, die der Browser benutzt – sie zeigt auf den Proxy, nicht auf Kestrel.</summary>
    public string BaseUrl => _proxy?.BaseUrl ?? string.Empty;

    /// <summary>Die Einweg-Verzögerung je Richtung; die Umlaufzeit ist das Doppelte.</summary>
    public int DelayMilliseconds
    {
        get => _proxy?.DelayMilliseconds ?? 0;
        set
        {
            if (_proxy is not null)
            {
                _proxy.DelayMilliseconds = value;
            }
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var kestrelPort = GetFreeTcpPort();

        // Eigenes ContentRoot je Lauf – gleiche Begründung wie in DesignerAppFixture.
        _contentRoot = Path.Combine(Path.GetTempPath(), $"flirty-spike-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Beide Angaben sind funktional zwingend (siehe DesignerAppFixture): ohne sie wird
            // _framework/blazor.web.js nicht ausgeliefert und der Circuit kommt nie zustande.
            ApplicationName = "Flirty.Designer",
            EnvironmentName = "Development",
            ContentRootPath = _contentRoot,
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{kestrelPort}");

        DesignerApp.ConfigureServices(builder);

        _app = builder.Build();
        DesignerApp.Configure(_app);

        await _app.StartAsync();

        _proxy = LatencyProxy.Start(kestrelPort);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // Reihenfolge: erst der Proxy (der hält die Browser-Verbindungen), dann der Host.
        if (_proxy is not null)
        {
            await _proxy.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_contentRoot is not null && Directory.Exists(_contentRoot))
        {
            try { Directory.Delete(_contentRoot, recursive: true); }
            catch (IOException) { /* egal – Temp */ }
            catch (UnauthorizedAccessException) { /* dito */ }
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
