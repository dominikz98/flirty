// SPIKE #100 (Wegwerf, wird NICHT gemergt) – Kandidat A: das Ziehen läuft vollständig im Browser.
//
// Der springende Punkt: Zwischen pointerdown und pointerup geht KEINE Nachricht an den Server. Der
// Knoten wird per SVG-transform verschoben, die inzidenten Kanten werden gleich mit neu gezeichnet –
// letzteres ist keine Kür, sondern Voraussetzung für einen fairen Vergleich: Blazor.Diagrams rechnet
// beim Ziehen ebenfalls alle anhängenden Pfade neu.
//
// Erst das Loslassen ruft genau einmal in C# (OnDragEnd) und schreibt die Position in den
// Server-Zustand fort.

const states = new WeakMap();

export function attach(canvas, dotNetRef, nodeWidth, nodeHeight) {
    if (!canvas || states.has(canvas)) {
        return;
    }

    const nodes = new Map();
    for (const element of canvas.querySelectorAll(".spike-node")) {
        const index = Number(element.dataset.index);
        const transform = element.getAttribute("transform");
        const [x, y] = transform.slice("translate(".length, -1).split(" ").map(Number);
        nodes.set(index, { element, x, y, edges: [] });
    }

    for (const path of canvas.querySelectorAll(".spike-edge")) {
        const from = Number(path.dataset.from);
        const to = Number(path.dataset.to);
        const edge = { path, from, to };
        nodes.get(from)?.edges.push(edge);
        nodes.get(to)?.edges.push(edge);
    }

    const state = {
        canvas,
        dotNetRef,
        nodes,
        halfWidth: nodeWidth / 2,
        halfHeight: nodeHeight / 2,
        drag: null,
    };

    state.onPointerDown = (event) => onPointerDown(state, event);
    state.onPointerMove = (event) => onPointerMove(state, event);
    state.onPointerUp = (event) => onPointerUp(state, event);

    canvas.addEventListener("pointerdown", state.onPointerDown);
    canvas.addEventListener("pointermove", state.onPointerMove);
    canvas.addEventListener("pointerup", state.onPointerUp);
    canvas.addEventListener("pointercancel", state.onPointerUp);

    states.set(canvas, state);

    // Explizites Bereitschaftssignal. Für einen Canvas trägt das Wiederholmuster aus
    // DesignerE2ETests.InteractWhenReadyAsync nicht: Eine wiederholte Zieh-Geste verschöbe doppelt.
    canvas.setAttribute("data-canvas-ready", "true");
}

export function detach(canvas) {
    const state = states.get(canvas);
    if (!state) {
        return;
    }

    canvas.removeEventListener("pointerdown", state.onPointerDown);
    canvas.removeEventListener("pointermove", state.onPointerMove);
    canvas.removeEventListener("pointerup", state.onPointerUp);
    canvas.removeEventListener("pointercancel", state.onPointerUp);
    canvas.removeAttribute("data-canvas-ready");
    states.delete(canvas);
}

function onPointerDown(state, event) {
    const element = event.target.closest(".spike-node");
    if (!element) {
        return;
    }

    const node = state.nodes.get(Number(element.dataset.index));
    if (!node) {
        return;
    }

    state.drag = {
        node,
        pointerId: event.pointerId,
        originX: node.x,
        originY: node.y,
        startClientX: event.clientX,
        startClientY: event.clientY,
    };

    element.classList.add("is-dragging");
    state.canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
}

function onPointerMove(state, event) {
    const drag = state.drag;
    if (!drag || drag.pointerId !== event.pointerId) {
        return;
    }

    drag.node.x = drag.originX + (event.clientX - drag.startClientX);
    drag.node.y = drag.originY + (event.clientY - drag.startClientY);
    render(state, drag.node);
    event.preventDefault();
}

function onPointerUp(state, event) {
    const drag = state.drag;
    if (!drag || drag.pointerId !== event.pointerId) {
        return;
    }

    state.drag = null;
    drag.node.element.classList.remove("is-dragging");
    if (state.canvas.hasPointerCapture(event.pointerId)) {
        state.canvas.releasePointerCapture(event.pointerId);
    }

    // Die eine Nachricht der ganzen Geste.
    state.dotNetRef.invokeMethodAsync(
        "OnDragEnd", Number(drag.node.element.dataset.index), drag.node.x, drag.node.y);
}

function render(state, node) {
    node.element.setAttribute("transform", `translate(${round(node.x)} ${round(node.y)})`);

    for (const edge of node.edges) {
        const from = state.nodes.get(edge.from);
        const to = state.nodes.get(edge.to);
        edge.path.setAttribute(
            "d",
            `M ${round(from.x + state.halfWidth)} ${round(from.y + state.halfHeight)}`
            + ` L ${round(to.x + state.halfWidth)} ${round(to.y + state.halfHeight)}`);
    }
}

function round(value) {
    return Math.round(value * 100) / 100;
}
