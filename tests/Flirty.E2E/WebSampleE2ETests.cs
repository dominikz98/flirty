using Microsoft.Playwright;

namespace Flirty.E2E;

/// <summary>
/// Playwright E2E of the web sample chat UI (#45/#47) against a real, in-process hosted Kestrel
/// (<see cref="WebSampleAppFixture"/>). Covers the issue's acceptance criterion in both directions:
/// <b>branching</b> (dev branch <i>and</i> default branch), <b>loop over a list</b> (two iterations
/// including completion, in-process trigger and a full outbound→inbound webhook round-trip),
/// <b>reload→resume</b> in the middle of the loop as well as <b>editing an earlier answer</b> (free
/// text, branching question, targeted loop iteration). If no Playwright browsers are installed, the
/// tests skip themselves (<see cref="SkippableFactAttribute"/>) – install e.g. via
/// <c>pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tests share <b>one</b> app including database via the fixture, but each gets a fresh browser
/// context (empty <c>localStorage</c> → own <c>externalUserKey</c> → own session). What they <i>do</i>
/// share as a result are the singletons behind the two trigger panels (<c>TriggerLog</c>,
/// <c>WebhookInbox</c>): every test that completes the dialog writes into them. The panel assertions
/// are therefore deliberately <c>Contains</c>-based and must not be switched to a count.
/// </para>
/// <para>
/// The chat UI discards the complete history on every render and rebuilds it from the server state
/// (<c>refreshAndRender</c>). There is therefore always exactly <b>one</b> bot bubble – the open
/// question –, which makes the check "which question is currently open?" via
/// <see cref="CurrentPromptKey"/> exact, and the order of the answer bubbles matches the sequence on
/// the server.
/// </para>
/// </remarks>
public sealed class WebSampleE2ETests : IClassFixture<WebSampleAppFixture>
{
    private static readonly LocatorAssertionsToContainTextOptions SlowContains = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveTextOptions SlowText = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveValueOptions SlowValue = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveCountOptions SlowCount = new() { Timeout = 15_000 };

    private readonly WebSampleAppFixture _fixture;

    /// <summary>Initializes the test with the shared app host.</summary>
    /// <param name="fixture">The in-process hosted sample app host.</param>
    public WebSampleE2ETests(WebSampleAppFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The complete run: dev branch, two loop iterations, completion – and the proof that the
    /// completion triggers the in-process handler <b>and</b> the outgoing webhook arrives at its own
    /// inbound receiver (the round-trip needs a real Kestrel and is only checkable here).
    /// </summary>
    [SkippableFact]
    public async Task Durchlauf_Branching_Loop_und_Trigger_Rundlauf()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        // Loop over a list: two skill iterations (Yes = loop-back), then No = exit.
        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "Yes");
        await FillAndSendAsync(page, "Blazor");
        await ChooseAsync(page, "No");

        // Final question (Boolean) -> dialog completed.
        await ChooseAsync(page, "Yes");

        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("completed", SlowContains);

        // The iteration badges prove that the loop really collected: both loop questions (entry skill
        // and breaking question more) carry their own index per run.
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(
            ["role", "language", "skill #1", "more #1", "skill #2", "more #2", "summary"], SlowText);
        await Assertions.Expect(page.Locator("#skillsList li")).ToHaveTextAsync(["EF Core", "Blazor"], SlowText);

        // In-process handler and outbound→inbound webhook round-trip become visible in the panel (polling).
        await Assertions.Expect(page.Locator("#triggersList")).ToContainTextAsync("web-onboarding", SlowContains);
        await Assertions.Expect(page.Locator("#webhooksList")).ToContainTextAsync("OnDialogCompleted", SlowContains);
    }

    /// <summary>
    /// The counter-check to the dev branch: "Product Manager" matches no condition, so the
    /// <c>IsDefault</c> transition to <c>product</c> takes effect – and both branches then run into the
    /// same loop.
    /// </summary>
    [SkippableFact]
    public async Task Branching_Default_Zweig_fuehrt_ueber_product_in_die_Schleife()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await session.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await ChooseAsync(page, "Product Manager");

        // Not language: the condition role == "dev" does not match, the default transition takes effect.
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("product", SlowText);

        await FillAndSendAsync(page, "Flirty");

        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("skill", SlowText);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(["role", "product"], SlowText);
    }

    /// <summary>
    /// Reload <b>in the middle of the loop</b>: after reloading, the complete history comes from the
    /// server via <c>GET /flirty/sessions/{id}</c> – including the iteration assignment of the already
    /// collected answers and the open question.
    /// </summary>
    [SkippableFact]
    public async Task Reload_stellt_die_Session_mitten_in_der_Schleife_wieder_her()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "Yes");
        await FillAndSendAsync(page, "Blazor");
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("more", SlowText);

        await page.ReloadAsync();

        // Exact instead of Contains: "resume" also stands in the status text of the server-side resume
        // path (POST /flirty/sessions with isResumed) – what is to be checked here is the localStorage
        // path.
        await Assertions.Expect(page.Locator("#statusLine"))
            .ToHaveTextAsync("Session restored after reload (resume).", SlowText);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(
            ["role", "language", "skill #1", "more #1", "skill #2"], SlowText);
        await Assertions.Expect(page.Locator("#skillsList li")).ToHaveTextAsync(["EF Core", "Blazor"], SlowText);
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("more", SlowText);
    }

    /// <summary>
    /// Editing a free-text answer: the new value replaces the old one, all <b>downstream</b> answers
    /// are discarded and the path is recomputed from the edited question onwards.
    /// </summary>
    [SkippableFact]
    public async Task Editieren_einer_Antwort_verwirft_nachgelagerte_Antworten()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await AwaitAnsweredAsync(page);

        await EditAsync(page, "language");

        // Prefilled is the saved value – not its display form.
        await Assertions.Expect(EditField(page)).ToHaveValueAsync("C#", SlowValue);
        await SaveEditAsync(page, "Rust");

        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("1 downstream answer(s) discarded", SlowContains);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(["role", "language"], SlowText);
        await Assertions.Expect(Bubble(page, "language")).ToContainTextAsync("Rust", SlowContains);
        await Assertions.Expect(page.Locator("#skillsList")).ToContainTextAsync("No skill recorded yet", SlowContains);

        // The path was recomputed from language onwards – the loop begins from the start.
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("skill", SlowText);
    }

    /// <summary>
    /// The issue's main case: change the <b>branching question</b> afterwards. dev becomes pm, so the
    /// engine recomputes the path and switches to the default branch – the answers of the dev branch
    /// are thereby obsolete and get discarded.
    /// </summary>
    /// <remarks>
    /// At the same time the regression test for the bug this test uncovered: the edit form rendered a
    /// text field for every question and prefilled it with the <i>display form</i>. For a single choice
    /// this saved the label ("Product Manager") instead of the value ("pm") – the
    /// <c>AnswerValidator</c> rejected that with 400, the status line showed only "Error: 400 …".
    /// </remarks>
    [SkippableFact]
    public async Task Editieren_der_Verzweigungsfrage_wechselt_den_Zweig()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await AwaitAnsweredAsync(page);

        await EditAsync(page, "role");
        // Single choice: the edit form offers the same option buttons as the normal input, the click
        // saves directly.
        await ChooseAsync(page, "Product Manager");

        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("2 downstream answer(s) discarded", SlowContains);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(["role"], SlowText);
        await Assertions.Expect(Bubble(page, "role")).ToContainTextAsync("Product Manager", SlowContains);
        await Assertions.Expect(page.Locator("#skillsList")).ToContainTextAsync("No skill recorded yet", SlowContains);

        // The branch has switched: instead of language, product is now open.
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("product", SlowText);
    }

    /// <summary>
    /// Editing within the loop: the UI sends along the <c>iterationIndex</c> of the clicked bubble, so
    /// that <i>this</i> iteration specifically is overwritten. The completed dialog is reopened in doing
    /// so, because the recomputation leads to a non-terminal question.
    /// </summary>
    [SkippableFact]
    public async Task Editieren_einer_Loop_Iteration_trifft_genau_diese_Iteration()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "Yes");
        await FillAndSendAsync(page, "Blazor");
        await ChooseAsync(page, "No");
        await ChooseAsync(page, "Yes");
        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("completed", SlowContains);

        await EditAsync(page, "skill #2");
        await SaveEditAsync(page, "Rust");

        // Only the answers given after the second iteration are discarded (more #2, summary).
        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("2 downstream answer(s) discarded", SlowContains);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(
            ["role", "language", "skill #1", "more #1", "skill #2"], SlowText);

        // Iteration 1 stays untouched, only iteration 2 carries the new value.
        await Assertions.Expect(page.Locator("#skillsList li")).ToHaveTextAsync(["EF Core", "Rust"], SlowText);

        // The completed session is open again – at the breaking question of the loop.
        await Assertions.Expect(page.Locator(".msg--system")).ToHaveCountAsync(0);
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("more", SlowText);
    }

    /// <summary>
    /// Answering the yes/no question at the end again with "Yes": the value must <b>stay preserved</b>.
    /// The test sounds trivial but is the second half of the bug described above – and the more
    /// dangerous one: a text field with the display form "Yes" ran through <c>encodeAnswer</c> on
    /// saving, which maps everything except <c>"true"</c> to <c>false</c>. The answer thus silently
    /// flipped to "No", without an error message.
    /// </summary>
    [SkippableFact]
    public async Task Editieren_einer_Ja_Nein_Antwort_behaelt_den_gewaehlten_Wert()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "No");
        await ChooseAsync(page, "Yes");
        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("completed", SlowContains);

        await EditAsync(page, "summary");
        await ChooseAsync(page, "Yes");

        // summary is terminal: there is nothing to discard, the session stays completed.
        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("0 downstream answer(s) discarded", SlowContains);
        await Assertions.Expect(Bubble(page, "summary")).ToContainTextAsync("Yes", SlowContains);
        await Assertions.Expect(Bubble(page, "summary")).Not.ToContainTextAsync("No", SlowContains);
        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("completed", SlowContains);
    }

    // ---- Flow helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Opens the chat UI and answers the entry in the dev branch (role "Developer", language "C#"), so
    /// that afterwards the entry question of the loop is open.
    /// </summary>
    /// <param name="session">The test's browser session.</param>
    /// <returns>The page with the opened chat UI.</returns>
    private async Task<IPage> ArrangeDevBranchAsync(PlaywrightSession session)
    {
        var page = await session.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await ChooseAsync(page, "Developer");
        await FillAndSendAsync(page, "C#");

        return page;
    }

    /// <summary>Fills the input field of the input line and sends the answer.</summary>
    private static async Task FillAndSendAsync(IPage page, string text)
    {
        await EditField(page).FillAsync(text);
        await InputArea(page).GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
    }

    /// <summary>
    /// Waits until the last sent answer is booked server-side – recognizable by the status line having
    /// dropped the sending hint again and the edit pencils no longer being locked.
    /// </summary>
    /// <remarks>
    /// Needed before an <see cref="EditAsync"/> directly after <see cref="FillAndSendAsync"/>: without
    /// the waiting the edit call overtakes the still-running submit. The server does not yet know the
    /// last answer then, discards one answer too few and rejects the trailing submit with 409 ("is not
    /// the currently open question"). On fast hardware this was reproducibly red (#97). The chat UI now
    /// locks the pencils itself for the duration of the request; this waiting here nevertheless makes
    /// the precondition visible in the test instead of tacitly presupposing it.
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <returns>A task that is completed once the answer is booked.</returns>
    private static Task AwaitAnsweredAsync(IPage page)
        => Assertions.Expect(page.Locator("#chatLog .msg__edit:disabled")).ToHaveCountAsync(0, SlowCount);

    /// <summary>
    /// Clicks a choice button of the input line (answer option or yes/no). Deliberately narrowed to the
    /// input line: there both the buttons of the open question and those of the edit form appear.
    /// </summary>
    private static Task ChooseAsync(IPage page, string label)
        => InputArea(page).GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();

    /// <summary>Opens the edit form via the pencil on the answer bubble.</summary>
    /// <param name="page">The page.</param>
    /// <param name="keyBadge">The key badge of the bubble, e.g. <c>language</c> or <c>skill #2</c>.</param>
    private static Task EditAsync(IPage page, string keyBadge)
        => Bubble(page, keyBadge).Locator(".msg__edit").ClickAsync();

    /// <summary>Overwrites the value in the edit form of a free-text/number/date question and saves.</summary>
    private static async Task SaveEditAsync(IPage page, string text)
    {
        await EditField(page).FillAsync(text);
        await InputArea(page).GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
    }

    // ---- Locator helpers -----------------------------------------------------------------------------

    /// <summary>The input line – carries the open question or the edit form depending on state.</summary>
    private static ILocator InputArea(IPage page) => page.Locator(".chat__input");

    /// <summary>The text input field of the input line.</summary>
    private static ILocator EditField(IPage page) => InputArea(page).Locator("input.field");

    /// <summary>The answer bubble for the given key badge.</summary>
    private static ILocator Bubble(IPage page, string keyBadge)
        => page.Locator($".msg--user:has-text('{keyBadge}')");

    /// <summary>The key badges of all answer bubbles in answer order (incl. iteration index).</summary>
    private static ILocator AnsweredKeys(IPage page) => page.Locator(".msg--user .msg__key");

    /// <summary>The key badge of the currently open question; empty when the dialog is completed.</summary>
    private static ILocator CurrentPromptKey(IPage page) => page.Locator(".msg--bot .msg__key");
}
