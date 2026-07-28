// Verschieben und Zoomen des Graph-Canvas (#101).
//
// Warum das hier und nicht in C#: Der Designer ist Blazor *Server*. Jedes Blazor-Ereignis ist ein
// SignalR-Roundtrip – liefe das Verschieben in C#, kostete jeder Zeigerschritt eine Netzwerkumlaufzeit.
// Der Spike zu #100 hat das gemessen (40 px Rückstand hinter dem Zeiger, 68 Nachrichten je Geste);
// ADR 0006 macht daraus eine Zusage: Zwischen pointerdown und pointerup geht KEINE Nachricht an den
// Server.
//
// Die Wahrheit über die Ansicht liegt deshalb im DOM, nicht im C#-Zustand. Das ist der Grund, warum
// DialogGraph.razor auf .graph-viewport niemals selbst ein transform rendert: Der nächste Re-Render
// würde Verschiebung und Zoom sonst zurücksetzen.

const states = new WeakMap();

/**
 * Bindet Zeiger- und Rad-Steuerung an einen Canvas.
 * @param {SVGSVGElement} canvas Das SVG-Element.
 * @param {object} dotNetRef Rückkanal nach C#. In dieser Stufe ungenutzt – ohne Layout-Persistenz
 *   braucht der Server die Ansicht nicht zu kennen. Er wird trotzdem gehalten, damit die Stufen 2 und 3
 *   ihre Rückrufe anhängen können, ohne die Signatur zu brechen.
 * @param {{minZoom: number, maxZoom: number, zoomStep: number}} options Grenzwerte aus GraphMetrics.
 */
export function attach(canvas, dotNetRef, options) {
    if (!canvas || states.has(canvas)) {
        return;
    }

    const viewport = canvas.querySelector(".graph-viewport");
    if (!viewport) {
        return;
    }

    const state = {
        canvas,
        viewport,
        dotNetRef,
        options,
        x: 0,
        y: 0,
        scale: 1,
        pan: null,
    };

    state.onPointerDown = (event) => onPointerDown(state, event);
    state.onPointerMove = (event) => onPointerMove(state, event);
    state.onPointerUp = (event) => onPointerUp(state, event);
    state.onWheel = (event) => onWheel(state, event);
    state.onKeyDown = (event) => onKeyDown(event);

    canvas.addEventListener("pointerdown", state.onPointerDown);
    canvas.addEventListener("pointermove", state.onPointerMove);
    canvas.addEventListener("pointerup", state.onPointerUp);
    canvas.addEventListener("pointercancel", state.onPointerUp);
    canvas.addEventListener("wheel", state.onWheel, { passive: false });
    canvas.addEventListener("keydown", state.onKeyDown);

    states.set(canvas, state);

    // Explizites Bereitschaftssignal für die E2E-Suite. Das Wiederholmuster InteractWhenReadyAsync
    // trägt für einen Canvas nicht: Es setzt idempotente Aktionen voraus, ein wiederholtes Ziehen
    // verschöbe doppelt und ein wiederholter Zoomschritt zoomte zweimal.
    canvas.setAttribute("data-canvas-ready", "true");
}

/**
 * Löst die Bindung wieder.
 * @param {SVGSVGElement} canvas Das SVG-Element.
 */
export function detach(canvas) {
    const state = states.get(canvas);
    if (!state) {
        return;
    }

    canvas.removeEventListener("pointerdown", state.onPointerDown);
    canvas.removeEventListener("pointermove", state.onPointerMove);
    canvas.removeEventListener("pointerup", state.onPointerUp);
    canvas.removeEventListener("pointercancel", state.onPointerUp);
    canvas.removeEventListener("wheel", state.onWheel);
    canvas.removeEventListener("keydown", state.onKeyDown);
    canvas.removeAttribute("data-canvas-ready");
    states.delete(canvas);
}

/**
 * Zoomt um die Mitte der Ansicht – der Weg der Werkzeugleiste.
 * @param {SVGSVGElement} canvas Das SVG-Element.
 * @param {number} factor Der Faktor (> 1 vergrößert).
 */
export function zoomBy(canvas, factor) {
    const state = states.get(canvas);
    if (!state) {
        return;
    }

    const box = canvas.getBoundingClientRect();
    applyZoom(state, factor, box.width / 2, box.height / 2);
}

/**
 * Setzt Verschiebung und Zoom zurück.
 * @param {SVGSVGElement} canvas Das SVG-Element.
 */
export function resetViewport(canvas) {
    const state = states.get(canvas);
    if (!state) {
        return;
    }

    state.x = 0;
    state.y = 0;
    state.scale = 1;
    render(state);
}

/**
 * Holt einen Knoten in den sichtbaren Bereich – für die Tastaturbedienung.
 * @param {SVGSVGElement} canvas Das SVG-Element.
 * @param {string} nodeId Die Frage-Id.
 */
export function focusNode(canvas, nodeId) {
    const node = canvas?.querySelector(`[data-node-id="${nodeId}"] .graph-node-card`);
    if (node) {
        node.focus();
    }
}

function onPointerDown(state, event) {
    // Trifft die Geste ein bedienbares Element, gehört sie diesem – und zwar VOLLSTÄNDIG: kein
    // preventDefault(), sonst verpufft der nachfolgende Klick und damit Blazors @onclick.
    if (event.target.closest(".graph-node, .graph-edge-hit, .graph-loop-label, .chip, button")) {
        return;
    }

    if (event.button !== 0) {
        return;
    }

    state.pan = {
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        originX: state.x,
        originY: state.y,
    };

    state.canvas.classList.add("is-panning");
    state.canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
}

function onPointerMove(state, event) {
    const pan = state.pan;
    if (!pan || pan.pointerId !== event.pointerId) {
        return;
    }

    state.x = pan.originX + (event.clientX - pan.startX);
    state.y = pan.originY + (event.clientY - pan.startY);
    render(state);
    event.preventDefault();
}

function onPointerUp(state, event) {
    const pan = state.pan;
    if (!pan || pan.pointerId !== event.pointerId) {
        return;
    }

    state.pan = null;
    state.canvas.classList.remove("is-panning");
    if (state.canvas.hasPointerCapture(event.pointerId)) {
        state.canvas.releasePointerCapture(event.pointerId);
    }
}

function onWheel(state, event) {
    event.preventDefault();

    const box = state.canvas.getBoundingClientRect();
    const factor = event.deltaY < 0 ? state.options.zoomStep : 1 / state.options.zoomStep;

    applyZoom(state, factor, event.clientX - box.left, event.clientY - box.top);
}

function onKeyDown(event) {
    // Die Leertaste betätigt einen fokussierten Knoten – ohne diesen Riegel scrollt sie zusätzlich die
    // Seite. In Blazor ließe sich @onkeydown:preventDefault nur statisch setzen, was auch Tab träfe.
    if (event.key === " " && event.target.closest(".graph-node-card, .graph-loop-label")) {
        event.preventDefault();
    }
}

/**
 * Zoomt um einen Ankerpunkt: Der Punkt unter dem Zeiger bleibt stehen, alles andere skaliert um ihn
 * herum. Ohne Anker springt der Graph bei jedem Rad-Schritt weg.
 */
function applyZoom(state, factor, anchorX, anchorY) {
    const next = clamp(state.scale * factor, state.options.minZoom, state.options.maxZoom);
    if (next === state.scale) {
        return;
    }

    const ratio = next / state.scale;
    state.x = anchorX - ((anchorX - state.x) * ratio);
    state.y = anchorY - ((anchorY - state.y) * ratio);
    state.scale = next;
    render(state);
}

function render(state) {
    state.viewport.setAttribute(
        "transform",
        `translate(${round(state.x)} ${round(state.y)}) scale(${round(state.scale)})`);
}

function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
}

function round(value) {
    return Math.round(value * 1000) / 1000;
}
