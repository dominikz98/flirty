using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Flirty.E2E;

/// <summary>
/// Playwright E2E of the Blazor designer (#46) against a real, in-process hosted Kestrel
/// (<see cref="DesignerAppFixture"/>). Covers the issue's acceptance criterion – create dialog →
/// branching → loop → save – and then plays the dialog configured this way through with the
/// test runner (#43) and thus the real engine. If no Playwright browsers are installed, the tests
/// skip themselves (<see cref="SkippableFactAttribute"/>) – install e.g. via
/// <c>pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium</c>.
/// </summary>
/// <remarks>
/// The graph built here deliberately mirrors <c>DesignerTestHost.ArrangeLoopDialogAsync</c> from
/// <c>tests/Flirty.Tests</c>: <c>position</c> → <c>more</c>, on <c>more == "yes"</c> back to
/// <c>position</c>, otherwise on to <c>summary</c>; loop <c>position_list</c>. So service tests
/// and E2E describe the same dialog – once via the commands, once through the UI.
/// </remarks>
public sealed class DesignerE2ETests : IClassFixture<DesignerAppFixture>
{
    private static readonly LocatorAssertionsToContainTextOptions SlowContains = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveTextOptions SlowText = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveCountOptions SlowCount = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveValueOptions SlowValue = new() { Timeout = 15_000 };

    // The question select fields list the questions in dialog order; at index 0 sits the empty entry
    // ("— none —" or "— choose —"). The indices therefore apply to the graph built below – that they
    // hit the expected questions is verified by SetStartQuestionAsync.
    private const int PositionOption = 1;
    private const int MoreOption = 2;
    private const int SummaryOption = 3;

    private static readonly Regex DialogUrl = new(@"/dialogs/[0-9a-fA-F-]{36}$");
    private static readonly Regex QuestionUrl = new(@"/questions/[0-9a-fA-F-]{36}$");
    private static readonly Regex TransitionUrl = new(@"/transitions/[0-9a-fA-F-]{36}$");

    private readonly DesignerAppFixture _fixture;

    /// <summary>Initializes the test with the shared designer host.</summary>
    /// <param name="fixture">The in-process hosted designer.</param>
    public DesignerE2ETests(DesignerAppFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The acceptance criterion from #46: create a dialog with its questions, transitions and loop
    /// completely through the UI, publish it – and prove via a reload that everything was really saved
    /// (after reloading, every field comes from the database).
    /// </summary>
    [SkippableFact]
    public async Task Dialog_mit_Branching_und_Schleife_anlegen_und_speichern()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1 .badge")).ToHaveTextAsync("Published", SlowText);

        // Reload: the server re-renders the page completely from the database – only that proves that
        // questions, transitions, condition, loop and publish status are persisted.
        await page.ReloadAsync();

        await Assertions.Expect(page.Locator("h1 .badge-published")).ToHaveTextAsync("Published", SlowText);
        await Assertions.Expect(Section(page, "Questions").Locator("tbody tr")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(Section(page, "Transitions (branching)").Locator("tbody tr"))
            .ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(Section(page, "Transitions (branching)"))
            .ToContainTextAsync("more == \"yes\"", SlowContains);

        // The loop carries the badge "Loop" instead of "n warning(s)": the LoopAnalyzer thus finds a
        // reachable exit – the cycle is not an infinite loop.
        var loopRow = Section(page, "Loops").Locator("tbody tr").Filter(new() { HasText = "position_list" });
        await Assertions.Expect(loopRow.Locator(".badge")).ToHaveTextAsync("Loop", SlowText);
    }

    /// <summary>
    /// The counter-check to the configuration: play the same – deliberately <b>unpublished</b> – dialog
    /// through with the test runner (#43) and thus the real engine. Two iterations of the loop, then
    /// exit and completion.
    /// </summary>
    [SkippableFact]
    public async Task Testlauf_spielt_die_Schleife_mit_der_echten_Engine_durch()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Test run" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/test$"));

        // Exact: otherwise the name would also match the "Start new run" of the result card. A second
        // click would only begin another run and discard the first – harmless.
        await InteractWhenReadyAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Start run", Exact = true }).ClickAsync(),
            () => Assertions.Expect(CurrentStep(page)).ToContainTextAsync("Which role?", QuickContains));

        // First iteration, back-jump via "Yes", second iteration, exit via "No".
        await AnswerTextAsync(page, "Backend");
        await ChooseAsync(page, "Yes");
        await AnswerTextAsync(page, "Frontend");
        await ChooseAsync(page, "No");
        await AnswerTextAsync(page, "done");

        await Assertions.Expect(CurrentStep(page)).ToContainTextAsync("Dialog completed", SlowContains);

        // The history shows the second iteration – the loop thus really collected instead of overwriting
        // the first answer …
        await Assertions.Expect(page.Locator(".transcript")).ToContainTextAsync("Iteration 2", SlowContains);

        // … and the expression context shows both values under the collection key.
        var collection = Section(page, "Expression context").Locator("tbody tr").Filter(new() { HasText = "position_list" });
        await Assertions.Expect(collection).ToContainTextAsync("Backend", SlowContains);
        await Assertions.Expect(collection).ToContainTextAsync("Frontend", SlowContains);
    }

    /// <summary>
    /// Smoke test of the graph view (#101): The canvas binds its JS module, draws the graph and leads
    /// via the selection into the existing question editor. The full canvas coverage remains stage 5
    /// (#105) – here it is about the proof that the page lives in the browser at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test waits on <c>data-canvas-ready</c> instead of using <c>InteractWhenReadyAsync</c>.
    /// The retry pattern presupposes <b>idempotent</b> actions – that does not hold for a canvas:
    /// a repeated drag would move twice, a repeated zoom step would zoom twice. The attribute is set by
    /// the JS module as soon as it is bound; it is therefore a real signal instead of a guess (ADR 0006).
    /// </para>
    /// <para>
    /// It is deliberately the <b>first</b> <c>data-</c> attribute in the designer – the rest of the suite
    /// addresses via role, heading, field <c>id</c> and CSS class.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Ansicht_zeigt_den_Ablauf_und_fuehrt_in_den_Frage_Editor()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);
        await CreateQuestionTriggerAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));

        // The readiness signal of the canvas – only after it is an interaction reliable.
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // Three questions, three transitions – the same graph the list view shows.
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(3, SlowCount);

        // The markers that make the flow readable: entry, terminal and the loop frame.
        await Assertions.Expect(page.Locator(".graph-node.is-start")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-terminal")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-loop-label")).ToContainTextAsync("position_list", SlowContains);

        // The trigger hangs as a chip on exactly the question after which it fires – not on all.
        var triggerNode = page.Locator(".graph-node").Filter(new() { HasText = "summary" });
        await Assertions.Expect(triggerNode.Locator(".chip")).ToContainTextAsync("hooks.test", SlowContains);
        await Assertions.Expect(page.Locator(".graph-node .chip")).ToHaveCountAsync(1, SlowCount);

        // Selection opens the inspector. The card is addressed via its class, not via the role: since
        // #103 a node carries a second button (the source port), and GetByRole(Button) would therefore
        // be a strict-mode violation.
        await page.Locator(".graph-node").Filter(new() { HasText = "position" }).First
            .Locator(".graph-node-card").ClickAsync();
        var inspector = page.Locator(".graph-inspector");
        await Assertions.Expect(inspector).ToContainTextAsync("Entry question", SlowContains);

        // … and the inspector leads into the existing editor instead of rebuilding it.
        await inspector.GetByRole(AriaRole.Button, new() { Name = "Edit question →" }).ClickAsync();
        await page.WaitForURLAsync(QuestionUrl);
        await Assertions.Expect(page.Locator("#key")).ToHaveValueAsync("position", SlowValue);
    }

    /// <summary>
    /// Smoke test of the layout persistence (#102): A node is dragged, the position survives the
    /// reload – i.e. the complete rebuild of the page from the database – and "Reset layout" restores
    /// the position of the auto-layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <b>published</b> dialog is dragged. Thereby the test also proves the stage's core promise:
    /// where every graph change returns 409, moving goes through (ADR 0007) – visible here in that no
    /// error message appears and the position is really saved.
    /// </para>
    /// <para>
    /// This test too waits on <c>data-canvas-ready</c> instead of using <c>InteractWhenReadyAsync</c> –
    /// a repeated drag would move twice. The drag runs over several <c>Mouse.MoveAsync</c> steps: a
    /// single jump produces exactly one <c>pointermove</c>, which would exceed the 4-px threshold but
    /// would not look like a real gesture.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Knoten_verschieben_ueberlebt_den_Reload()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1 .badge")).ToHaveTextAsync("Published", SlowText);

        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        var node = page.Locator(".graph-node").Filter(new() { HasText = "summary" });
        var pinnedNode = page.Locator(".graph-node.is-pinned").Filter(new() { HasText = "summary" });
        var before = await TransformOfAsync(node);

        // Before the drag nothing is pinned – otherwise the reload below would check a position that
        // already held.
        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Reset layout" })).ToBeHiddenAsync();

        await DragByAsync(page, node, 180, 120);

        // The node now carries its own position – the marking comes from the freshly built model, i.e.
        // only after the server took over the drag.
        await Assertions.Expect(pinnedNode).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        var afterDrag = await TransformOfAsync(node);
        Assert.NotEqual(before, afterDrag);

        // The proof: after the reload the server renders the node from the database.
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });
        await Assertions.Expect(pinnedNode).ToHaveCountAsync(1, SlowCount);
        Assert.Equal(afterDrag, await TransformOfAsync(node));

        // Reset discards the row – afterwards the node lies again where the auto-layout had it.
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset layout" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes, reset" }).ClickAsync();

        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(0, SlowCount);
        Assert.Equal(before, await TransformOfAsync(node));
    }

    /// <summary>
    /// Smoke test of editing on the canvas (#103): A building block is dragged from the palette onto
    /// the surface, a second appended by click, both are connected at the source port – and everything
    /// appears immediately in the list view too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test begins on an <b>empty</b> dialog. That is deliberate: until #103 a hint replaced the
    /// canvas as long as there were no questions – nothing can be dragged onto a non-existent surface.
    /// The empty case is thus the actual proof.
    /// </para>
    /// <para>
    /// The last check is the most important: the questions stand in the question list of the dialog
    /// editor. The canvas calls the same admin commands as the forms – there is no second truth.
    /// </para>
    /// <para>
    /// The full gesture coverage (dragging into the void, resorting, deleting with cascade, trigger,
    /// loop) remains stage 5 (#105); each of these rules is proven at the command level in
    /// <c>tests/Flirty.Tests/Designer</c>.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Palette_und_Port_legen_Fragen_und_Uebergang_an()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeEmptyDialogAsync(session);

        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // The empty dialog still shows a drawing surface – otherwise there would be no target for the drag.
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(page.Locator(".graph-palette-item").First).ToBeEnabledAsync();

        // 1) Drag: The building block lands at the release point, i.e. with its own position (is-pinned).
        await DragToCanvasAsync(page, page.Locator(".graph-palette-item").First, 260, 140);

        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        // 2) Click: the pointerless path. Without a position – that is assigned by the auto-layout.
        await page.Locator(".graph-palette-item").Nth(1).ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);

        // 3) Connect: drag from the source port of one node onto the other.
        var source = page.Locator(".graph-node").First;
        var target = page.Locator(".graph-node").Nth(1);
        await DragToTargetAsync(page, source.Locator(".graph-port"), target.Locator(".graph-node-card"));

        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        // The reload proves that everything was written – not just standing in the DOM.
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, SlowCount);

        // No second truth: the same questions stand in the list of the dialog editor.
        await page.Locator(".back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
        await Assertions.Expect(Section(page, "Questions").Locator("tbody tr")).ToHaveCountAsync(2, SlowCount);
        await Assertions.Expect(Section(page, "Transitions (branching)").Locator("tbody tr"))
            .ToHaveCountAsync(1, SlowCount);
    }

    /// <summary>
    /// Since #103 the inspector is an editor: save header fields, connect, toggle default, delete. The
    /// test checks the <b>wiring</b> of these paths – each of them is its own <c>EventCallback</c> from
    /// panel via inspector to the page, and a wrongly connected one would slip through every unit test.
    /// </summary>
    /// <remarks>
    /// The end is at the same time the proof for "the co-cleanup is visibly carried along": with the
    /// deleted question the edge that hung on it disappears – reported as a count, not asserted.
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Inspector_bearbeitet_Frage_Uebergang_und_loescht_mit_Kaskade()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeEmptyDialogAsync(session);

        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // Two questions via the pointerless path – here it is about the inspector, not the gesture.
        await page.Locator(".graph-palette-item").First.ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(1, SlowCount);
        await page.Locator(".graph-palette-item").First.ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);

        var inspector = page.Locator(".graph-inspector");

        // 1) Save header fields: the node carries the new key afterwards.
        await SelectNodeAsync(page, page.Locator(".graph-node").First);

        // Addressed via the key, not via the DOM order: the arrangement arises from layer and column
        // and is no promise to the test.
        var start = page.Locator(".graph-node").Filter(new() { HasText = "start" });

        // Fill AND save in a repeatable unit, and the result is checked on the graph – not the field
        // content.
        //
        // The reason is a trap that turned this test red twice: a look at the DOM value does NOT prove
        // that Blazor saw the input. If the first interaction on a freshly rendered field fizzles
        // (Blazor Server wires it up only with the next circuit update), the typed value still stands
        // in the DOM – until the next render overwrites it with the bound value. Whoever checks in this
        // window sees success and saves the old value. Only an effect the server produced is reliable:
        // the node with the new key. Both are idempotent – saving the same value again changes nothing.
        await InteractWhenReadyAsync(
            async () =>
            {
                await page.Locator("#inspectorKey").FillAsync("start");
                await page.Locator("#inspectorText").FillAsync("What is your name?");
                await inspector.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            },
            () => Assertions.Expect(start).ToHaveCountAsync(1, QuickCount));

        // 2) Connect via the selection list – the keyboard equivalent to dragging at the port.
        //
        // The precondition is made visible instead of presupposed: "Connect" is operable exactly when
        // the server knows the chosen target. Without this intermediate step the test hung on a race –
        // the selection in the list could be overtaken by the re-render of the node selection and
        // thereby discarded, and the click ran into a disabled button (#105). Only the selecting is
        // repeated; the click creates a transition and must not double.
        await SelectNodeAsync(page, start, "start");

        var connect = inspector.GetByRole(AriaRole.Button, new() { Name = "Connect" });
        await InteractWhenReadyAsync(
            () => page.Locator("#inspectorConnect").SelectOptionAsync(new SelectOptionValue { Index = 2 }),
            () => Assertions.Expect(connect).ToBeEnabledAsync(QuickEnabled));

        await connect.ClickAsync();
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, SlowCount);

        // 3) Toggle default: the edge changes its marking, and thereby the warning "No default
        //    transition" disappears from the graph.
        await SelectNodeAsync(page, start);
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Default" }).First.ClickAsync(),
            () => Assertions.Expect(page.Locator(".graph-edge.is-default")).ToHaveCountAsync(1, QuickCount));

        // 4) Delete with visible cascade: the question goes, its edge goes with it.
        await SelectNodeAsync(page, start);
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync(),
            () => Assertions.Expect(inspector).ToContainTextAsync("Yes, delete", QuickContains));
        await inspector.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync();

        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(page.Locator(".banner.ok")).ToContainTextAsync("removed along with it", SlowContains);

        // The inspector visibly falls back to the legend – the selection would otherwise point into the void.
        await Assertions.Expect(inspector).ToContainTextAsync("Legend", SlowContains);
    }

    /// <summary>
    /// When the dialog is published the graph gestures are <b>disabled</b> instead of running into a
    /// conflict: there is no source port, the palette is locked, and the hint offers the new version.
    /// Moving continues to work (ADR 0007) – and produces no error message.
    /// </summary>
    [SkippableFact]
    public async Task Graph_Gesten_sind_bei_veroeffentlichtem_Dialog_deaktiviert()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1 .badge")).ToHaveTextAsync("Published", SlowText);

        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // The attribute is C# state: the JS module reads it on every gesture instead of copying it at
        // binding time.
        await Assertions.Expect(page.Locator("svg[data-editable='false']")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-port")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(page.Locator(".graph-palette-item").First).ToBeDisabledAsync();
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Create new version" })).ToBeVisibleAsync();

        // Moving stays allowed – and does not run into a conflict.
        await DragByAsync(page, page.Locator(".graph-node").Filter(new() { HasText = "summary" }), 150, 90);

        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);
    }

    /// <summary>
    /// The test run in the graph (#104): the same run as in the list-based runner, but as a path on the
    /// canvas – visited nodes, the open question, taken edges, the iteration count on the loop frame
    /// and the published triggers at the triggering node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test stands in the browser, although the derivation of the path in
    /// <c>tests/Flirty.Tests/Designer/GraphRunAnalyzerTests</c> is fully checked against the real
    /// engine: what is added here is the <b>wiring</b> – toggle, canvas in the runner, answer input in
    /// both views and the edit path via the inspector panel. Exactly this kind of connection broke
    /// twice in #103 without a unit test being able to see it.
    /// </para>
    /// <para>
    /// The <b>unpublished</b> dialog is played – the runner starts via
    /// <c>StartDialogVersionCommand</c>, so a draft is testable without publishing.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Testlauf_im_Graphen_hebt_den_gelaufenen_Pfad_hervor()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Test run" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/test$"));

        // Toggling is idempotent – choosing "Graph" twice changes nothing.
        await InteractWhenReadyAsync(
            () => page.Locator(".run-views")
                .GetByRole(AriaRole.Button, new() { Name = "Graph", Exact = true }).ClickAsync(),
            () => Assertions.Expect(page.Locator(".graph-canvas")).ToHaveCountAsync(1, QuickCount));

        // The explicit readiness signal of the module instead of a retry: canvas gestures are not
        // idempotent (ADR 0006).
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // Before the start the graph lies there, but without a path.
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-visited")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge.is-taken")).ToHaveCountAsync(0, SlowCount);

        await InteractWhenReadyAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Start run", Exact = true }).ClickAsync(),
            () => Assertions.Expect(CurrentStep(page)).ToContainTextAsync("Which role?", QuickContains));

        // No answer yet: the entry question is open but not visited.
        await Assertions.Expect(page.Locator(".graph-node.is-current")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-visited")).ToHaveCountAsync(0, SlowCount);

        // First iteration. The card of the visited node shows the answer, the edge is taken.
        await AnswerTextAsync(page, "Backend");
        await Assertions.Expect(page.Locator(".graph-node.is-visited")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge.is-taken")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-visited .graph-node-answer"))
            .ToContainTextAsync("Backend", SlowContains);

        // Back-jump: thereby a second edge is taken – the loop was really traversed.
        await ChooseAsync(page, "Yes");
        await Assertions.Expect(page.Locator(".graph-edge.is-taken")).ToHaveCountAsync(2, SlowCount);

        // Second iteration, then exit via "No".
        await AnswerTextAsync(page, "Frontend");
        await Assertions.Expect(page.Locator(".graph-loop-iterations")).ToContainTextAsync("2 iterations", SlowContains);

        await ChooseAsync(page, "No");
        await Assertions.Expect(page.Locator(".graph-edge.is-taken")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-current").Filter(new() { HasText = "summary" }))
            .ToHaveCountAsync(1, SlowCount);

        // The published triggers hang on the triggering node – the source remains the DesignerTriggerLog.
        await Assertions.Expect(page.Locator(".graph-node .chip-fired").First).ToBeVisibleAsync();

        // The inspector shows the bindings and the answers per iteration at the selected node.
        var positionNode = page.Locator(".graph-node").Filter(new() { HasText = "Which role?" });
        var inspector = page.Locator(".graph-inspector");

        await InteractWhenReadyAsync(
            () => positionNode.Locator(".graph-node-card").ClickAsync(),
            () => Assertions.Expect(page.Locator("#runInspectorKey")).ToBeVisibleAsync(QuickVisible));

        await Assertions.Expect(inspector).ToContainTextAsync("Iteration 2", SlowContains);
        await Assertions.Expect(inspector).ToContainTextAsync("\"Frontend\"", SlowContains);
        // The binding of the enclosing loop stands at the node – that is the gain over the global board
        // of the list view.
        await Assertions.Expect(inspector).ToContainTextAsync("position_list", SlowContains);

        // An edit recomputes the path: the downstream answers fall away, the path shrinks to the one
        // visited node. The whole unit (open, fill, save) is repeated – the same value saved again
        // leads to the same state.
        await InteractWhenReadyAsync(
            async () =>
            {
                await inspector.GetByRole(AriaRole.Button, new() { Name = "Edit" }).First.ClickAsync();
                await inspector.Locator(".answer-input input.input").FillAsync("Middleware");
                await inspector.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            },
            () => Assertions.Expect(page.Locator(".graph-node.is-visited")).ToHaveCountAsync(1, QuickCount));

        await Assertions.Expect(page.Locator(".graph-edge.is-taken")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-visited .graph-node-answer"))
            .ToContainTextAsync("Middleware", SlowContains);

        // And the list view shows the same run – the same state, just a different representation.
        await page.Locator(".run-views").GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
        await Assertions.Expect(page.Locator(".transcript")).ToContainTextAsync("Middleware", SlowContains);
        await Assertions.Expect(page.Locator(".transcript li")).ToHaveCountAsync(1, SlowCount);

        // Back to "Graph": the canvas is re-bound. That also checks that releasing the binding when
        // switching away does not tear the circuit – an error in `DisposeAsync` would show up here,
        // because afterwards nothing would be interactive anymore.
        await page.Locator(".run-views").GetByRole(AriaRole.Button, new() { Name = "Graph", Exact = true })
            .ClickAsync();
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".graph-node.is-visited")).ToHaveCountAsync(1, SlowCount);
    }

    /// <summary>
    /// The creation flow on the canvas (#105): A dialog arises completely from gestures – drag a
    /// building block, drag from the port into the <b>void</b> (question and transition in one drag),
    /// condition with live validation, default edge, entry question, moving – is published and survives
    /// a reload, <b>including</b> the positions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The drag <b>into the void</b> is the reason this test exists: it is the only gesture that derives
    /// two commands from one movement (<c>CreateQuestionCommand</c> +
    /// <c>SetDialogLayoutCommand</c> + <c>CreateTransitionCommand</c>), and the only one whose
    /// hit-test branch "no node under the pointer" is only traversed in the browser.
    /// </para>
    /// <para>
    /// <b>After every gesture a server-produced effect is awaited.</b> The lock
    /// <c>send()</c> in the JS module discards a second gesture <b>silently</b> while the first runs – a
    /// movement triggered too early leaves no message, only a missing effect. That is why behind every
    /// drag stands a count or a message, never a wait time.
    /// </para>
    /// <para>
    /// Publishing and setting the entry question happen deliberately in two different places: the
    /// entry question goes, since #105, at the node (the graph otherwise warned about something that
    /// could only be healed elsewhere), publishing stays in the dialog editor – it concerns the whole
    /// dialog, not one element of the graph.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Anlege_Flow_auf_dem_Canvas_ueberlebt_Veroeffentlichen_und_Reload()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeEmptyDialogAsync(session);

        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        var inspector = page.Locator(".graph-inspector");

        // 1) The first building block comes from the palette. Its order is that of the enum
        //    (QuestionType.SingleChoice = 0), so the suggested key is "choice".
        await DragToCanvasFractionAsync(page, page.Locator(".graph-palette-item").First, 0.25, 0.2);

        var choice = page.Locator(".graph-node").Filter(new() { HasText = "choice" });
        await Assertions.Expect(choice).ToHaveCountAsync(1, SlowCount);

        // Now – with one question – the graph warns about the missing entry. On the empty dialog it
        // deliberately does not, that is why the check stands here and not above.
        await Assertions.Expect(page.Locator(".banner.warn"))
            .ToContainTextAsync("No entry question set", SlowContains);

        // 2) From the port into the void: question AND transition from one movement.
        await DragToCanvasFractionAsync(page, choice.Locator(".graph-port"), 0.25, 0.72);

        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.ok"))
            .ToContainTextAsync("created and connected with choice", SlowContains);

        // 3) The same drag a second time – this also proves that the module's lock releases the next
        //    gesture again. The second branch is what first makes the graph complete: a conditional
        //    edge needs a default edge as its counterpart.
        await DragToCanvasFractionAsync(page, choice.Locator(".graph-port"), 0.72, 0.72);

        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(2, SlowCount);

        // 4) Condition with live validation. The expression is compiled against the dialog's sample
        //    context – "choice" is bound there as a string (DesignerExpressionContext).
        //
        //    The status message stands INSIDE the repeated unit: it arises server-side and is thus the
        //    proof that Blazor saw the input. A look at the field value would not be – it would stand
        //    in the DOM even if the input fizzled.
        await SelectOutgoingEdgeAsync(page, choice, 0);
        await InteractWhenReadyAsync(
            async () =>
            {
                await page.Locator("#inspectorExpression").FillAsync("choice == \"yes\"");
                await Assertions.Expect(page.Locator(".expr-status"))
                    .ToContainTextAsync("Expression is valid", QuickContains);
                await inspector.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            },
            // The label of EXACTLY ONE edge is checked: after the second branch there are two labels,
            // and an unbounded locator would be a strict-mode violation.
            () => Assertions.Expect(ConditionLabel(page)).ToHaveCountAsync(1, QuickCount));

        // 5) The second edge becomes default – afterwards the graph is warning-free.
        await SelectNodeAsync(page, choice);
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Default" }).Nth(1).ClickAsync(),
            () => Assertions.Expect(page.Locator(".graph-edge.is-default")).ToHaveCountAsync(1, QuickCount));

        // 6) Entry question at the node (#105). Effect: the node carries the marking, and the dialog
        //    warning disappears – the server computes both when rebuilding the model.
        await SelectNodeAsync(page, choice);
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Set as entry question" }).ClickAsync(),
            () => Assertions.Expect(page.Locator(".graph-node.is-start")).ToHaveCountAsync(1, QuickCount));

        await Assertions.Expect(page.Locator(".graph-node.is-start").Filter(new() { HasText = "choice" }))
            .ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.warn")).ToHaveCountAsync(0, SlowCount);

        // 7) Moving. All three nodes are already pinned (every gesture created a position), so the
        //    count is no proof here – the position itself is remembered. It stands in user coordinates
        //    and is thus independent of how the SVG currently scales.
        await DragByAsync(page, choice, 120, 90);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        var moved = await TransformOfAsync(choice);
        Assert.NotNull(moved);

        // 8) Publish in the dialog editor – the canvas deliberately knows no button for it.
        await page.Locator(".back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
        await PublishFromEditorAsync(page);

        // 9) The persistence proof: after reloading, every piece comes from the database.
        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(2, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge.is-default")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(ConditionLabel(page)).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-start")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(3, SlowCount);
        Assert.Equal(moved, await TransformOfAsync(choice));

        // And the graph is now locked – the same effect the read-mode test checks.
        await Assertions.Expect(page.Locator("svg[data-editable='false']")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        // The counter-check to the guard: the entry question is part of the graph, so it cannot be
        // changed on a published version. Locked, not erroneous – the button cannot be triggered at all
        // instead of running into a 409.
        await SelectNodeAsync(page, page.Locator(".graph-node").Filter(new() { HasText = "text2" }));
        await Assertions.Expect(
                inspector.GetByRole(AriaRole.Button, new() { Name = "Set as entry question" }))
            .ToBeDisabledAsync();
    }

    /// <summary>
    /// The two inspector gestures that #103 deliberately left open (#105): mark a back-jump
    /// <b>at the cycle</b> as a loop and create a trigger at exactly one question.
    /// </summary>
    /// <remarks>
    /// Both paths only have their appeal in the browser: the loop suggestion appears only when the
    /// designer has recognized the cycle itself and prefilled the collection key – and the trigger chip
    /// must hang on the <b>triggering</b> question, not on all. Finally the list parity: both stand
    /// immediately in the dialog editor too, because they are the same commands.
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Inspector_legt_Trigger_und_Schleife_am_Zyklus_an()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeEmptyDialogAsync(session);

        await page.GetByRole(AriaRole.Link, new() { Name = "View graph" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        var inspector = page.Locator(".graph-inspector");

        // Two questions via the pointerless path – here it is about the inspector, not the gesture.
        // Deliberately two different types: the keys "choice" and "text" do not overlap, while
        // "choice"/"choice2" as a text filter would hit both nodes.
        await page.Locator(".graph-palette-item").First.ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(1, SlowCount);
        await page.Locator(".graph-palette-item").Nth(2).ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);

        var choice = page.Locator(".graph-node").Filter(new() { HasText = "choice" });
        var text = page.Locator(".graph-node").Filter(new() { HasText = "text" });

        // Outbound via the selection list (index 2 = the second question in dialog order).
        //
        // Selecting and clicking happen in TWO steps with a server-side precondition between them:
        // "Connect" is operable exactly when the server knows the chosen target. That is why only the
        // selecting is repeated (idempotent), not the click – it creates a transition and must not
        // double.
        await SelectNodeAsync(page, choice, "choice");

        var connect = inspector.GetByRole(AriaRole.Button, new() { Name = "Connect" });
        await InteractWhenReadyAsync(
            () => page.Locator("#inspectorConnect").SelectOptionAsync(new SelectOptionValue { Index = 2 }),
            () => Assertions.Expect(connect).ToBeEnabledAsync(QuickEnabled));

        await connect.ClickAsync();
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, SlowCount);

        // Return path via the port gesture – thereby the cycle exists, and the designer recognizes it
        // itself.
        await DragToTargetAsync(page, text.Locator(".graph-port"), choice.Locator(".graph-node-card"));
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(2, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge.is-backjump")).ToHaveCountAsync(1, SlowCount);

        // 1) Loop AT THE CYCLE: the suggestion hangs on the edge that causes the back-jump and is
        //    prefilled with the key of the entry question (LoopFormModel.SuggestCollectionKey).
        await SelectOutgoingEdgeAsync(page, text, 0);
        await Assertions.Expect(inspector).ToContainTextAsync("Back-jump without a loop marker", SlowContains);
        await Assertions.Expect(page.Locator("#inspectorCollectionKey")).ToHaveValueAsync("choice_list", SlowValue);

        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Mark as loop" }).ClickAsync(),
            () => Assertions.Expect(page.Locator(".graph-loop-label")).ToHaveCountAsync(1, QuickCount));

        await Assertions.Expect(page.Locator(".graph-loop-label")).ToContainTextAsync("choice_list", SlowContains);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        // 2) Trigger at the node. The channel is already set to Webhook (default of the form model),
        //    so the URL field is there immediately.
        await SelectNodeAsync(page, choice);
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Create trigger after this question" })
                .ClickAsync(),
            () => Assertions.Expect(page.Locator("#inspectorTriggerUrl")).ToBeVisibleAsync(QuickVisible));

        await InteractWhenReadyAsync(
            async () =>
            {
                await page.Locator("#inspectorTriggerUrl").FillAsync("https://hooks.test/canvas");
                await inspector.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
            },
            () => Assertions.Expect(page.Locator(".graph-node .chip")).ToHaveCountAsync(1, QuickCount));

        // The chip hangs on the triggering question – not on all.
        await Assertions.Expect(choice.Locator(".chip")).ToContainTextAsync("hooks.test", SlowContains);

        // The reload proves that marker and trigger were written.
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".graph-loop-label")).ToContainTextAsync("choice_list", SlowContains);
        await Assertions.Expect(page.Locator(".graph-node .chip")).ToHaveCountAsync(1, SlowCount);

        // No second truth: both stand in the lists of the dialog editor.
        await page.Locator(".back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
        await Assertions.Expect(Section(page, "Loops").Locator("tbody tr"))
            .ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(Section(page, "Triggers").Locator("tbody tr")).ToHaveCountAsync(1, SlowCount);
    }

    /// <summary>
    /// The edge label that carries the condition set in test A. The expression appears there shortened
    /// as a label (<c>DialogGraphBuilder</c>) and is thus the effect of saving visible in the picture.
    /// </summary>
    private static ILocator ConditionLabel(IPage page)
        => page.Locator(".graph-edge-label").Filter(new() { HasText = "choice == \"yes\"" });

    /// <summary>Reads the <c>transform</c> of a node – the position visible in the DOM.</summary>
    private static async Task<string?> TransformOfAsync(ILocator node)
        => await node.GetAttributeAsync("transform");

    /// <summary>
    /// Drags an element by the given offset. Deliberately via <c>Mouse</c> instead of
    /// <c>DragToAsync</c>: the latter uses the HTML5 drag-and-drop API, which does not fire at all on
    /// an SVG canvas with pointer events.
    /// </summary>
    private static async Task DragByAsync(IPage page, ILocator target, int deltaX, int deltaY)
    {
        // The canvas host is 70vh tall and stands below header, hint and toolbar – a node of the lower
        // layers thus lies slightly outside the window. Mouse coordinates are window-relative; without
        // the scrolling the gesture would aim into the void.
        await target.ScrollIntoViewIfNeededAsync();

        var box = await target.BoundingBoxAsync();
        Assert.NotNull(box);

        const int steps = 5;
        var startX = box.X + (box.Width / 2);
        var startY = box.Y + (box.Height / 2);

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();

        // Several steps: a real gesture, and the first exceeds the module's 4-px threshold.
        for (var step = 1; step <= steps; step++)
        {
            await page.Mouse.MoveAsync(
                startX + (deltaX * step / (float)steps), startY + (deltaY * step / (float)steps));
        }

        await page.Mouse.UpAsync();
    }

    /// <summary>
    /// Drags an element onto a point on the drawing surface – the palette gesture (#103).
    /// </summary>
    /// <remarks>
    /// Like <see cref="DragByAsync"/> via <c>Mouse</c>: the palette entries are HTML outside the SVG,
    /// but their gesture runs in the same pointer-events model as the canvas – <c>DragToAsync</c>
    /// (HTML5 drag-and-drop) fires nothing there.
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <param name="source">The palette entry.</param>
    /// <param name="offsetX">The horizontal distance from the left edge of the drawing surface in px.</param>
    /// <param name="offsetY">The vertical distance from its top edge in px.</param>
    private static async Task DragToCanvasAsync(IPage page, ILocator source, int offsetX, int offsetY)
    {
        var canvas = page.Locator(".graph-canvas");
        await canvas.ScrollIntoViewIfNeededAsync();

        var from = await source.BoundingBoxAsync();
        var target = await canvas.BoundingBoxAsync();
        Assert.NotNull(from);
        Assert.NotNull(target);

        await DragBetweenAsync(
            page,
            from.X + (from.Width / 2),
            from.Y + (from.Height / 2),
            target.X + offsetX,
            target.Y + offsetY);
    }

    /// <summary>
    /// Drags an element onto a <b>fraction</b> of the drawing surface (0…1 per axis).
    /// </summary>
    /// <remarks>
    /// Deliberately relative instead of in pixels: the SVG scales its <c>viewBox</c> into the 70 vh
    /// tall host (<c>preserveAspectRatio</c> in the default), and this shares its width with palette
    /// and inspector. How many screen pixels a node is wide thus depends on the window – a fixed pixel
    /// value would land on a node instead of beside it at a different layout, and the drag into the
    /// void would silently become a connection.
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <param name="source">The palette entry or the source port.</param>
    /// <param name="fractionX">Horizontal release point as a fraction of the surface width.</param>
    /// <param name="fractionY">Vertical release point as a fraction of the surface height.</param>
    private static async Task DragToCanvasFractionAsync(
        IPage page, ILocator source, double fractionX, double fractionY)
    {
        var canvas = page.Locator(".graph-canvas");
        await canvas.ScrollIntoViewIfNeededAsync();

        var box = await canvas.BoundingBoxAsync();
        Assert.NotNull(box);

        await DragToCanvasAsync(page, source, (int)(box.Width * fractionX), (int)(box.Height * fractionY));
    }

    /// <summary>Drags from one element onto the center of another – the connection gesture (#103).</summary>
    /// <param name="page">The page.</param>
    /// <param name="source">The source port.</param>
    /// <param name="target">The target (the node card).</param>
    private static async Task DragToTargetAsync(IPage page, ILocator source, ILocator target)
    {
        await source.ScrollIntoViewIfNeededAsync();

        var from = await source.BoundingBoxAsync();
        var to = await target.BoundingBoxAsync();
        Assert.NotNull(from);
        Assert.NotNull(to);

        await DragBetweenAsync(
            page,
            from.X + (from.Width / 2),
            from.Y + (from.Height / 2),
            to.X + (to.Width / 2),
            to.Y + (to.Height / 2));
    }

    /// <summary>
    /// The shared drag between two window coordinates. Several steps so the first exceeds the module's
    /// 4-px threshold and the gesture looks like a real one.
    /// </summary>
    private static async Task DragBetweenAsync(
        IPage page, float startX, float startY, float endX, float endY)
    {
        const int steps = 6;

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();

        for (var step = 1; step <= steps; step++)
        {
            await page.Mouse.MoveAsync(
                startX + ((endX - startX) * step / steps),
                startY + ((endY - startY) * step / steps));
        }

        await page.Mouse.UpAsync();
    }

    /// <summary>
    /// Creates a dialog <b>without</b> questions – the starting point for the canvas gestures that
    /// create their questions themselves.
    /// </summary>
    /// <param name="session">The test's browser session.</param>
    /// <returns>The page sitting on the empty dialog editor.</returns>
    private async Task<IPage> ArrangeEmptyDialogAsync(PlaywrightSession session)
    {
        var page = await session.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/dialogs");

        await InteractWhenReadyAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "New dialog" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#key")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#key").FillAsync($"e2e-{Guid.NewGuid():N}"[..12]);
        await page.Locator("#name").FillAsync("E2E-Canvas-Dialog");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await page.WaitForURLAsync(DialogUrl);

        return page;
    }

    /// <summary>
    /// Creates a webhook trigger on the final question – the precondition for the graph view to be
    /// able to show a trigger chip at all.
    /// </summary>
    /// <param name="page">The page sitting on the dialog editor.</param>
    private static async Task CreateQuestionTriggerAsync(IPage page)
    {
        await Section(page, "Triggers").GetByRole(AriaRole.Button, new() { Name = "New trigger" }).ClickAsync();
        await page.Locator("#triggerScope").SelectOptionAsync("AfterQuestion");

        // The question select field appears only when the timing requires a question.
        await Assertions.Expect(page.Locator("#triggerQuestion")).ToBeVisibleAsync(QuickVisible);
        await page.Locator("#triggerQuestion").SelectOptionAsync(new SelectOptionValue { Index = SummaryOption });
        await page.Locator("#triggerUrl").FillAsync("https://hooks.test/done");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Assertions.Expect(Section(page, "Triggers").Locator("tbody tr")).ToHaveCountAsync(1, SlowCount);
    }

    /// <summary>
    /// Builds the loop dialog completely through the UI: dialog → three questions → answer options →
    /// entry question → three transitions → condition → loop marker. Both tests create their
    /// <b>own</b> dialog (unique key) because they share the fixture's database.
    /// </summary>
    /// <param name="session">The test's browser session.</param>
    /// <returns>The page sitting on the finished dialog editor.</returns>
    private async Task<IPage> ArrangeDialogAsync(PlaywrightSession session)
    {
        var page = await session.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/dialogs");

        await InteractWhenReadyAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "New dialog" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#key")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#key").FillAsync($"e2e-{Guid.NewGuid():N}"[..12]);
        await page.Locator("#name").FillAsync("E2E-Loop-Dialog");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        // CreateDialogCommand -> the page navigates itself into the editor of the new dialog.
        await page.WaitForURLAsync(DialogUrl);

        await CreateQuestionAsync(page, "position", "Which role?", "FreeText");
        await CreateQuestionAsync(page, "more", "Another role?", "SingleChoice");
        await CreateQuestionAsync(page, "summary", "Summary?", "FreeText");

        await AddChoicesToMoreQuestionAsync(page);
        await SetStartQuestionAsync(page);
        await CreateTransitionsAsync(page);
        await SetBackJumpConditionAsync(page);
        await MarkLoopAsync(page);

        return page;
    }

    private static async Task CreateQuestionAsync(IPage page, string key, string text, string type)
    {
        var questions = Section(page, "Questions");

        await InteractWhenReadyAsync(
            () => questions.GetByRole(AriaRole.Button, new() { Name = "New question" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#questionKey")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#questionKey").FillAsync(key);
        await page.Locator("#questionText").FillAsync(text);
        await page.Locator("#questionType").SelectOptionAsync(type);
        await questions.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Assertions.Expect(questions.Locator("tbody tr").Filter(new() { HasText = text }))
            .ToHaveCountAsync(1, SlowCount);
    }

    /// <summary>
    /// Adds the answer options of the single choice <c>more</c>. These are deliberately maintained not
    /// by the dialog editor but by the question editor (#39) – so we switch there and back.
    /// </summary>
    private static async Task AddChoicesToMoreQuestionAsync(IPage page)
    {
        await Section(page, "Questions").Locator("tbody tr").Filter(new() { HasText = "Another role?" })
            .GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await page.WaitForURLAsync(QuestionUrl);

        await CreateAnswerOptionAsync(page, "yes", "Yes");
        await CreateAnswerOptionAsync(page, "no", "No");

        await page.Locator("p.back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
    }

    private static async Task CreateAnswerOptionAsync(IPage page, string key, string label)
    {
        var options = Section(page, "Answer options");

        await InteractWhenReadyAsync(
            () => options.GetByRole(AriaRole.Button, new() { Name = "New option" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#optionKey")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#optionKey").FillAsync(key);
        await page.Locator("#optionLabel").FillAsync(label);
        // The value is saved and validated – exactly that appears later in the expression.
        await page.Locator("#optionValue").FillAsync(key);
        await options.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Assertions.Expect(options.Locator("tbody tr").Filter(new() { HasText = label }))
            .ToHaveCountAsync(1, SlowCount);
    }

    private static async Task SetStartQuestionAsync(IPage page)
    {
        // The badge "Entry" on the position row is at the same time the effect check and the proof that
        // the option indices above hit the expected questions. Choosing and saving the same question
        // again is without consequence – so the interaction may be repeated.
        var startBadge = Section(page, "Questions").Locator("tbody tr")
            .Filter(new() { HasText = "Which role?" }).Locator(".badge-start");

        await InteractWhenReadyAsync(
            async () =>
            {
                await page.Locator("#startQuestion").SelectOptionAsync(new SelectOptionValue { Index = PositionOption });
                await Section(page, "Metadata").GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            },
            () => Assertions.Expect(startBadge).ToBeVisibleAsync(QuickVisible));
    }

    /// <summary>
    /// Creates the branching: <c>position</c> → <c>more</c> (default), from <c>more</c> the conditional
    /// back-jump to <c>position</c> and as default the exit to <c>summary</c>.
    /// </summary>
    private static async Task CreateTransitionsAsync(IPage page)
    {
        await CreateTransitionAsync(page, PositionOption, MoreOption, isDefault: true);
        await CreateTransitionAsync(page, MoreOption, PositionOption, isDefault: false);
        await CreateTransitionAsync(page, MoreOption, SummaryOption, isDefault: true);

        var transitions = Section(page, "Transitions (branching)");
        await Assertions.Expect(transitions.Locator("tbody tr")).ToHaveCountAsync(3, SlowCount);
        // The designer recognizes the cycle itself.
        await Assertions.Expect(transitions.Locator(".badge-loop")).ToHaveTextAsync("Back-jump", SlowText);
    }

    private static async Task CreateTransitionAsync(IPage page, int from, int target, bool isDefault)
    {
        var transitions = Section(page, "Transitions (branching)");

        await InteractWhenReadyAsync(
            () => transitions.GetByRole(AriaRole.Button, new() { Name = "New transition" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#transitionFrom")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#transitionFrom").SelectOptionAsync(new SelectOptionValue { Index = from });
        await page.Locator("#transitionTarget").SelectOptionAsync(new SelectOptionValue { Index = target });
        if (isDefault)
        {
            await page.Locator("#transitionDefault").CheckAsync();
        }

        await transitions.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(page.Locator("#transitionFrom")).ToHaveCountAsync(0, SlowCount);
    }

    /// <summary>
    /// Maintains the condition of the back-jump in the transition editor (#40) and checks the
    /// <b>live validation</b> in doing so: the expression is compiled against the dialog's sample
    /// context already while typing.
    /// </summary>
    private static async Task SetBackJumpConditionAsync(IPage page)
    {
        await Section(page, "Transitions (branching)").Locator("tbody tr").Filter(new() { HasText = "Back-jump" })
            .GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await page.WaitForURLAsync(TransitionUrl);

        await InteractWhenReadyAsync(
            () => page.Locator("#expression").FillAsync("more == \"yes\""),
            () => Assertions.Expect(page.Locator(".expr-status"))
                .ToContainTextAsync("Expression is valid", QuickContains));

        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(page.Locator(".banner.ok")).ToContainTextAsync("saved", SlowContains);

        await page.Locator("p.back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
    }

    /// <summary>
    /// Marks the cycle as a loop (#41) – via the suggestion the designer offers itself for unmarked
    /// back-jumps (including the prefilled collection key).
    /// </summary>
    private static async Task MarkLoopAsync(IPage page)
    {
        var loops = Section(page, "Loops");

        await InteractWhenReadyAsync(
            () => loops.GetByRole(AriaRole.Button, new() { Name = "Mark as loop" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#loopKey")).ToBeVisibleAsync(QuickVisible));

        // The collection key is prefilled from the back-jump (LoopFormModel.SuggestCollectionKey).
        await Assertions.Expect(page.Locator("#loopKey")).ToHaveValueAsync("position_list", new() { Timeout = 15_000 });

        await loops.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(loops.Locator("tbody tr")).ToHaveCountAsync(1, SlowCount);
    }

    // ---- Test runner ---------------------------------------------------------------------------------

    /// <summary>The section with the open question or – after the last step – the result.</summary>
    private static ILocator CurrentStep(IPage page)
        => page.Locator(".editor").Filter(new() { Has = page.Locator("h2", new() { HasTextRegex = new Regex("^(Current question|Result)$") }) });

    private static async Task AnswerTextAsync(IPage page, string text)
    {
        await CurrentStep(page).Locator(".answer-input input.input").FillAsync(text);
        await CurrentStep(page).GetByRole(AriaRole.Button, new() { Name = "Answer" }).ClickAsync();
    }

    private static Task ChooseAsync(IPage page, string label)
        => CurrentStep(page).GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();

    // ---- Helpers -------------------------------------------------------------------------------------

    /// <summary>A section ("editor" card) of the page, addressed via its heading.</summary>
    /// <param name="page">The page.</param>
    /// <param name="heading">The exact text of the <c>h2</c> heading.</param>
    private static ILocator Section(IPage page, string heading)
        => page.Locator(".editor").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }) });

    /// <summary>
    /// Performs the <b>first</b> interaction after a page change and repeats it until it takes effect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Blazor Server a freshly rendered page is at first only prerendered DOM; until the circuit has
    /// taken it over, clicks and inputs fizzle <b>silently</b> – no error, no effect. This holds not
    /// only after <c>GotoAsync</c>, but also after every <c>NavigateTo</c> navigation of the designer:
    /// the router is static, every page is delivered anew via Enhanced Navigation and its interactive
    /// component is attached to the circuit only afterwards.
    /// </para>
    /// <para>
    /// There is no reliable JS signal for it: <c>window.Blazor.reconnect</c> is defined and the
    /// <c>&lt;!--Blazor:…--&gt;</c> boot markers have vanished <i>before</i> the circuit processes
    /// events (both measured). That is why the interaction is repeated until its effect occurs – it
    /// must be <b>idempotent</b> for that (open a form, fill a field, save the same value again).
    /// </para>
    /// </remarks>
    /// <param name="interaction">The – idempotent – interaction.</param>
    /// <param name="verify">Check of the effect; should use a short timeout.</param>
    private static async Task InteractWhenReadyAsync(Func<Task> interaction, Func<Task> verify)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await interaction();
            try
            {
                await verify();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                // The circuit had not yet taken over the page – try again.
            }
        }
    }

    /// <summary>Short timeout for the effect check in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToBeVisibleOptions QuickVisible = new() { Timeout = 2_000 };

    /// <summary>Short timeout for the effect check in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToContainTextOptions QuickContains = new() { Timeout = 2_000 };

    /// <summary>Short timeout for the effect check in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToHaveValueOptions QuickValue = new() { Timeout = 2_000 };

    /// <summary>Short timeout for the effect check in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToHaveCountOptions QuickCount = new() { Timeout = 2_000 };

    /// <summary>
    /// Selects a node and waits until its inspector panel is up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repeated because the selection produces a freshly rendered panel and the first interaction on it
    /// can fizzle. A selection is idempotent – choosing the same node twice changes nothing.
    /// </para>
    /// <para>
    /// Pass <paramref name="expectedKey"/> when <b>a question panel is already open</b>: then
    /// <c>#inspectorKey</c> is already visible, and the check "is visible" would apply to the old panel –
    /// the retry loop would consider the fizzled click successful. The key in the field, by contrast,
    /// is a statement about <i>which</i> question the server currently shows.
    /// </para>
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <param name="node">The node to select.</param>
    /// <param name="expectedKey">The expected key in the inspector, if a panel is already open.</param>
    private static async Task SelectNodeAsync(IPage page, ILocator node, string? expectedKey = null)
        => await InteractWhenReadyAsync(
            () => node.Locator(".graph-node-card").ClickAsync(),
            () => expectedKey is null
                ? Assertions.Expect(page.Locator("#inspectorKey")).ToBeVisibleAsync(QuickVisible)
                : Assertions.Expect(page.Locator("#inspectorKey")).ToHaveValueAsync(expectedKey, QuickValue));

    /// <summary>
    /// Selects an outgoing edge of a node via the list in the inspector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> via a click on <c>.graph-edge-hit</c>: the hit path is a Bézier, and the
    /// center of its bounding box does not necessarily lie on the stroke – Playwright would then aim
    /// beside it and fail on actionability instead of on the matter. The list is the keyboard path to
    /// the edges anyway (they are not focusable).
    /// </para>
    /// <para>
    /// Addressed via <c>ol.graph-inspector-list</c>: the <b>outgoing</b> transitions stand in an
    /// ordered list (their order is the evaluation order), the incoming ones in an unordered one.
    /// Without this distinction the index would hit the wrong one for a node with incoming edges.
    /// </para>
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <param name="node">The node whose edge is selected.</param>
    /// <param name="index">The position in the evaluation order (zero-based).</param>
    private static async Task SelectOutgoingEdgeAsync(IPage page, ILocator node, int index)
    {
        await SelectNodeAsync(page, node);

        await InteractWhenReadyAsync(
            () => page.Locator(".graph-inspector ol.graph-inspector-list .graph-edge-link")
                .Nth(index).ClickAsync(),
            () => Assertions.Expect(page.Locator("#inspectorExpression")).ToBeVisibleAsync(QuickVisible));
    }

    /// <summary>
    /// Publishes the dialog from within the dialog editor.
    /// </summary>
    /// <remarks>
    /// Two steps in <b>one</b> repeatable unit: with open graph warnings the editor asks back (#97),
    /// otherwise not. After publishing neither of the two buttons is visible anymore – the unit is thus
    /// idempotent. "Publish" needs <c>Exact&#160;=&#160;true</c>: without it the name also matches
    /// "Yes, publish", and the locator violates strict mode.
    /// </remarks>
    /// <param name="page">The page sitting on the dialog editor.</param>
    private static Task PublishFromEditorAsync(IPage page)
        => InteractWhenReadyAsync(
            async () =>
            {
                var publish = page.GetByRole(
                    AriaRole.Button, new() { Name = "Publish", Exact = true });
                if (await publish.IsVisibleAsync())
                {
                    await publish.ClickAsync();
                }

                var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Yes, publish" });
                if (await confirm.IsVisibleAsync())
                {
                    await confirm.ClickAsync();
                }
            },
            () => Assertions.Expect(page.Locator("h1 .badge")).ToHaveTextAsync("Published", QuickText));

    /// <summary>Short timeout for the effect check in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToHaveTextOptions QuickText = new() { Timeout = 2_000 };

    /// <summary>Short timeout for the effect check in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToBeEnabledOptions QuickEnabled = new() { Timeout = 2_000 };
}
