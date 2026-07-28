using Flirty.Designer.Components.Pages.Spike;
using Xunit.Abstractions;

namespace Flirty.E2E.Spike;

/// <summary>
/// SPIKE #100 (Wegwerf): reine Diagnose, kein Akzeptanzkriterium. Klärt, warum eine Geste in einem
/// Prototyp nicht ankommt.
/// </summary>
public sealed class CanvasSpikeDiagnosticTests : IClassFixture<CanvasSpikeFixture>
{
    private readonly CanvasSpikeFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CanvasSpikeDiagnosticTests(CanvasSpikeFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [SkippableFact]
    public async Task Diagnose_Blazor_Diagrams()
    {
        _fixture.DelayMilliseconds = 0;
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await session.NewPageAsync();

        page.Console += (_, m) => _output.WriteLine($"[console:{m.Type}] {m.Text}");
        page.PageError += (_, e) => _output.WriteLine($"[pageerror] {e}");

        await page.GotoAsync($"{_fixture.BaseUrl}/spike/canvas-b");
        await SpikeInteraction.WaitForCircuitAsync(page);
        await page.WaitForTimeoutAsync(1500);

        _output.WriteLine("typeof ZBlazorDiagrams = "
            + await page.EvaluateAsync<string>("() => typeof window.ZBlazorDiagrams"));
        _output.WriteLine("registrierte Canvases = "
            + await page.EvaluateAsync<string>(
                "() => window.ZBlazorDiagrams ? JSON.stringify(Object.keys(window.ZBlazorDiagrams.canvases)) : 'n/a'"));
        _output.WriteLine("verfolgte Elemente = "
            + await page.EvaluateAsync<string>(
                "() => window.ZBlazorDiagrams ? Object.keys(window.ZBlazorDiagrams.tracked).length : -1"));
        _output.WriteLine("Canvas-Rect = "
            + await page.EvaluateAsync<string>(
                "() => { const c = document.querySelector('.diagram-canvas'); return c ? JSON.stringify(c.getBoundingClientRect()) : 'kein Canvas'; }"));
        _output.WriteLine("Canvas-outerHTML (Anfang) = "
            + await page.EvaluateAsync<string>(
                "() => { const c = document.querySelector('.diagram-canvas'); return c ? c.outerHTML.substring(0, 400) : 'kein Canvas'; }"));

        // Kommen die Pointer-Ereignisse überhaupt am Canvas an?
        await page.EvaluateAsync("""
            () => {
                window.__seen = { down: 0, move: 0, up: 0 };
                const c = document.querySelector('.diagram-canvas');
                c.addEventListener('pointerdown', () => window.__seen.down++, true);
                c.addEventListener('pointermove', () => window.__seen.move++, true);
                c.addEventListener('pointerup',   () => window.__seen.up++,   true);
            }
            """);

        var target = page.Locator($"[data-node-id='{SpikeGraph.Nodes[SpikeGraph.DragTargetIndex].Key}']");
        var before = await target.BoundingBoxAsync();
        Assert.NotNull(before);
        _output.WriteLine($"Zielknoten vorher: x={before.X:F1} y={before.Y:F1} w={before.Width:F1} h={before.Height:F1}");

        await SpikeInteraction.DragAsync(page, before, deltaX: 200);
        await page.WaitForTimeoutAsync(1000);

        _output.WriteLine("Pointer-Ereignisse am Canvas = "
            + await page.EvaluateAsync<string>("() => JSON.stringify(window.__seen)"));

        var after = await target.BoundingBoxAsync();
        _output.WriteLine($"Zielknoten nachher: x={after!.X:F1} y={after.Y:F1}");
        _output.WriteLine("transform = "
            + await target.EvaluateAsync<string>("el => el.getAttribute('transform')"));
        _output.WriteLine("Klasse = " + await target.EvaluateAsync<string>("el => el.getAttribute('class')"));
        _output.WriteLine("Diagram-Zähler = " + await page.Locator("#diag").InnerTextAsync());
        _output.WriteLine("verfolgte Elemente nach ControlledSize = "
            + await page.EvaluateAsync<string>(
                "() => String(Object.keys(window.ZBlazorDiagrams.tracked).length)"));

        // Der Messlauf zählt je Geste 37 BeginInvokeDotNetFromJS und keinen einzigen
        // DispatchBrowserEvent. Wer ruft da aus JS nach .NET? Belegt statt vermutet: Ein
        // DotNetObjectReference ruft NICHT über DotNet.invokeMethodAsync, sondern über die
        // Prototyp-Methode der Referenz – dort wird mitgezählt.
        await page.EvaluateAsync("""
            () => {
                window.__calls = {};
                const reference = Object.values(window.ZBlazorDiagrams.canvases)[0]?.ref;
                if (!reference) { window.__patched = 'keine Canvas-Referenz gefunden'; return; }
                const prototype = Object.getPrototypeOf(reference);
                const original = prototype.invokeMethodAsync;
                prototype.invokeMethodAsync = function (method, ...args) {
                    window.__calls[method] = (window.__calls[method] || 0) + 1;
                    return original.apply(this, [method, ...args]);
                };
                window.__patched = 'ok';
            }
            """);

        var second = await target.BoundingBoxAsync();
        Assert.NotNull(second);
        await SpikeInteraction.DragAsync(page, second, deltaX: 100);
        await page.WaitForTimeoutAsync(1000);

        _output.WriteLine("Referenz-Patch = " + await page.EvaluateAsync<string>("() => window.__patched"));
        _output.WriteLine("JS→.NET-Aufrufe der zweiten Geste = "
            + await page.EvaluateAsync<string>("() => JSON.stringify(window.__calls)"));
    }
}
