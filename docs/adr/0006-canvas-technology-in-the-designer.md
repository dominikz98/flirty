# ADR 0006 – Canvas technology in the designer: in-house SVG instead of a diagram library

- **Status:** Accepted
- **Context issue:** #100 – Spike: canvas technology for the visual dialog designer
  (in-house SVG vs. Blazor.Diagrams); frame: #99 – EPIC 11 Visual graph designer (canvas)
- **Affected:** `src/Flirty.Designer` (EPIC 11, stages 1–5), `Directory.Packages.props`

## Context

EPIC 11 gives the designer a **canvas view**: questions as nodes, transitions as edges, loops as a range.
Until then the surface is a stack of forms – complete, but the flow of a dialog is not readable from it.

The hard constraint is the interactivity variant: `Flirty.Designer` is a Blazor Web App with
`AddInteractiveServerComponents()` (`src/Flirty.Designer/DesignerApp.cs`), i.e. **Blazor Server**. Every
Blazor event is therefore a SignalR round-trip. If dragging a node runs in C#, **every pointer move**
costs a network round-trip time – at two-digit node counts and a WAN circuit the page is unusable. This
decision therefore falls **before** the first line of canvas code and is expensive to revise later: it
determines whether node contents are Razor components or library widgets, where pan/zoom/selection come
from and how the E2E tests take hold.

The choice was between an **in-house build** (SVG in Razor + a collocated JS module) and
**[Blazor.Diagrams](https://github.com/Blazor-Diagrams/Blazor.Diagrams)** (`Z.Blazor.Diagrams`, MIT) –
the only serious free candidate. It explicitly advertises "95 % C#/Blazor, JS only where needed" and
server suitability. That was exactly what had to be **measured**, not believed: its documentation and
demo site runs on **WebAssembly** (boot manifest checked), i.e. with in-process pointer events without a
network. A fluid demo there says nothing about a server circuit.

## Decision

The canvas is **built in-house**: SVG in Razor components, pointer interaction in a collocated
ES module (`*.razor.js`, pattern like `Components/Layout/ReconnectModal.razor.js`). `Z.Blazor.Diagrams`
is not adopted.

Independent of that, the architectural commitment for every canvas gesture is:

- **Dragging runs entirely client-side** (SVG `transform`, incident edges drawn along in the JS).
  Between `pointerdown` and `pointerup` **no** message goes to the server.
- **Only the release calls a command** – exactly one message per gesture.
- The canvas sets an **explicit readiness signal** (`data-canvas-ready`) as soon as the JS module is
  bound. The retry pattern `InteractWhenReadyAsync` from `tests/Flirty.E2E` does not carry here:
  it presupposes idempotent actions, a repeated drag would move twice.

Measured with two throwaway prototypes over the **same** graph (30 nodes, 45 edges, cycle
17→9, one question with four outgoing edges) against an artificially throttled circuit. The code lives
on the branch `spike/dz/100` and is deliberately **not merged** – it is the provenance of the numbers,
not product code.

**Result** (median over 7 gestures per candidate, each 300 px in 30 steps of 16 ms; a fresh circuit per
gesture, candidates alternating, one unmeasured warm-up gesture per side):

| Candidate | Lag behind the pointer | Standstill after release | Messages ↑/↓ per gesture | Payload per gesture |
|---|---:|---:|---:|---:|
| **In-house SVG** | **0 px** (0–0) | **0.3 ms** (0.1–0.4) | **2 / 2** | **688 B** |
| Blazor.Diagrams 3.0.4.1 | 40 px ≈ **64 ms** (40–50 px) | **168 ms** (166–231) | **68 / 68** | **50,309 B** |

Constraints of the measurement: measured circuit round-trip time **163 ms** (in-house) and **163 ms**
(library) – the throttling hit both equally; 31 `pointermove` events actually triggered in the browser in
both cases (Chromium coalesces them onto the animation frame, which caps the library's round-trips from
above and is thus to its benefit). Breakdown per gesture – in-house:
`BeginInvokeDotNetFromJS ×1`, `JS.EndInvokeDotNet ×1`, `JS.RenderBatch ×1`, `OnRenderCompleted ×1`.
Library: `BeginInvokeDotNetFromJS ×37`, `JS.EndInvokeDotNet ×37`, `JS.RenderBatch ×31`,
`OnRenderCompleted ×31`.

Machine: AMD Ryzen 7 5800X (8 cores), 64 GB, Windows 11 Enterprise 10.0.26100, .NET SDK 10.0.204,
Playwright 1.61.0 / Chromium Headless Shell, loopback plus a TCP delay proxy 2 × 75 ms.

## Discarded alternatives

- **Blazor.Diagrams (`Z.Blazor.Diagrams` 3.0.4.1).** Discarded because of its **drag path**, not because
  of license, target framework or package hygiene – those are impeccable: MIT, native `lib/net10.0`, a
  single foreign transitive dependency (`SvgPathProperties 1.1.2`) without an advisory, and the designer
  builds with the reference under `TreatWarningsAsErrors=true` with **0 warnings** (re-measured). Since
  `Flirty.Designer` is `IsPackable=false`, a dependency there would not have burdened any NuGet package
  anyway. The reason for exclusion lies in the architecture: `Components/DiagramCanvas.razor` wires
  `@onpointermove="OnPointerMove"` as a **C# handler**, and the shipped `wwwroot/script.js` (48
  lines, read in person) contains exclusively `getBoundingClientRect`, `ResizeObserver`,
  `MutationObserver` and a `scroll` listener – **not a single pointer or drag handler**. There is
  therefore no client-side drag path one could switch on; no throttling or coalescing happens
  anywhere. The consequence stood above in the table: **34 times as many messages and 73 times as much
  payload per gesture**, a visible lag of **around 40 px (≈ 64 ms)** behind the pointer, and the
  node stands still only a round-trip after release. That is no fault of the library – for
  WebAssembly, for which it is expressly optimized, the approach is right. For a server circuit
  it is not.
- **Retrofitting Blazor.Diagrams with a client-side drag (fork or contribution).** Obvious, because the
  rest of the library (router, path generators, groups, virtualization) is exactly what we would
  otherwise build ourselves. Ruled out on the reach of the intervention and on the maintenance state: the
  drag hangs on the behavior chain (`SelectionBehavior` → `DragMovablesBehavior`), which works throughout
  on server-side models – a client-side path would be no patch, but a second truth over
  node positions. On top of that: 108 open issues, effectively one maintainer, last commit 2026-03-02, and
  the performance review the maintainer **himself** opened in 2022 (#217, "so many JS calls that can most
  probably be batched") is untouched. What does not work there today foreseeably will not work
  tomorrow either.
- **Syncfusion / MindFusion Diagram.** Functionally the most advanced of all candidates – and both
  **commercially licensed**. Flirty is an MIT repo that publishes two NuGet packages; a
  license obligation in the designer would be a hurdle for anyone who wants to start the designer, and it
  would not be defused by `IsPackable=false` either. Not part of the spike, ruled out on principle.
- **Embedding a JS diagram library** (jsPlumb, Cytoscape.js, React Flow etc.) and driving it via interop.
  The drag problem would thereby be solved – the costs lie elsewhere: the repo deliberately has **no**
  Node toolchain (no `package.json`, no bundler), node contents would then be JS templates instead of
  Razor components, and the graph state would live twice. Embedded ready-made bundles without a build
  chain would additionally have to be maintained past `MapStaticAssets`/CSP.
- **A read-only view without dragging** (auto-layout, no interaction). Would have deferred the decision,
  not answered it: for a pure view any technology is suitable, and the choice would fall again at the
  first editing feature – then with a finished investment in the wrong direction.

## Consequences

**Positive**

- Dragging costs the circuit **one** message instead of one per frame, and the node follows the pointer
  within a frame – regardless of the round-trip time. That is the property EPIC 11 hangs on.
- **No new dependency.** The `Directory.Packages.props` entry from the spike is dropped; the package
  list stays as it is.
- Node contents (type badge, required marker, warning marker, trigger chips) are **Razor components** and
  share classes and contrast rules with the rest of the designer (`wwwroot/app.css`). After the
  contrast findings from #95 that is no side aspect.
- Accessibility stays in our own hands: nodes are focusable SVG elements, not library widgets with a
  foreign focus model.

**Negative**

- **Pan/zoom, edge routing, selection and snapping are in-house.** A rough estimate based on the
  prototypes: pan/zoom in the JS module around 100 lines, straight edges with avoidance at node edges
  around 150, multi-selection and snapping around 50 each. Plus a deterministic auto-layout
  (Sugiyama-light, estimated ~150 lines) – necessary because without a saved position something sensible
  must arise and because E2E selectors need stable coordinates.
- **Two languages per gesture.** The truth over node positions lies on release with the server, in between
  in the DOM. Whoever changes the JS module must pull the C# state along – otherwise the next render sets
  the node back. The prototype shows the spot: `OnDragEnd` writes the position forward, otherwise the
  old value wins.
- What the spike learned **in passing** stays an in-house obligation: edges must not cover the node.
  In Blazor.Diagrams the edges lie in the same SVG layer *after* the nodes and carry a 12 px wide
  invisible hit path – a `pointerdown` on the node center hit an edge there instead of the node. The
  in-house build draws edges **before** the nodes; that is a commitment, not a coincidence.

**Open**

- The **layout persistence** is expressly not decided here. It is the subject of stage 2
  (#102) and gets its own ADR – including the rationale for why a layout command deliberately does not
  fall under `DialogEditGuard`.
- Whether the canvas still carries beyond around 50 nodes is unmeasured. The bar was set at 30; the goal
  is dialogs on the order of real questionnaires, not hundred-node graphs.
- Should Blazor.Diagrams ever gain a client-side drag path, this decision is to be re-examined – then via
  a **new** ADR, not by rewriting this one.

Details: [DESIGNER.md](../DESIGNER.md).
