using System.Globalization;
using System.Text;
using Flirty.Designer.Components.Pages.Spike;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Flirty.E2E.Spike;

/// <summary>Ein Kandidat der Messlatte.</summary>
internal sealed record Candidate(string Name, string Route);

/// <summary>Das Ergebnis einer einzelnen gemessenen Geste.</summary>
internal sealed record GestureResult(DragMeasurement Drag, GestureTraffic Traffic, double RoundTripMs);

/// <summary>
/// SPIKE #100 (Wegwerf, wird NICHT gemergt): der Messlauf hinter dem Akzeptanzkriterium „die Zahlen
/// sind dokumentiert – kein Bauchgefühl".
/// </summary>
/// <remarks>
/// <para>
/// <b>Läuft nicht in der CI.</b> Ein 2-Kern-Runner liefert für Latenzmessungen wertlose, schwankende
/// Zahlen; der Test verlangt deshalb ausdrücklich <c>FLIRTY_SPIKE=1</c>.
/// </para>
/// <para>
/// Verfahren: gleicher Graph in beiden Prototypen (hart geprüft), Drosselung erst nach dem Seitenaufbau,
/// eine ungemessene Aufwärm-Geste je Seite, dann eine gemessene; frischer Browser-Context – und damit
/// frischer Circuit – je Geste; die Kandidaten laufen abwechselnd, damit Drift beide gleich trifft;
/// ausgewertet wird der <b>Median</b>, nie das Mittel.
/// </para>
/// </remarks>
public sealed class CanvasSpikeMeasurementTests : IClassFixture<CanvasSpikeFixture>
{
    private const int MeasuredGestures = 7;
    private const int OneWayDelayMs = 75;
    private const double GestureDistance = SpikeInteraction.DragDistance;

    private static readonly Candidate CanvasA = new("A – Eigenbau-SVG", "/spike/canvas-a");
    private static readonly Candidate CanvasB = new("B – Blazor.Diagrams", "/spike/canvas-b");

    private static readonly string NodeSelector =
        $"[data-node-id='{SpikeGraph.Nodes[SpikeGraph.DragTargetIndex].Key}']";

    private readonly CanvasSpikeFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Initialisiert den Messlauf mit dem gedrosselten Designer-Host.</summary>
    public CanvasSpikeMeasurementTests(CanvasSpikeFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>Die Messlatte aus #100, für beide Kandidaten identisch gefahren.</summary>
    [SkippableFact]
    public async Task Messlatte_vergleicht_Eigenbau_und_Blazor_Diagrams()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable("FLIRTY_SPIKE") == "1",
            "Messlauf des Canvas-Spikes – nur mit FLIRTY_SPIKE=1 (nie in der CI: dort sind die Zahlen wertlos).");

        await using var session = await PlaywrightSession.LaunchAsync();

        var results = new Dictionary<string, List<GestureResult>>
        {
            [CanvasA.Name] = [],
            [CanvasB.Name] = [],
        };

        for (var round = 0; round < MeasuredGestures; round++)
        {
            // Startreihenfolge wechseln: sonst trifft ein Aufwärm- oder Drift-Effekt immer denselben.
            var order = round % 2 == 0 ? new[] { CanvasA, CanvasB } : [CanvasB, CanvasA];
            foreach (var candidate in order)
            {
                var result = await MeasureGestureAsync(session, candidate);
                results[candidate.Name].Add(result);
                _output.WriteLine(
                    $"[{round + 1}/{MeasuredGestures}] {candidate.Name}: "
                    + $"Rückstand {result.Drag.LagPixels:F1} px / {result.Drag.LagMilliseconds:F0} ms, "
                    + $"Settle {result.Drag.SettleMilliseconds:F0} ms, "
                    + $"Nachrichten {result.Traffic.Sent}↑/{result.Traffic.Received}↓, "
                    + $"{result.Traffic.Bytes} B, {result.Drag.DispatchedMoves} pointermove, "
                    + $"RTT {result.RoundTripMs:F0} ms");
            }
        }

        var report = BuildReport(results);
        _output.WriteLine(string.Empty);
        _output.WriteLine(report);

        var scratch = Environment.GetEnvironmentVariable("FLIRTY_SPIKE_OUT");
        if (!string.IsNullOrWhiteSpace(scratch))
        {
            await File.WriteAllTextAsync(scratch, report, Encoding.UTF8);
        }

        // Kein Ergebnis-Gate: der Test dokumentiert, er bewertet nicht. Nur die Messbarkeit wird
        // sichergestellt – eine Geste ohne Nachrichtenverkehr wäre eine kaputte Messung, keine Zahl.
        Assert.All(results.Values, list => Assert.Equal(MeasuredGestures, list.Count));
        Assert.All(results.Values, list => Assert.All(list, r => Assert.True(r.Traffic.Total > 0)));
    }

    private async Task<GestureResult> MeasureGestureAsync(PlaywrightSession session, Candidate candidate)
    {
        var page = await session.NewPageAsync();
        try
        {
            var recorder = new SignalRFrameRecorder();
            recorder.Attach(page);

            // Aufbauen und aufwärmen läuft ungedrosselt – sonst dauert allein der Boot Minuten.
            _fixture.DelayMilliseconds = 0;
            await page.GotoAsync($"{_fixture.BaseUrl}{candidate.Route}");
            await SpikeInteraction.WaitForCircuitAsync(page);

            // Gleicher Graph in beiden Prototypen – geprüft, nicht behauptet.
            await Assertions.Expect(page.Locator("[data-node-id]"))
                .ToHaveCountAsync(SpikeGraph.Nodes.Count, new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator($"{NodeSelector} rect"))
                .ToHaveCountAsync(1, new() { Timeout = 15_000 });

            Assert.True(recorder.WebSocketSeen,
                "Keine Blazor-WebSocket beobachtet – ohne WebSocket-Transport wäre jede Roundtrip-Zahl 0.");

            // Aufwärmen: JIT des Renderpfads, erster Render-Batch, Style-/Layout-Caches des Browsers.
            // Nach links, damit die gemessene Geste am selben Punkt beginnt wie ohne Aufwärmen.
            var warmupBox = await page.Locator(NodeSelector).BoundingBoxAsync();
            Assert.NotNull(warmupBox);
            await SpikeInteraction.DragAsync(page, warmupBox, -GestureDistance);
            await page.WaitForTimeoutAsync(500);

            // Ab hier gedrosselt.
            _fixture.DelayMilliseconds = OneWayDelayMs;

            var box = await page.Locator(NodeSelector).BoundingBoxAsync();
            Assert.NotNull(box);
            await DragProbe.InstallAsync(page, NodeSelector);
            recorder.Start();

            await SpikeInteraction.DragAsync(page, box);

            // Mindestens eine Umlaufzeit plus Puffer abwarten: Prototyp A schweigt während der Geste,
            // seine einzige Nachricht kommt erst nach dem Loslassen – verzögert um genau diese Zeit.
            var traffic = await recorder.StopAsync(TimeSpan.FromMilliseconds((2 * OneWayDelayMs) + 400));
            var drag = await DragProbe.CollectAsync(page);
            var roundTrip = await SpikeInteraction.MeasureCircuitRoundTripAsync(page);

            return new GestureResult(drag, traffic, roundTrip);
        }
        finally
        {
            _fixture.DelayMilliseconds = 0;
            await page.CloseAsync();
        }
    }

    private static string BuildReport(Dictionary<string, List<GestureResult>> results)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Messlatte #100 – {SpikeGraph.Nodes.Count} Knoten / {SpikeGraph.Edges.Count} Kanten, "
            + $"{MeasuredGestures} Gesten je Kandidat, Geste {GestureDistance:F0} px in "
            + $"{SpikeInteraction.DragSteps} Schritten à {SpikeInteraction.DragStepDelayMs} ms, "
            + $"Drosselung 2 × {OneWayDelayMs} ms.");
        report.AppendLine();
        report.AppendLine("| Kandidat | Rückstand px (Median) | Rückstand ms | Settle ms | Nachrichten ↑ | Nachrichten ↓ | Bytes | pointermove | RTT ms |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var (name, list) in results)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} "
                + $"| {Stat(list, r => r.Drag.LagPixels)} "
                + $"| {Stat(list, r => r.Drag.LagMilliseconds)} "
                + $"| {Stat(list, r => r.Drag.SettleMilliseconds)} "
                + $"| {Stat(list, r => r.Traffic.Sent)} "
                + $"| {Stat(list, r => r.Traffic.Received)} "
                + $"| {Stat(list, r => r.Traffic.Bytes)} "
                + $"| {Stat(list, r => r.Drag.DispatchedMoves)} "
                + $"| {Stat(list, r => r.RoundTripMs)} |");
        }

        report.AppendLine();
        report.AppendLine("Format: Median (min–max). Aufschlüsselung der Nachrichten je Kandidat:");
        foreach (var (name, list) in results)
        {
            var merged = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in list.SelectMany(r => r.Traffic.Breakdown))
            {
                merged[pair.Key] = merged.GetValueOrDefault(pair.Key) + pair.Value;
            }

            var perGesture = merged.OrderByDescending(p => p.Value)
                .Select(p => $"{p.Key} ×{p.Value / (double)list.Count:F1}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- **{name}** (je Geste): {string.Join(", ", perGesture)}");
        }

        return report.ToString();
    }

    private static string Stat(List<GestureResult> list, Func<GestureResult, double> selector)
    {
        var values = list.Select(selector).Where(v => !double.IsNaN(v)).ToArray();
        if (values.Length == 0)
        {
            return "–";
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"{SpikeInteraction.Median(values):F1} ({values.Min():F1}–{values.Max():F1})");
    }
}
