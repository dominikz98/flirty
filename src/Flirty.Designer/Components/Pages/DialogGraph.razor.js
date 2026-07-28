// Verschieben und Zoomen des Graph-Canvas (#101) sowie das Ziehen einzelner Knoten (#102).
//
// Warum das hier und nicht in C#: Der Designer ist Blazor *Server*. Jedes Blazor-Ereignis ist ein
// SignalR-Roundtrip – liefe das Verschieben in C#, kostete jeder Zeigerschritt eine Netzwerkumlaufzeit.
// Der Spike zu #100 hat das gemessen (40 px Rückstand hinter dem Zeiger, 68 Nachrichten je Geste);
// ADR 0006 macht daraus eine Zusage: Zwischen pointerdown und pointerup geht KEINE Nachricht an den
// Server. Für den Knoten-Zug (#102) gilt sie genauso – die einzige Stelle in dieser Datei, die
// invokeMethodAsync aufruft, ist onPointerUp. Wer hier einen Aufruf in onPointerMove ergänzt, bricht
// ein Akzeptanzkriterium.
//
// Die Wahrheit über die Ansicht liegt deshalb im DOM, nicht im C#-Zustand. Das ist der Grund, warum
// DialogGraph.razor auf .graph-viewport niemals selbst ein transform rendert: Der nächste Re-Render
// würde Verschiebung und Zoom sonst zurücksetzen. Beim Knoten dagegen *ist* das transform C#-Zustand –
// deshalb schreibt der Zug es nur vorläufig und der Commit lässt C# denselben Wert rendern.

const states = new WeakMap();

/** Ab wie vielen Bildschirm-Pixeln aus einem Klick ein Zug wird. */
const DRAG_THRESHOLD = 4;

/**
 * Bindet Zeiger- und Rad-Steuerung an einen Canvas.
 * @param {SVGSVGElement} canvas Das SVG-Element.
 * @param {object} dotNetRef Rückkanal nach C# – Ziel von MoveNodeAsync beim Loslassen eines Knotens.
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
        drag: null,
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
    if (event.button !== 0) {
        return;
    }

    // Ein Knoten wird gezogen – aber erst ab der Schwelle. Bis dahin bleibt die Geste ein Klick, damit
    // Blazors @onclick (die Auswahl) weiter funktioniert. Deshalb hier KEIN preventDefault und noch
    // keine Pointer-Capture.
    const node = event.target.closest(".graph-node");
    if (node) {
        const origin = nodeOrigin(node);
        const point = toUserSpace(state, event);
        if (origin && point) {
            state.drag = {
                pointerId: event.pointerId,
                node,
                nodeId: node.getAttribute("data-node-id"),
                startX: event.clientX,
                startY: event.clientY,
                grabX: point.x - origin.x,
                grabY: point.y - origin.y,
                x: origin.x,
                y: origin.y,
                moved: false,
            };
        }

        return;
    }

    // Trifft die Geste ein anderes bedienbares Element, gehört sie diesem – und zwar VOLLSTÄNDIG: kein
    // preventDefault(), sonst verpufft der nachfolgende Klick und damit Blazors @onclick.
    if (event.target.closest(".graph-edge-hit, .graph-loop-label, .chip, button")) {
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
    const drag = state.drag;
    if (drag && drag.pointerId === event.pointerId) {
        onNodeMove(state, drag, event);
        return;
    }

    const pan = state.pan;
    if (!pan || pan.pointerId !== event.pointerId) {
        return;
    }

    state.x = pan.originX + (event.clientX - pan.startX);
    state.y = pan.originY + (event.clientY - pan.startY);
    render(state);
    event.preventDefault();
}

function onNodeMove(state, drag, event) {
    if (!drag.moved) {
        const distance = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY);
        if (distance < DRAG_THRESHOLD) {
            return;
        }

        beginNodeDrag(state, drag, event);
    }

    const point = toUserSpace(state, event);
    if (!point) {
        return;
    }

    // Nie über den Ursprung hinaus: Die viewBox beginnt links bei 0, ein negativer Wert schöbe den
    // Knoten aus der Zeichenfläche – und der Command lehnt ihn ohnehin ab.
    drag.x = Math.max(0, Math.round(point.x - drag.grabX));
    drag.y = Math.max(0, Math.round(point.y - drag.grabY));

    drag.node.setAttribute("transform", `translate(${drag.x} ${drag.y})`);
    event.preventDefault();
}

function beginNodeDrag(state, drag, event) {
    drag.moved = true;

    state.canvas.classList.add("is-dragging");
    drag.node.classList.add("is-dragging");

    // Die anliegenden Kanten werden gedimmt statt live neu berechnet: Ihr Verlauf entsteht in C#
    // (GraphLayout.Route) und ist dort getestet – ihn im Browser nachzubauen wäre eine zweite Quelle
    // für dieselbe Geometrie. Nach dem Commit zeichnet C# die exakten Pfade.
    for (const edge of incidentEdges(state.canvas, drag.nodeId)) {
        edge.classList.add("is-stale");
    }

    state.canvas.setPointerCapture(event.pointerId);
}

function onPointerUp(state, event) {
    const drag = state.drag;
    if (drag && drag.pointerId === event.pointerId) {
        endNodeDrag(state, drag, event);
        return;
    }

    const pan = state.pan;
    if (!pan || pan.pointerId !== event.pointerId) {
        return;
    }

    state.pan = null;
    state.canvas.classList.remove("is-panning");
    releasePointer(state, event.pointerId);
}

function endNodeDrag(state, drag, event) {
    state.drag = null;

    if (!drag.moved) {
        // Unter der Schwelle geblieben: Das war ein Klick. Nichts anfassen – der folgende click
        // trägt Blazors Auswahl.
        return;
    }

    state.canvas.classList.remove("is-dragging");
    drag.node.classList.remove("is-dragging");
    releasePointer(state, event.pointerId);

    swallowNextClick(state);

    // Genau EINE Nachricht je Geste – die einzige Interop-Stelle dieses Moduls.
    state.dotNetRef?.invokeMethodAsync("MoveNodeAsync", drag.nodeId, drag.x, drag.y);
}

/**
 * Verschluckt das click, das der Browser nach dem Loslassen noch auf den Knoten feuert. Ohne diesen
 * Riegel wählte jeder Zug den Knoten zusätzlich aus – eine zweite Nachricht und ein Nebeneffekt, den
 * niemand ausgelöst hat.
 *
 * Der Timeout räumt den Horcher auch dann ab, wenn ausnahmsweise kein click folgt (etwa weil der Zeiger
 * das Fenster verlassen hat). Er läuft nach dem click derselben Geste – sonst bliebe der Riegel liegen
 * und schluckte irgendwann einen echten Klick.
 */
function swallowNextClick(state) {
    const swallow = (event) => {
        event.stopPropagation();
        event.preventDefault();
    };

    state.canvas.addEventListener("click", swallow, { capture: true, once: true });
    setTimeout(() => state.canvas.removeEventListener("click", swallow, { capture: true }), 0);
}

function releasePointer(state, pointerId) {
    if (state.canvas.hasPointerCapture(pointerId)) {
        state.canvas.releasePointerCapture(pointerId);
    }
}

function incidentEdges(canvas, nodeId) {
    return canvas.querySelectorAll(
        `.graph-edge[data-from="${nodeId}"], .graph-edge[data-to="${nodeId}"]`);
}

/** Liest die linke obere Ecke eines Knotens aus seinem transform. */
function nodeOrigin(node) {
    const matrix = node.transform?.baseVal?.consolidate()?.matrix;
    return matrix ? { x: matrix.e, y: matrix.f } : null;
}

/**
 * Rechnet einen Zeigerpunkt in Nutzerkoordinaten des Viewports um.
 *
 * Über getScreenCTM statt über state.scale: Die Matrix enthält auch die Skalierung, die die viewBox
 * gegenüber der CSS-Breite des SVG erzeugt. Wer nur durch state.scale teilt, unterschlägt diesen
 * Faktor – der Knoten liefe dann je nach Fensterbreite schneller oder langsamer als der Zeiger.
 */
function toUserSpace(state, event) {
    const matrix = state.viewport.getScreenCTM();

    return matrix ? new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse()) : null;
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
