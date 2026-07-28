using Microsoft.Playwright;

namespace Flirty.E2E.Spike;

/// <summary>
/// SPIKE #100 (Wegwerf, wird NICHT gemergt): die gemeinsame Gestensteuerung beider Prototypen. Beide
/// werden mit <b>identischer</b> Geste und identischer Bereitschaftsprüfung bedient – sonst ist der
/// Vergleich wertlos.
/// </summary>
internal static class SpikeInteraction
{
    /// <summary>Zahl der Zwischenschritte einer gemessenen Zieh-Geste.</summary>
    public const int DragSteps = 30;

    /// <summary>Weg je Zwischenschritt in px.</summary>
    public const double DragStepPixels = 10;

    /// <summary>Taktung der Zwischenschritte in ms (~60 Hz).</summary>
    public const int DragStepDelayMs = 16;

    /// <summary>Gesamtweg einer Geste in px.</summary>
    public const double DragDistance = DragSteps * DragStepPixels;

    /// <summary>
    /// Wartet, bis der Circuit die Seite tatsächlich übernommen hat – mit einem <b>echten</b> Signal
    /// statt einer Wiederholschleife: Die RTT-Sonde beider Prototypen ändert bei Klick einen Zähler
    /// im DOM. Kommt die Änderung an, verarbeitet der Circuit Ereignisse.
    /// </summary>
    /// <remarks>
    /// <c>DesignerE2ETests.InteractWhenReadyAsync</c> wiederholt stattdessen eine idempotente Aktion.
    /// Für einen Canvas trägt das nicht: Ein wiederholter Drag verschöbe doppelt. Genau deshalb bekommt
    /// jede Spike-Seite ein explizites Bereitschaftssignal – ein Befund, der in den ADR gehört.
    /// </remarks>
    /// <param name="page">Die Seite.</param>
    public static async Task WaitForCircuitAsync(IPage page)
    {
        await page.WaitForSelectorAsync("#rtt-probe");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var elapsed = await page.EvaluateAsync<double>(RoundTripScript);
            if (elapsed >= 0)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "Der Blazor-Circuit hat die Spike-Seite nicht übernommen (RTT-Sonde antwortet nicht).");
    }

    /// <summary>
    /// Misst die Umlaufzeit eines echten Blazor-Ereignisses: Klick → Server → Render-Batch → DOM.
    /// Median aus fünf Messungen. Diese Zahl steht neben jedem Messwert – ohne sie ist kein Ergebnis
    /// auf einer anderen Maschine interpretierbar.
    /// </summary>
    /// <param name="page">Die Seite.</param>
    /// <returns>Die Umlaufzeit in Millisekunden.</returns>
    public static async Task<double> MeasureCircuitRoundTripAsync(IPage page)
    {
        var samples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var elapsed = await page.EvaluateAsync<double>(RoundTripScript);
            if (elapsed >= 0)
            {
                samples.Add(elapsed);
            }
        }

        return samples.Count == 0 ? -1 : Median(samples);
    }

    /// <summary>
    /// Führt die gemessene Geste aus: aufsetzen, <see cref="DragSteps"/> Zwischenschritte im
    /// <see cref="DragStepDelayMs"/>-Takt, loslassen.
    /// </summary>
    /// <remarks>
    /// Bewusst <b>nicht</b> <c>MoveAsync(..., Steps = n)</c>: Playwright wartet zwischen diesen Schritten
    /// nicht, die CDP-Aufrufe liegen 1–3 ms auseinander. Das wäre eine Sturzflut, die kein Anwender
    /// erzeugt – und sie würde den serverseitigen Kandidaten unrealistisch hart treffen.
    /// </remarks>
    /// <param name="page">Die Seite.</param>
    /// <param name="nodeBox">Die Bounding-Box des zu ziehenden Knotens.</param>
    /// <param name="deltaX">Gesamtweg in px; wird auf <see cref="DragSteps"/> Schritte verteilt.</param>
    public static async Task DragAsync(IPage page, LocatorBoundingBoxResult nodeBox, double deltaX = DragDistance)
    {
        var (startX, startY) = GrabPoint(nodeBox);
        var step = deltaX / DragSteps;

        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();

        for (var k = 1; k <= DragSteps; k++)
        {
            await page.Mouse.MoveAsync((float)(startX + (k * step)), (float)startY);
            await page.WaitForTimeoutAsync(DragStepDelayMs);
        }

        await page.Mouse.UpAsync();
    }

    /// <summary>
    /// Der Punkt, an dem der Knoten gegriffen wird: linkes oberes Viertel, <b>nicht</b> die Mitte.
    /// </summary>
    /// <remarks>
    /// Am Mittelpunkt laufen alle Kanten des Knotens zusammen. In Blazor.Diagrams liegen die Kanten in
    /// derselben SVG-Ebene <i>nach</i> den Knoten, also darüber – ein <c>pointerdown</c> auf die Mitte
    /// trifft dort eine Kante statt des Knotens (nachgemessen: das Modell beim <c>pointerdown</c> war
    /// ein Link). Prototyp A zeichnet die Kanten <i>vor</i> den Knoten und hätte das Problem nicht;
    /// beide werden trotzdem am selben relativen Punkt gegriffen, damit die Geste identisch ist.
    /// </remarks>
    /// <param name="nodeBox">Die Bounding-Box des Knotens.</param>
    /// <returns>Der Greifpunkt in Viewport-Koordinaten.</returns>
    public static (double X, double Y) GrabPoint(LocatorBoundingBoxResult nodeBox)
        => (nodeBox.X + (nodeBox.Width * 0.25), nodeBox.Y + (nodeBox.Height * 0.25));

    /// <summary>Median einer Stichprobe (nie das arithmetische Mittel – ein GC-Ausreißer dominiert es).</summary>
    /// <param name="values">Die Messwerte.</param>
    /// <returns>Der Median.</returns>
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>
    /// Klickt die RTT-Sonde <b>in der Seite</b> (nicht über den Playwright-Treiber) und misst, wann der
    /// vom Server gerenderte Zähler im DOM ankommt. Der Klick läuft in-page, damit der Treiber-Hop
    /// nicht in die Messung fällt. Rückgabe <c>-1</c> = keine Antwort innerhalb von 5 s.
    /// </summary>
    private const string RoundTripScript = """
        () => new Promise(resolve => {
            const token = document.getElementById('rtt-token');
            const probe = document.getElementById('rtt-probe');
            if (!token || !probe) { resolve(-1); return; }
            const before = token.textContent;
            const observer = new MutationObserver(() => {
                if (token.textContent !== before) {
                    observer.disconnect();
                    clearTimeout(timer);
                    resolve(performance.now() - t0);
                }
            });
            observer.observe(token, { childList: true, characterData: true, subtree: true });
            const timer = setTimeout(() => { observer.disconnect(); resolve(-1); }, 5000);
            const t0 = performance.now();
            probe.click();
        })
        """;
}
