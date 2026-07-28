using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;

namespace Flirty.E2E.Spike;

/// <summary>Ein Abtastwert je Animationsframe: Zeitpunkt, linke Kante des Knotens, Zeigerposition.</summary>
internal sealed record ProbeSample(
    [property: JsonPropertyName("t")] double Time,
    [property: JsonPropertyName("nodeLeft")] double NodeLeft,
    [property: JsonPropertyName("pointerX")] double PointerX);

/// <summary>Die Rohdaten einer Geste, wie sie die Sonde aus der Seite zurückgibt.</summary>
internal sealed record ProbeResult(
    [property: JsonPropertyName("moves")] double[] Moves,
    [property: JsonPropertyName("samples")] ProbeSample[] Samples);

/// <summary>Die ausgewertete Geste.</summary>
/// <param name="LagPixels">
/// <b>Primärmetrik.</b> Median des Rückstands des Knotens hinter dem Zeiger im eingeschwungenen
/// mittleren Drittel der Geste, in px.
/// </param>
/// <param name="LagMilliseconds">Derselbe Rückstand, über die Gestengeschwindigkeit in Zeit umgerechnet.</param>
/// <param name="SettleMilliseconds">
/// <b>Kontrollmetrik.</b> Zeit vom letzten <c>pointermove</c> bis der Knoten endgültig stillsteht.
/// Macht den Rückstau durch <c>MaxBufferedUnacknowledgedRenderBatches</c> (Default 10) sichtbar.
/// </param>
/// <param name="DispatchedMoves">
/// Tatsächlich im Browser ausgelöste <c>pointermove</c>-Ereignisse. Chromium fasst sie auf den
/// Animationsframe zusammen, es sind also weniger als die abgesetzten Maus-Kommandos – und diese Zahl
/// begrenzt die Roundtrips des serverseitigen Kandidaten. Gehört deshalb neben jedes Ergebnis.
/// </param>
/// <param name="Samples">Zahl der Abtastwerte (Plausibilitätswert für die Framerate).</param>
internal sealed record DragMeasurement(
    double LagPixels,
    double LagMilliseconds,
    double SettleMilliseconds,
    int DispatchedMoves,
    int Samples);

/// <summary>
/// SPIKE #100 (Wegwerf, wird NICHT gemergt): misst in der Seite, wie weit der Knoten dem Zeiger
/// hinterherhängt.
/// </summary>
/// <remarks>
/// <para>
/// Der Kunstgriff, der ein fehleranfälliges Zuordnen von Ereignissen zu DOM-Mutationen erspart: Beide
/// Prototypen verschieben den Knoten <b>exakt um das Zeiger-Delta</b>. Der Abstand zwischen Zeiger und
/// linker Knotenkante ist damit konstant, solange der Knoten folgt – <i>jede</i> Vergrößerung dieses
/// Abstands ist Rückstand. Ein <c>requestAnimationFrame</c>-Sampler liest ihn je Frame ab, ohne einen
/// Playwright-Roundtrip je Messpunkt.
/// </para>
/// <para>
/// Der <c>pointermove</c>-Horcher hängt an <c>window</c> (Capture-Phase, <c>passive</c>): So läuft er
/// vor dem Anwendungs-Handler, kostet beide Prototypen dasselbe, beeinflusst nichts – und überlebt,
/// dass Blazor den Knoten neu rendert.
/// </para>
/// </remarks>
internal static class DragProbe
{
    /// <summary>Installiert die Sonde auf dem angegebenen Knoten.</summary>
    /// <param name="page">Die Seite.</param>
    /// <param name="nodeSelector">CSS-Selektor des zu beobachtenden Knotens.</param>
    public static Task InstallAsync(IPage page, string nodeSelector)
        => page.EvaluateAsync(InstallScript, nodeSelector);

    /// <summary>Hält die Sonde an und wertet die Geste aus.</summary>
    /// <param name="page">Die Seite.</param>
    /// <returns>Die ausgewertete Geste.</returns>
    public static async Task<DragMeasurement> CollectAsync(IPage page)
    {
        // Bewusst als JSON-Zeichenkette: Playwrights eigener Konverter kann Records ohne
        // parameterlosen Konstruktor nicht erzeugen ("No parameterless constructor defined").
        var json = await page.EvaluateAsync<string>(CollectScript);
        var raw = JsonSerializer.Deserialize<ProbeResult>(json)
            ?? throw new InvalidOperationException("Die Sonde hat keine Daten geliefert.");

        return Analyse(raw);
    }

    /// <summary>Wertet die Rohdaten aus – getrennt testbar und ohne Browser nachvollziehbar.</summary>
    /// <param name="raw">Die Rohdaten der Sonde.</param>
    /// <returns>Die ausgewertete Geste.</returns>
    public static DragMeasurement Analyse(ProbeResult raw)
    {
        if (raw.Moves.Length < 3 || raw.Samples.Length < 3)
        {
            throw new InvalidOperationException(
                $"Zu wenig Messpunkte: {raw.Moves.Length} Bewegungen, {raw.Samples.Length} Abtastwerte.");
        }

        var firstMove = raw.Moves[0];
        var lastMove = raw.Moves[^1];

        // Der Sollabstand: der Wert, bevor sich überhaupt etwas bewegt hat.
        var baseline = raw.Samples
            .Where(s => s.Time <= firstMove)
            .Select(s => s.PointerX - s.NodeLeft)
            .DefaultIfEmpty(raw.Samples[0].PointerX - raw.Samples[0].NodeLeft)
            .Last();

        // Mittleres Drittel: der Anlauf (erste Frames) und das Auslaufen bleiben draußen.
        var windowStart = firstMove + ((lastMove - firstMove) / 3);
        var windowEnd = firstMove + (2 * (lastMove - firstMove) / 3);
        var inWindow = raw.Samples
            .Where(s => s.Time >= windowStart && s.Time <= windowEnd)
            .Select(s => (s.PointerX - s.NodeLeft) - baseline)
            .ToArray();

        var lagPixels = inWindow.Length > 0 ? SpikeInteraction.Median(inWindow) : double.NaN;

        // Umrechnung über die bekannte Gestengeschwindigkeit (DragStepPixels je DragStepDelayMs).
        var pixelsPerMs = SpikeInteraction.DragStepPixels / SpikeInteraction.DragStepDelayMs;
        var lagMilliseconds = lagPixels / pixelsPerMs;

        return new DragMeasurement(
            lagPixels,
            lagMilliseconds,
            SettleTime(raw, lastMove),
            raw.Moves.Length,
            raw.Samples.Length);
    }

    /// <summary>
    /// Zeit vom letzten <c>pointermove</c> bis der Knoten drei Frames lang (±1 px) stillsteht.
    /// </summary>
    private static double SettleTime(ProbeResult raw, double lastMove)
    {
        var tail = raw.Samples.Where(s => s.Time >= lastMove).ToArray();
        if (tail.Length == 0)
        {
            return double.NaN;
        }

        var final = tail[^1].NodeLeft;
        var stable = 0;
        for (var i = 0; i < tail.Length; i++)
        {
            if (Math.Abs(tail[i].NodeLeft - final) <= 1)
            {
                stable++;
                if (stable >= 3)
                {
                    return tail[i - 2].Time - lastMove;
                }
            }
            else
            {
                stable = 0;
            }
        }

        return tail[^1].Time - lastMove;
    }

    private const string InstallScript = """
        (selector) => {
            const state = { moves: [], samples: [], lastX: null, selector };
            state.onMove = (e) => {
                state.lastX = e.clientX;
                state.moves.push(performance.now());
            };
            addEventListener('pointermove', state.onMove, { capture: true, passive: true });
            const tick = () => {
                if (state.lastX !== null) {
                    // Je Frame neu abfragen: Blazor darf den Knoten neu rendern, ohne die Sonde zu
                    // entwerten. Eine querySelector-Abfrage je Frame ist gegenüber einem Roundtrip nichts.
                    const el = document.querySelector(state.selector);
                    if (el) {
                        state.samples.push({
                            t: performance.now(),
                            nodeLeft: el.getBoundingClientRect().left,
                            pointerX: state.lastX,
                        });
                    }
                }
                state.raf = requestAnimationFrame(tick);
            };
            state.raf = requestAnimationFrame(tick);
            window.__spikeProbe = state;
        }
        """;

    private const string CollectScript = """
        () => {
            const state = window.__spikeProbe;
            if (!state) { return JSON.stringify({ moves: [], samples: [] }); }
            cancelAnimationFrame(state.raf);
            removeEventListener('pointermove', state.onMove, { capture: true });
            window.__spikeProbe = null;
            return JSON.stringify({ moves: state.moves, samples: state.samples });
        }
        """;
}
