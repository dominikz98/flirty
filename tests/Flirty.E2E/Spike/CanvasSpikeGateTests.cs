using Flirty.Designer.Components.Pages.Spike;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Flirty.E2E.Spike;

/// <summary>
/// SPIKE #100 (Wegwerf, wird NICHT gemergt): die beiden Vorbedingungen des Messlaufs. Sie laufen
/// getrennt, weil ein Messergebnis wertlos ist, solange eine von beiden nicht steht – und weil sie
/// beim Scheitern eine eindeutige Ursache benennen.
/// </summary>
public sealed class CanvasSpikeGateTests : IClassFixture<CanvasSpikeFixture>
{
    private readonly CanvasSpikeFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Initialisiert das Gate mit dem gedrosselten Designer-Host.</summary>
    public CanvasSpikeGateTests(CanvasSpikeFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Gate S0: Lässt sich in Blazor.Diagrams unter <c>InteractiveServer</c> überhaupt ein Knoten
    /// ziehen? ZBD-Issue #425 berichtet genau hier ein Totalversagen im Standard-Template. Ohne dieses
    /// Gate misst der Messlauf womöglich eine Seite, auf der schlicht nichts passiert.
    /// </summary>
    [SkippableFact]
    public async Task Gate_S0_Blazor_Diagrams_laesst_einen_Knoten_ziehen()
    {
        _fixture.DelayMilliseconds = 0;
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await session.NewPageAsync();

        await page.GotoAsync($"{_fixture.BaseUrl}/spike/canvas-b");
        await SpikeInteraction.WaitForCircuitAsync(page);

        var nodes = page.Locator("[data-node-id]");
        await Assertions.Expect(nodes).ToHaveCountAsync(SpikeGraph.Nodes.Count, new() { Timeout = 15_000 });

        // Der DOM-Aufbau von ZBD ist die Grundlage aller Selektoren im Messlauf – einmal protokollieren.
        _output.WriteLine("Erster Knoten im DOM:");
        _output.WriteLine(await nodes.First.EvaluateAsync<string>("el => el.outerHTML"));
        _output.WriteLine("Erste Kante im DOM:");
        _output.WriteLine(await page.Locator(".diagram-link").First.EvaluateAsync<string>("el => el.outerHTML"));

        var target = page.Locator($"[data-node-id='{SpikeGraph.Nodes[SpikeGraph.DragTargetIndex].Key}']");
        var before = await target.BoundingBoxAsync();
        Assert.NotNull(before);

        await SpikeInteraction.DragAsync(page, before, deltaX: 200);

        var after = await target.BoundingBoxAsync();
        Assert.NotNull(after);
        _output.WriteLine($"Knoten x: {before.X:F1} -> {after.X:F1} (Δ {after.X - before.X:F1} px)");

        Assert.True(
            after.X - before.X > 100,
            $"Der Knoten hat sich nicht mitbewegt (Δ {after.X - before.X:F1} px). "
            + "Blazor.Diagrams ist unter InteractiveServer nicht in Betrieb – siehe ZBD-Issue #425.");
    }

    /// <summary>
    /// Gate S0b: die Gegenprobe für Prototyp A. Dieselbe Geste, derselbe Graph, derselbe Greifpunkt –
    /// hier muss der Knoten <b>ohne</b> Zutun des Servers folgen, und der Rückkanal darf genau
    /// <b>einmal</b> feuern.
    /// </summary>
    [SkippableFact]
    public async Task Gate_S0b_Der_Eigenbau_zieht_clientseitig_und_meldet_genau_einmal()
    {
        _fixture.DelayMilliseconds = 0;
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await session.NewPageAsync();

        await page.GotoAsync($"{_fixture.BaseUrl}/spike/canvas-a");
        await SpikeInteraction.WaitForCircuitAsync(page);
        await page.WaitForSelectorAsync("#spike-canvas-a[data-canvas-ready='true']");

        await Assertions.Expect(page.Locator("[data-node-id]"))
            .ToHaveCountAsync(SpikeGraph.Nodes.Count, new() { Timeout = 15_000 });
        await Assertions.Expect(page.Locator("[data-edge-id]"))
            .ToHaveCountAsync(SpikeGraph.Edges.Count, new() { Timeout = 15_000 });

        var target = page.Locator($"[data-node-id='{SpikeGraph.Nodes[SpikeGraph.DragTargetIndex].Key}']");
        var before = await target.BoundingBoxAsync();
        Assert.NotNull(before);

        await SpikeInteraction.DragAsync(page, before, deltaX: 200);

        var after = await target.BoundingBoxAsync();
        Assert.NotNull(after);
        _output.WriteLine($"Knoten x: {before.X:F1} -> {after.X:F1} (Δ {after.X - before.X:F1} px)");
        Assert.True(after.X - before.X > 100, $"Der Knoten ist nicht gefolgt (Δ {after.X - before.X:F1} px).");

        // Der Rückkanal: genau eine Nachricht je Geste – und der Server hat die Position übernommen.
        await Assertions.Expect(page.Locator("#drag-commits")).ToHaveTextAsync("1", new() { Timeout = 5_000 });
        await page.ReloadAsync();
        await SpikeInteraction.WaitForCircuitAsync(page);
        var reloaded = await target.BoundingBoxAsync();
        Assert.NotNull(reloaded);
        _output.WriteLine($"Nach Reload (Server-Zustand, ohne Persistenz): x={reloaded.X:F1}");
    }

    /// <summary>
    /// Gate S1: Wirkt die Latenz-Injektion überhaupt auf den Circuit? Gemessen wird die Umlaufzeit
    /// eines echten Blazor-Ereignisses (Klick → Server → Render → DOM), einmal ohne und einmal mit
    /// 75 ms Einweg-Verzögerung. Bleibt der zweite Wert niedrig, ist der Proxy defekt – meist
    /// vergessenes <c>NoDelay</c> oder nicht entkoppelte Lese-/Schreibseite.
    /// </summary>
    [SkippableFact]
    public async Task Gate_S1_Der_Latenz_Proxy_verzoegert_den_Circuit()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await session.NewPageAsync();

        _fixture.DelayMilliseconds = 0;
        await page.GotoAsync($"{_fixture.BaseUrl}/spike/canvas-a");
        await SpikeInteraction.WaitForCircuitAsync(page);

        var baseline = await SpikeInteraction.MeasureCircuitRoundTripAsync(page);
        _output.WriteLine($"RTT ohne Drosselung:  {baseline:F1} ms");

        _fixture.DelayMilliseconds = 75;
        var throttled = await SpikeInteraction.MeasureCircuitRoundTripAsync(page);
        _output.WriteLine($"RTT mit 2 x 75 ms:    {throttled:F1} ms");
        _fixture.DelayMilliseconds = 0;

        Assert.True(baseline < 60, $"Die ungedrosselte RTT ist mit {baseline:F1} ms unplausibel hoch.");
        Assert.True(
            throttled >= 140,
            $"Die Drosselung wirkt nicht: {throttled:F1} ms statt >= 140 ms. Proxy prüfen "
            + "(NoDelay auf beiden Sockets, Reader/Writer entkoppelt).");
    }
}
