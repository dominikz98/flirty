using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Flirty.E2E.Spike;

/// <summary>
/// SPIKE #100 (Wegwerf, wird NICHT gemergt): ein TCP-Proxy, der jedem Byte in <b>beide</b> Richtungen
/// eine konstante Einweg-Verzögerung aufprägt. Damit lässt sich der Blazor-Circuit auf eine realistische
/// WAN-Laufzeit drosseln, ohne den Designer anzufassen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Warum nicht CDP?</b> <c>Network.emulateNetworkConditions</c> scheidet aus: Chromium leitet die
/// Latenz in <c>ThrottlingNetworkInterceptor::StartThrottle</c> über <c>if (start &amp;&amp; …)</c> ab,
/// und für WebSocket-Frames ist <c>start</c> hart <c>false</c>. Auf WS-Frames wirkt dort also
/// ausschließlich der Durchsatz, nie die Latenz – und Durchsatz als Latenzersatz scheitert an der
/// Paketgranularität von 1500 Byte.
/// </para>
/// <para>
/// <b>Warum Reader und Writer entkoppelt sind.</b> Eine naive <c>Read → Delay → Write</c>-Schleife wäre
/// kein Latenzmodell, sondern ein Rate-Limiter von einem Chunk je Verzögerung: Der nächste
/// <c>ReadAsync</c> startet erst nach dem Write des vorigen Chunks, die Verzögerung akkumuliert also
/// über die Geste. Deshalb schreibt der Reader jeden Chunk mit einem eigenen Fälligkeitszeitpunkt in
/// einen Kanal und blockiert nie; ein einzelner Writer je Richtung hält die Reihenfolge.
/// </para>
/// <para>
/// Die tatsächlich erreichte Umlaufzeit wird nicht angenommen, sondern gemessen (RTT-Sonde im Messlauf) –
/// <c>Task.Delay</c> hat auf Windows eine grobe Auflösung, die letzten Millisekunden werden deshalb
/// aktiv abgewartet.
/// </para>
/// </remarks>
internal sealed class LatencyProxy : IAsyncDisposable
{
    private const int BufferSize = 64 * 1024;

    private readonly TcpListener _listener;
    private readonly int _upstreamPort;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _connections = [];
    private readonly Lock _connectionsGate = new();

    private Task? _acceptLoop;
    private volatile int _delayMilliseconds;

    private LatencyProxy(TcpListener listener, int port, int upstreamPort)
    {
        _listener = listener;
        _upstreamPort = upstreamPort;
        Port = port;
    }

    /// <summary>Der Port, auf dem der Proxy lauscht (der Browser verbindet sich hierher).</summary>
    public int Port { get; }

    /// <summary>Die Basis-URL des Proxys – Ersatz für die Kestrel-URL im Test.</summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>
    /// Die Einweg-Verzögerung je Richtung in Millisekunden; die resultierende Umlaufzeit ist das
    /// Doppelte. Während Navigation und Boot bewusst auf <c>0</c> lassen und erst unmittelbar vor der
    /// Messung hochsetzen – sonst dauert allein der Seitenaufbau Minuten.
    /// </summary>
    public int DelayMilliseconds
    {
        get => _delayMilliseconds;
        set => _delayMilliseconds = value;
    }

    /// <summary>Startet den Proxy vor einem bereits laufenden Kestrel.</summary>
    /// <param name="upstreamPort">Der Port des echten Hosts.</param>
    /// <returns>Der laufende Proxy.</returns>
    public static LatencyProxy Start(int upstreamPort)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var proxy = new LatencyProxy(listener, port, upstreamPort);
        proxy._acceptLoop = Task.Run(() => proxy.AcceptLoopAsync(proxy._cts.Token));
        return proxy;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var connection = Task.Run(() => HandleAsync(client, cancellationToken), CancellationToken.None);
            lock (_connectionsGate)
            {
                _connections.RemoveAll(t => t.IsCompleted);
                _connections.Add(connection);
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var downstream = client;
        using var upstream = new TcpClient();

        try
        {
            // Ohne Nagle-Abschaltung legen sich bis zu 200 ms Delayed-ACK über die injizierte Latenz –
            // die gemessene Zahl wäre dann frei erfunden. Der wichtigste Einzeiler des ganzen Proxys.
            downstream.NoDelay = true;
            await upstream.ConnectAsync(IPAddress.Loopback, _upstreamPort, cancellationToken);
            upstream.NoDelay = true;

            var toUpstream = PumpAsync(downstream, upstream, cancellationToken);
            var toDownstream = PumpAsync(upstream, downstream, cancellationToken);
            await Task.WhenAll(toUpstream, toDownstream);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren – erwartet.
        }
        catch (SocketException)
        {
            // Gegenstelle weg (Reload, Circuit-Ende) – für einen Messproxy belanglos.
        }
        catch (IOException)
        {
            // dito
        }
    }

    /// <summary>
    /// Pumpt eine Richtung: Reader liest ohne zu blockieren und stempelt jeden Chunk mit seiner
    /// Fälligkeit, ein einzelner Writer gibt ihn frühestens dann weiter.
    /// </summary>
    private async Task PumpAsync(TcpClient from, TcpClient to, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<(byte[] Chunk, long DueAt)>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var reader = Task.Run(async () =>
        {
            var buffer = new byte[BufferSize];
            try
            {
                while (true)
                {
                    var read = await from.GetStream().ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    var due = Stopwatch.GetTimestamp()
                        + (long)(_delayMilliseconds / 1000.0 * Stopwatch.Frequency);
                    channel.Writer.TryWrite((buffer[..read], due));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException
                                          or ObjectDisposedException)
            {
                // Verbindungsende – der Writer räumt gleich auf.
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        var writer = Task.Run(async () =>
        {
            try
            {
                await foreach (var (chunk, dueAt) in channel.Reader.ReadAllAsync(CancellationToken.None))
                {
                    await WaitUntilAsync(dueAt, cancellationToken);
                    await to.GetStream().WriteAsync(chunk, cancellationToken);
                    await to.GetStream().FlushAsync(cancellationToken);
                }

                // Halbschluss statt hartem Dispose: sonst bricht der WebSocket-Close-Handshake und
                // Kestrel protokolliert Fehler, die nur der Proxy verursacht hat.
                to.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException
                                          or ObjectDisposedException)
            {
                // dito
            }
        }, CancellationToken.None);

        await Task.WhenAll(reader, writer);
    }

    /// <summary>
    /// Wartet bis zum Fälligkeitszeitpunkt. <c>Task.Delay</c> ist auf Windows grob (~15 ms), deshalb
    /// wird der Rest unter 2 ms aktiv abgewartet – das hält den Jitter klein, ohne nennenswert CPU zu
    /// verbrennen (wenige Millisekunden je Chunk).
    /// </summary>
    private static async Task WaitUntilAsync(long dueAt, CancellationToken cancellationToken)
    {
        var remainingMs = (dueAt - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
        if (remainingMs <= 0)
        {
            return;
        }

        if (remainingMs > 2)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(remainingMs - 2), cancellationToken);
        }

        var spinner = new SpinWait();
        while (Stopwatch.GetTimestamp() < dueAt)
        {
            spinner.SpinOnce();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();

        Task[] pending;
        lock (_connectionsGate)
        {
            pending = [.. _connections];
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // Beim Herunterfahren interessieren offene Verbindungen nicht mehr.
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // dito
            }
        }

        _cts.Dispose();
    }
}
