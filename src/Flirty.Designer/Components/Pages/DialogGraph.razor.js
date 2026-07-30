// Panning and zooming the graph canvas (#101), dragging individual nodes (#102) as well as the
// editing gestures: dropping building blocks from the palette and connecting nodes (#103).
//
// Why here and not in C#: the designer is Blazor *Server*. Every Blazor event is a
// SignalR round-trip – if moving ran in C#, every pointer step would cost a network round-trip time.
// The spike for #100 measured this (40 px lag behind the pointer, 68 messages per gesture);
// ADR 0006 turns it into a promise: between pointerdown and pointerup NO message goes to the
// server. It applies just the same for node drag, connection gesture and palette drop – the only place that
// calls invokeMethodAsync is the helper function send(), and it is called exclusively from a
// pointerup. Whoever adds a call in a pointermove here breaks an
// acceptance criterion.
//
// The truth about the view therefore lives in the DOM, not in the C# state. That is why
// DialogGraph.razor never renders a transform on .graph-viewport itself: the next re-render
// would otherwise reset pan and zoom. For a node, on the other hand, the transform *is* C# state –
// so the drag writes it only provisionally and the commit lets C# render the same value.
//
// Conversely, for the state that belongs to C#: data-editable and data-busy on the <svg> are rendered by C#, the
// module only READS them – and freshly on every gesture, not as an option frozen at attach.
//
// GESTURES ARE NOT IDEMPOTENT. A doubly triggered drop creates two questions, a doubled
// connection gesture two transitions. That is why every message runs through send(): it locks until the
// associated .NET method returns. The promise of invokeMethodAsync IS the receipt – a
// second back channel is not needed and could be forgotten.

const states = new WeakMap();

/** From how many screen pixels a click becomes a drag. */
const DRAG_THRESHOLD = 4;

/**
 * Binds pointer and wheel control to a canvas.
 * @param {SVGSVGElement} canvas The SVG element.
 * @param {object} dotNetRef Back channel to C# – target of all gesture messages on release.
 * @param {{minZoom: number, maxZoom: number, zoomStep: number, nodeWidth: number, nodeHeight: number}} options
 *        Limits and dimensions from GraphMetrics – never hardcoded in the JS, otherwise there are two truths.
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
        // C# renders the two placeholders; this module only fills their geometry (see module header).
        rubber: canvas.querySelector(".graph-rubber"),
        ghost: canvas.querySelector(".graph-ghost"),
        // The palette sits next to the canvas, not inside it – so its pointer events never reach it.
        // Searched for rather than passed as a parameter: both are children of the same .graph-layout, and an
        // additional ElementReference parameter would have to be threaded through the GraphPalette component
        // without gaining anything.
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

    // Explicit readiness signal for the E2E suite. The retry pattern InteractWhenReadyAsync
    // does not carry for a canvas: it presupposes idempotent actions, a repeated drag
    // would move twice and a repeated connection gesture would create two transitions.
    canvas.setAttribute("data-canvas-ready", "true");
}

/**
 * Releases the binding again.
 * @param {SVGSVGElement} canvas The SVG element.
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
 * Zooms around the center of the view – the toolbar's path.
 * @param {SVGSVGElement} canvas The SVG element.
 * @param {number} factor The factor (> 1 zooms in).
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
 * Resets pan and zoom.
 * @param {SVGSVGElement} canvas The SVG element.
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
 * Brings a node into the visible area – for keyboard operation.
 * @param {SVGSVGElement} canvas The SVG element.
 * @param {string} nodeId The question id.
 */
export function focusNode(canvas, nodeId) {
    const node = canvas?.querySelector(`[data-node-id="${nodeId}"] .graph-node-card`);
    if (node) {
        node.focus();
    }
}

// ---- Lock and state queries -----------------------------------------------------------------------

/**
 * Sends exactly one message and locks until the .NET method returns.
 *
 * The promise of invokeMethodAsync is the receipt: Blazor Server fulfills it when the call
 * is complete. A hanging circuit never lets it resolve and the canvas stays locked – that
 * is the right ordering of evils (locked instead of created twice); the truly severed case
 * is handled by the reconnect modal.
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

/** Whether the graph is editable – read, not stored (the attribute belongs to C#). */
function editable(state) {
    return state.canvas.dataset.editable === "true";
}

/** Whether a writing gesture may begin right now. */
function canEdit(state) {
    return !state.busy && editable(state);
}

// ---- Pointer on the canvas ------------------------------------------------------------------------

function onPointerDown(state, event) {
    if (event.button !== 0) {
        return;
    }

    // The source port MUST be checked before the node: it lies inside the node group, the
    // node check would otherwise swallow the connection gesture as a move drag.
    const port = event.target.closest(".graph-port");
    if (port) {
        // No preventDefault and no capture yet – just like for the node: until the threshold
        // the gesture stays a click, and the click is the pointerless way to connect (Blazor's @onclick on the
        // port switches the connection mode). Whoever adds preventDefault here takes it away.
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

    // A node is dragged – but only from the threshold on. Until then the gesture stays a click, so that
    // Blazor's @onclick (the selection) keeps working. That is why NO preventDefault here and no
    // pointer capture yet.
    const node = event.target.closest(".graph-node");
    if (node) {
        // Moving is allowed even on a published dialog (ADR 0007) – here only the
        // lock applies, not editability.
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

    // If the gesture hits another operable element, it belongs to it – and COMPLETELY: no
    // preventDefault(), otherwise the following click fizzles and with it Blazor's @onclick.
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

// ---- Moving nodes (#102) --------------------------------------------------------------------------

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

    // Never beyond the origin: the viewBox begins at 0 on the left, a negative value would push the
    // node off the drawing surface – and the command rejects it anyway.
    drag.x = Math.max(0, Math.round(point.x - drag.grabX));
    drag.y = Math.max(0, Math.round(point.y - drag.grabY));

    drag.node.setAttribute("transform", `translate(${drag.x} ${drag.y})`);
    event.preventDefault();
}

function beginNodeDrag(state, drag, event) {
    drag.moved = true;

    state.canvas.classList.add("is-dragging");
    drag.node.classList.add("is-dragging");

    // The adjacent edges are dimmed instead of recomputed live: their path arises in C#
    // (GraphLayout.Route) and is tested there – rebuilding it in the browser would be a second source
    // for the same geometry. After the commit C# draws the exact paths.
    for (const edge of incidentEdges(state.canvas, drag.nodeId)) {
        edge.classList.add("is-stale");
    }

    state.canvas.setPointerCapture(event.pointerId);
}

function endNodeDrag(state, drag, event) {
    state.drag = null;

    if (!drag.moved) {
        // Stayed under the threshold: that was a click. Touch nothing – the following click
        // carries Blazor's selection.
        return;
    }

    state.canvas.classList.remove("is-dragging");
    drag.node.classList.remove("is-dragging");
    releasePointer(state.canvas, event.pointerId);

    swallowNextClick(state.canvas);
    send(state, "MoveNodeAsync", drag.nodeId, drag.x, drag.y);
}

// ---- Connecting (#103) ---------------------------------------------------------------------------

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

    // Deliberately a straight line and not a Bézier like the finished edges: the final shape arises from
    // GraphLayout.Route and depends on layering and fanning – guessing it here would be a preview
    // that looks different after the commit.
    state.rubber?.setAttribute(
        "d",
        `M ${round(from.x)} ${round(from.y)} L ${round(point.x)} ${round(point.y)}`);

    highlightTarget(state, targetNodeAt(event));
    event.preventDefault();
}

function endLink(state, link, event) {
    state.link = null;

    if (!link.moved) {
        // Under the threshold: the following click switches the connection mode (keyboard path).
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

    // Dragged into the void: question and transition in one drag – but only if it was really released
    // on the drawing surface. Outside, the gesture is a cancel, not a stray question.
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

/** The center of the source port in user coordinates – bottom-edge center of the node box. */
function portCenter(state, node) {
    const origin = node ? nodeOrigin(node) : null;

    return origin
        ? { x: origin.x + (state.options.nodeWidth / 2), y: origin.y + state.options.nodeHeight }
        : null;
}

/**
 * The node under the pointer.
 *
 * Via elementFromPoint and not via event.target: after setPointerCapture, event.target points to the
 * capture element, not to what lies under the pointer. The precondition is that the rubber band and
 * preview carry `pointer-events: none` – otherwise the test hits them itself.
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

// ---- Dropping a building block from the palette (#103) -------------------------------------------

function onPaletteDown(state, event) {
    if (event.button !== 0 || !canEdit(state)) {
        return;
    }

    const item = event.target.closest(".graph-palette-item");
    if (!item) {
        return;
    }

    // As everywhere: no preventDefault, no capture. If the gesture stays under the threshold, it is a
    // click – and that creates the question via Blazor's @onclick without a position.
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

    // toUserSpace carries outside the SVG too: the matrix is global. If the pointer lies beside the
    // surface, the SVG simply clips the preview – that is the desired feedback.
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

    // The lock must sit on the PALETTE, not on the canvas: the click after the drag fires on the
    // palette entry. Without it every drag would additionally create the click question – two questions from one
    // gesture, exactly the trap this issue names.
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

// ---- Shared helpers ------------------------------------------------------------------------------

/**
 * Swallows the click that the browser still fires after release. Without this lock every
 * drag would additionally trigger the element's click action – a second message that no one triggered.
 *
 * The timeout also clears the listener if exceptionally no click follows (e.g. because the pointer
 * left the window). It runs after the click of the same gesture – otherwise the lock would remain
 * and eventually swallow a real click.
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

/** Reads the top-left corner of a node from its transform. */
function nodeOrigin(node) {
    const matrix = node.transform?.baseVal?.consolidate()?.matrix;
    return matrix ? { x: matrix.e, y: matrix.f } : null;
}

/**
 * Converts a pointer point into user coordinates of the viewport.
 *
 * Via getScreenCTM instead of via state.scale: the matrix also contains the scaling that the viewBox
 * produces relative to the CSS width of the SVG. Dividing only by state.scale omits this
 * factor – the node would then run faster or slower than the pointer depending on the window width.
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
    // The space bar activates a focused node – without this lock it additionally scrolls the
    // page. In Blazor @onkeydown:preventDefault could only be set statically, which would also hit Tab.
    if (event.key === " " && event.target.closest(".graph-node-card, .graph-loop-label, .graph-port")) {
        event.preventDefault();
    }
}

/**
 * Zooms around an anchor point: the point under the pointer stays put, everything else scales around
 * it. Without an anchor the graph jumps away on every wheel step.
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
