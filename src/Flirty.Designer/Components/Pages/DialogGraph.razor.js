// Verschieben und Zoomen des Graph-Canvas (#101), das Ziehen einzelner Knoten (#102) sowie die
// Editier-Gesten: Bausteine aus der Palette ablegen und Knoten verbinden (#103).
//
// Warum das hier und nicht in C#: Der Designer ist Blazor *Server*. Jedes Blazor-Ereignis ist ein
// SignalR-Roundtrip – liefe das Verschieben in C#, kostete jeder Zeigerschritt eine Netzwerkumlaufzeit.
// Der Spike zu #100 hat das gemessen (40 px Rückstand hinter dem Zeiger, 68 Nachrichten je Geste);
// ADR 0006 macht daraus eine Zusage: Zwischen pointerdown und pointerup geht KEINE Nachricht an den
// Server. Für Knoten-Zug, Verbindungsgeste und Palette-Drop gilt sie genauso – die einzige Stelle, die
// invokeMethodAsync aufruft, ist die Hilfsfunktion send(), und die wird ausschließlich aus einem
// pointerup heraus gerufen. Wer hier einen Aufruf in einem pointermove ergänzt, bricht ein
// Akzeptanzkriterium.
//
// Die Wahrheit über die Ansicht liegt deshalb im DOM, nicht im C#-Zustand. Das ist der Grund, warum
// DialogGraph.razor auf .graph-viewport niemals selbst ein transform rendert: Der nächste Re-Render
// würde Verschiebung und Zoom sonst zurücksetzen. Beim Knoten dagegen *ist* das transform C#-Zustand –
// deshalb schreibt der Zug es nur vorläufig und der Commit lässt C# denselben Wert rendern.
//
// Umgekehrt gilt für den Zustand, der C# gehört: data-editable und data-busy am <svg> rendert C#, das
// Modul LIEST sie nur – und zwar bei jeder Geste frisch, nicht als beim attach eingefrorene Option.
//
// GESTEN SIND NICHT IDEMPOTENT. Ein doppelt ausgelöster Drop legt zwei Fragen an, eine doppelte
// Verbindungsgeste zwei Übergänge. Deshalb läuft jede Nachricht über send(): Es sperrt bis die
// zugehörige .NET-Methode zurückkehrt. Das Versprechen von invokeMethodAsync IST die Quittung – ein
// zweiter Rückkanal wird nicht gebraucht und könnte vergessen werden.

const states = new WeakMap();

/** Ab wie vielen Bildschirm-Pixeln aus einem Klick ein Zug wird. */
const DRAG_THRESHOLD = 4;

/**
 * Bindet Zeiger- und Rad-Steuerung an einen Canvas.
 * @param {SVGSVGElement} canvas Das SVG-Element.
 * @param {object} dotNetRef Rückkanal nach C# – Ziel aller Gesten-Meldungen beim Loslassen.
 * @param {{minZoom: number, maxZoom: number, zoomStep: number, nodeWidth: number, nodeHeight: number}} options
 *        Grenzwerte und Maße aus GraphMetrics – nie im JS hartkodiert, sonst gibt es zwei Wahrheiten.
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
        // Die beiden Platzhalter rendert C#; dieses Modul füllt nur ihre Geometrie (siehe Modulkopf).
        rubber: canvas.querySelector(".graph-rubber"),
        ghost: canvas.querySelector(".graph-ghost"),
        // Die Palette liegt neben dem Canvas, nicht darin – ihre Zeiger-Ereignisse erreichen ihn also
        // nie. Gesucht statt als Parameter gereicht: Beide sind Kinder derselben .graph-layout, und ein
        // zusätzlicher ElementReference-Parameter müsste durch die GraphPalette-Komponente
        // durchgeschleift werden, ohne etwas zu gewinnen.
        palette: canvas.closest(".graph-layout")?.querySelector(".graph-palette") ?? null,
        x: 0,
        y: 0,
        scale: 1,
        busy: false,
        pan: null,
        drag: null,
        link: null,
        spawn: null,
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

    if (state.palette) {
        state.onPaletteDown = (event) => onPaletteDown(state, event);
        state.onPaletteMove = (event) => onPaletteMove(state, event);
        state.onPaletteUp = (event) => onPaletteUp(state, event);

        state.palette.addEventListener("pointerdown", state.onPaletteDown);
        state.palette.addEventListener("pointermove", state.onPaletteMove);
        state.palette.addEventListener("pointerup", state.onPaletteUp);
        state.palette.addEventListener("pointercancel", state.onPaletteUp);
    }

    states.set(canvas, state);

    // Explizites Bereitschaftssignal für die E2E-Suite. Das Wiederholmuster InteractWhenReadyAsync
    // trägt für einen Canvas nicht: Es setzt idempotente Aktionen voraus, ein wiederholtes Ziehen
    // verschöbe doppelt und eine wiederholte Verbindungsgeste legte zwei Übergänge an.
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

    if (state.palette && state.onPaletteDown) {
        state.palette.removeEventListener("pointerdown", state.onPaletteDown);
        state.palette.removeEventListener("pointermove", state.onPaletteMove);
        state.palette.removeEventListener("pointerup", state.onPaletteUp);
        state.palette.removeEventListener("pointercancel", state.onPaletteUp);
    }

    clearRubber(state);
    clearGhost(state);
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

// ---- Sperre und Zustandsabfragen ------------------------------------------------------------------

/**
 * Schickt genau eine Nachricht und sperrt, bis die .NET-Methode zurückkehrt.
 *
 * Das Versprechen von invokeMethodAsync ist die Quittung: Blazor Server erfüllt es, wenn der Aufruf
 * abgeschlossen ist. Ein hängender Circuit lässt es nie auflösen und der Canvas bleibt gesperrt – das
 * ist die richtige Reihenfolge der Übel (gesperrt statt doppelt angelegt); den echt abgerissenen Fall
 * behandelt das Reconnect-Modal.
 */
function send(state, method, ...args) {
    if (state.busy || !state.dotNetRef) {
        return;
    }

    state.busy = true;
    state.dotNetRef
        .invokeMethodAsync(method, ...args)
        .finally(() => {
            state.busy = false;
        });
}

/** Ob der Graph bearbeitbar ist – gelesen, nicht gespeichert (das Attribut gehört C#). */
function editable(state) {
    return state.canvas.dataset.editable === "true";
}

/** Ob eine schreibende Geste gerade beginnen darf. */
function canEdit(state) {
    return !state.busy && editable(state);
}

// ---- Zeiger auf dem Canvas ------------------------------------------------------------------------

function onPointerDown(state, event) {
    if (event.button !== 0) {
        return;
    }

    // Der Ausgangs-Port MUSS vor dem Knoten geprüft werden: Er liegt innerhalb der Knotengruppe, die
    // Knotenprüfung würde die Verbindungsgeste sonst als Verschiebe-Zug verschlucken.
    const port = event.target.closest(".graph-port");
    if (port) {
        // Kein preventDefault und noch keine Capture – genau wie beim Knoten: Bis zur Schwelle bleibt
        // die Geste ein Klick, und der Klick ist der zeigerlose Weg zu verbinden (Blazors @onclick am
        // Port schaltet den Verbindungsmodus). Wer hier preventDefault ergänzt, nimmt ihn weg.
        if (canEdit(state)) {
            const node = port.closest(".graph-node");
            state.link = {
                pointerId: event.pointerId,
                fromId: node?.getAttribute("data-node-id"),
                node,
                startX: event.clientX,
                startY: event.clientY,
                moved: false,
            };
        }

        return;
    }

    // Ein Knoten wird gezogen – aber erst ab der Schwelle. Bis dahin bleibt die Geste ein Klick, damit
    // Blazors @onclick (die Auswahl) weiter funktioniert. Deshalb hier KEIN preventDefault und noch
    // keine Pointer-Capture.
    const node = event.target.closest(".graph-node");
    if (node) {
        // Verschieben ist auch am veröffentlichten Dialog erlaubt (ADR 0007) – hier gilt nur die
        // Sperre, nicht die Bearbeitbarkeit.
        if (state.busy) {
            return;
        }

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
    const link = state.link;
    if (link && link.pointerId === event.pointerId) {
        onLinkMove(state, link, event);
        return;
    }

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

function onPointerUp(state, event) {
    const link = state.link;
    if (link && link.pointerId === event.pointerId) {
        endLink(state, link, event);
        return;
    }

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
    releasePointer(state.canvas, event.pointerId);
}

// ---- Knoten verschieben (#102) --------------------------------------------------------------------

function onNodeMove(state, drag, event) {
    if (!drag.moved) {
        if (distance(drag, event) < DRAG_THRESHOLD) {
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

function endNodeDrag(state, drag, event) {
    state.drag = null;

    if (!drag.moved) {
        // Unter der Schwelle geblieben: Das war ein Klick. Nichts anfassen – der folgende click
        // trägt Blazors Auswahl.
        return;
    }

    state.canvas.classList.remove("is-dragging");
    drag.node.classList.remove("is-dragging");
    releasePointer(state.canvas, event.pointerId);

    swallowNextClick(state.canvas);
    send(state, "MoveNodeAsync", drag.nodeId, drag.x, drag.y);
}

// ---- Verbinden (#103) ----------------------------------------------------------------------------

function onLinkMove(state, link, event) {
    if (!link.moved) {
        if (distance(link, event) < DRAG_THRESHOLD) {
            return;
        }

        link.moved = true;
        state.canvas.classList.add("is-linking");
        state.canvas.setPointerCapture(event.pointerId);
    }

    const from = portCenter(state, link.node);
    const point = toUserSpace(state, event);
    if (!from || !point) {
        return;
    }

    // Bewusst eine Gerade und keine Bézier wie die fertigen Kanten: Die endgültige Form entsteht aus
    // GraphLayout.Route und hängt an Schichtung und Fächer – sie hier zu erraten wäre eine Vorschau,
    // die nach dem Commit anders aussieht.
    state.rubber?.setAttribute(
        "d",
        `M ${round(from.x)} ${round(from.y)} L ${round(point.x)} ${round(point.y)}`);

    highlightTarget(state, targetNodeAt(event));
    event.preventDefault();
}

function endLink(state, link, event) {
    state.link = null;

    if (!link.moved) {
        // Unter der Schwelle: Der folgende click schaltet den Verbindungsmodus (Tastaturpfad).
        return;
    }

    state.canvas.classList.remove("is-linking");
    releasePointer(state.canvas, event.pointerId);
    clearRubber(state);
    highlightTarget(state, null);
    swallowNextClick(state.canvas);

    const target = targetNodeAt(event);
    const targetId = target?.getAttribute("data-node-id");

    if (targetId) {
        send(state, "ConnectAsync", link.fromId, targetId);
        return;
    }

    // Ins Leere gezogen: Frage und Übergang in einem Zug – aber nur, wenn wirklich auf der
    // Zeichenfläche losgelassen wurde. Außerhalb ist die Geste ein Abbruch, keine Streufrage.
    const point = insideCanvas(state, event) ? toUserSpace(state, event) : null;
    if (point) {
        send(
            state,
            "ConnectToNewQuestionAsync",
            link.fromId,
            Math.max(0, Math.round(point.x - (state.options.nodeWidth / 2))),
            Math.max(0, Math.round(point.y - (state.options.nodeHeight / 2))));
    }
}

/** Die Mitte des Ausgangs-Ports in Nutzerkoordinaten – Unterkante-Mitte der Knotenbox. */
function portCenter(state, node) {
    const origin = node ? nodeOrigin(node) : null;

    return origin
        ? { x: origin.x + (state.options.nodeWidth / 2), y: origin.y + state.options.nodeHeight }
        : null;
}

/**
 * Der Knoten unter dem Zeiger.
 *
 * Über elementFromPoint und nicht über event.target: Nach setPointerCapture zeigt event.target auf das
 * Capture-Element, nicht auf das, was unter dem Zeiger liegt. Voraussetzung ist, dass Gummiband und
 * Vorschau `pointer-events: none` tragen – sonst trifft der Test sie selbst.
 */
function targetNodeAt(event) {
    return document.elementFromPoint(event.clientX, event.clientY)?.closest(".graph-node") ?? null;
}

function highlightTarget(state, node) {
    for (const previous of state.canvas.querySelectorAll(".graph-node.is-link-target")) {
        if (previous !== node) {
            previous.classList.remove("is-link-target");
        }
    }

    node?.classList.add("is-link-target");
}

function clearRubber(state) {
    state.rubber?.removeAttribute("d");
}

// ---- Baustein aus der Palette ablegen (#103) -----------------------------------------------------

function onPaletteDown(state, event) {
    if (event.button !== 0 || !canEdit(state)) {
        return;
    }

    const item = event.target.closest(".graph-palette-item");
    if (!item) {
        return;
    }

    // Wie überall: kein preventDefault, keine Capture. Bleibt die Geste unter der Schwelle, ist sie ein
    // Klick – und der legt die Frage über Blazors @onclick ohne Position an.
    state.spawn = {
        pointerId: event.pointerId,
        item,
        type: item.getAttribute("data-question-type"),
        startX: event.clientX,
        startY: event.clientY,
        moved: false,
    };
}

function onPaletteMove(state, event) {
    const spawn = state.spawn;
    if (!spawn || spawn.pointerId !== event.pointerId) {
        return;
    }

    if (!spawn.moved) {
        if (distance(spawn, event) < DRAG_THRESHOLD) {
            return;
        }

        spawn.moved = true;
        state.canvas.classList.add("is-spawning");
        spawn.item.setPointerCapture(event.pointerId);
    }

    // toUserSpace trägt auch außerhalb des SVG: Die Matrix ist global. Liegt der Zeiger neben der
    // Fläche, schneidet das SVG die Vorschau einfach ab – das ist die gewünschte Rückmeldung.
    const point = toUserSpace(state, event);
    if (!point || !state.ghost) {
        return;
    }

    state.ghost.setAttribute("x", round(point.x - (state.options.nodeWidth / 2)));
    state.ghost.setAttribute("y", round(point.y - (state.options.nodeHeight / 2)));
    state.ghost.setAttribute("width", round(state.options.nodeWidth));
    state.ghost.setAttribute("height", round(state.options.nodeHeight));
    event.preventDefault();
}

function onPaletteUp(state, event) {
    const spawn = state.spawn;
    if (!spawn || spawn.pointerId !== event.pointerId) {
        return;
    }

    state.spawn = null;

    if (!spawn.moved) {
        return;
    }

    state.canvas.classList.remove("is-spawning");
    releasePointer(spawn.item, event.pointerId);
    clearGhost(state);

    // Der Riegel muss auf der PALETTE sitzen, nicht auf dem Canvas: Der click nach dem Zug feuert am
    // Palette-Eintrag. Ohne ihn legte jeder Zug zusätzlich die Klick-Frage an – zwei Fragen aus einer
    // Geste, genau die Falle, die dieses Issue benennt.
    swallowNextClick(state.palette);

    const point = insideCanvas(state, event) ? toUserSpace(state, event) : null;
    if (!point || !spawn.type) {
        return;
    }

    send(
        state,
        "CreateQuestionAtAsync",
        spawn.type,
        Math.max(0, Math.round(point.x - (state.options.nodeWidth / 2))),
        Math.max(0, Math.round(point.y - (state.options.nodeHeight / 2))));
}

function clearGhost(state) {
    if (!state.ghost) {
        return;
    }

    for (const name of ["x", "y", "width", "height"]) {
        state.ghost.removeAttribute(name);
    }
}

// ---- Gemeinsame Helfer ---------------------------------------------------------------------------

/**
 * Verschluckt das click, das der Browser nach dem Loslassen noch feuert. Ohne diesen Riegel löste jeder
 * Zug zusätzlich die Klick-Aktion des Elements aus – eine zweite Nachricht, die niemand ausgelöst hat.
 *
 * Der Timeout räumt den Horcher auch dann ab, wenn ausnahmsweise kein click folgt (etwa weil der Zeiger
 * das Fenster verlassen hat). Er läuft nach dem click derselben Geste – sonst bliebe der Riegel liegen
 * und schluckte irgendwann einen echten Klick.
 */
function swallowNextClick(element) {
    if (!element) {
        return;
    }

    const swallow = (event) => {
        event.stopPropagation();
        event.preventDefault();
    };

    element.addEventListener("click", swallow, { capture: true, once: true });
    setTimeout(() => element.removeEventListener("click", swallow, { capture: true }), 0);
}

function distance(gesture, event) {
    return Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY);
}

function insideCanvas(state, event) {
    const box = state.canvas.getBoundingClientRect();

    return event.clientX >= box.left
        && event.clientX <= box.right
        && event.clientY >= box.top
        && event.clientY <= box.bottom;
}

function releasePointer(element, pointerId) {
    if (element?.hasPointerCapture(pointerId)) {
        element.releasePointerCapture(pointerId);
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
    if (event.key === " " && event.target.closest(".graph-node-card, .graph-loop-label, .graph-port")) {
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
