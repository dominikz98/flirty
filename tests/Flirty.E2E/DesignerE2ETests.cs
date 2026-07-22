using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Flirty.E2E;

/// <summary>
/// Playwright-E2E des Blazor-Designers (#46) gegen ein echtes, in-Prozess gehostetes Kestrel
/// (<see cref="DesignerAppFixture"/>). Deckt das Akzeptanzkriterium des Issues ab – Dialog anlegen →
/// Branching → Loop → speichern – und spielt den so konfigurierten Dialog anschließend mit dem
/// Test-Runner (#43) und damit der echten Engine durch. Sind keine Playwright-Browser installiert,
/// überspringen sich die Tests (<see cref="SkippableFactAttribute"/>) – Installation z. B. via
/// <c>pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium</c>.
/// </summary>
/// <remarks>
/// Der aufgebaute Graph spiegelt bewusst <c>DesignerTestHost.ArrangeLoopDialogAsync</c> aus
/// <c>tests/Flirty.Tests</c>: <c>position</c> → <c>more</c>, bei <c>more == "yes"</c> zurück auf
/// <c>position</c>, sonst weiter auf <c>summary</c>; Schleife <c>positions</c>. So beschreiben
/// Service-Tests und E2E denselben Dialog – nur einmal über die Commands, einmal durch die UI.
/// </remarks>
public sealed class DesignerE2ETests : IClassFixture<DesignerAppFixture>
{
    private static readonly LocatorAssertionsToContainTextOptions SlowContains = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveTextOptions SlowText = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveCountOptions SlowCount = new() { Timeout = 15_000 };

    // Die Frage-Auswahlfelder listen die Fragen in Dialog-Reihenfolge; an Index 0 steht der Leereintrag
    // („— keine —" bzw. „— wählen —"). Die Indizes gelten also für den unten aufgebauten Graphen – dass
    // sie die erwarteten Fragen treffen, prüft SetStartQuestionAsync mit.
    private const int PositionOption = 1;
    private const int MoreOption = 2;
    private const int SummaryOption = 3;

    private static readonly Regex DialogUrl = new(@"/dialogs/[0-9a-fA-F-]{36}$");
    private static readonly Regex QuestionUrl = new(@"/questions/[0-9a-fA-F-]{36}$");
    private static readonly Regex TransitionUrl = new(@"/transitions/[0-9a-fA-F-]{36}$");

    private readonly DesignerAppFixture _fixture;

    /// <summary>Initialisiert den Test mit dem gemeinsam genutzten Designer-Host.</summary>
    /// <param name="fixture">Der in-Prozess gehostete Designer.</param>
    public DesignerE2ETests(DesignerAppFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Das Akzeptanzkriterium aus #46: einen Dialog samt Fragen, Übergängen und Schleife komplett
    /// durch die UI anlegen, veröffentlichen – und über einen Reload nachweisen, dass alles wirklich
    /// gespeichert wurde (nach dem Neuladen kommt jedes Feld aus der Datenbank).
    /// </summary>
    [SkippableFact]
    public async Task Dialog_mit_Branching_und_Schleife_anlegen_und_speichern()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Veröffentlichen" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1 .badge")).ToHaveTextAsync("Veröffentlicht", SlowText);

        // Neu laden: der Server rendert die Seite komplett aus der Datenbank neu – erst das belegt,
        // dass Fragen, Übergänge, Bedingung, Schleife und Publish-Status persistiert sind.
        await page.ReloadAsync();

        await Assertions.Expect(page.Locator("h1 .badge-published")).ToHaveTextAsync("Veröffentlicht", SlowText);
        await Assertions.Expect(Section(page, "Fragen").Locator("tbody tr")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(Section(page, "Übergänge (Branching)").Locator("tbody tr"))
            .ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(Section(page, "Übergänge (Branching)"))
            .ToContainTextAsync("more == \"yes\"", SlowContains);

        // Die Schleife trägt das Badge „Schleife" statt „n Warnung(en)": der LoopAnalyzer findet also
        // einen erreichbaren Ausstieg – der Zyklus ist keine Endlosschleife.
        var loopRow = Section(page, "Schleifen (Loops)").Locator("tbody tr").Filter(new() { HasText = "positions" });
        await Assertions.Expect(loopRow.Locator(".badge")).ToHaveTextAsync("Schleife", SlowText);
    }

    /// <summary>
    /// Die Gegenprobe zur Konfiguration: denselben – bewusst <b>unveröffentlichten</b> – Dialog mit dem
    /// Test-Runner (#43) und damit der echten Engine durchspielen. Zwei Iterationen der Schleife, dann
    /// Ausstieg und Abschluss.
    /// </summary>
    [SkippableFact]
    public async Task Testlauf_spielt_die_Schleife_mit_der_echten_Engine_durch()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Durchspielen" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/test$"));

        // Exact: sonst träfe der Name auch das „Neuen Lauf starten" der Ergebnis-Karte. Ein zweiter Klick
        // würde nur einen weiteren Lauf beginnen und den ersten verwerfen – harmlos.
        await InteractWhenReadyAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Lauf starten", Exact = true }).ClickAsync(),
            () => Assertions.Expect(CurrentStep(page)).ToContainTextAsync("Welche Position?", QuickContains));

        // Erste Iteration, Rücksprung über „Ja", zweite Iteration, Ausstieg über „Nein".
        await AnswerTextAsync(page, "Backend");
        await ChooseAsync(page, "Ja");
        await AnswerTextAsync(page, "Frontend");
        await ChooseAsync(page, "Nein");
        await AnswerTextAsync(page, "fertig");

        await Assertions.Expect(CurrentStep(page)).ToContainTextAsync("Dialog abgeschlossen", SlowContains);

        // Der Verlauf weist die zweite Iteration aus – die Schleife hat also wirklich gesammelt statt
        // die erste Antwort zu überschreiben …
        await Assertions.Expect(page.Locator(".transcript")).ToContainTextAsync("Iteration 2", SlowContains);

        // … und der Ausdruckskontext zeigt beide Werte unter dem Collection-Schlüssel.
        var collection = Section(page, "Ausdruckskontext").Locator("tbody tr").Filter(new() { HasText = "positions" });
        await Assertions.Expect(collection).ToContainTextAsync("Backend", SlowContains);
        await Assertions.Expect(collection).ToContainTextAsync("Frontend", SlowContains);
    }

    /// <summary>
    /// Baut den Schleifen-Dialog vollständig durch die UI auf: Dialog → drei Fragen → Antwortoptionen →
    /// Einstiegsfrage → drei Übergänge → Bedingung → Schleifen-Marker. Beide Tests legen dabei ihren
    /// <b>eigenen</b> Dialog an (eindeutiger Schlüssel), weil sie sich die Datenbank der Fixture teilen.
    /// </summary>
    /// <param name="session">Die Browser-Sitzung des Tests.</param>
    /// <returns>Die Seite, die auf dem fertigen Dialog-Editor steht.</returns>
    private async Task<IPage> ArrangeDialogAsync(PlaywrightSession session)
    {
        var page = await session.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/dialogs");

        await InteractWhenReadyAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Neuer Dialog" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#key")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#key").FillAsync($"e2e-{Guid.NewGuid():N}"[..12]);
        await page.Locator("#name").FillAsync("E2E-Schleifendialog");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();

        // CreateDialogCommand -> die Seite navigiert selbst in den Editor des neuen Dialogs.
        await page.WaitForURLAsync(DialogUrl);

        await CreateQuestionAsync(page, "position", "Welche Position?", "FreeText");
        await CreateQuestionAsync(page, "more", "Weitere Position?", "SingleChoice");
        await CreateQuestionAsync(page, "summary", "Zusammenfassung?", "FreeText");

        await AddChoicesToMoreQuestionAsync(page);
        await SetStartQuestionAsync(page);
        await CreateTransitionsAsync(page);
        await SetBackJumpConditionAsync(page);
        await MarkLoopAsync(page);

        return page;
    }

    private static async Task CreateQuestionAsync(IPage page, string key, string text, string type)
    {
        var questions = Section(page, "Fragen");

        await InteractWhenReadyAsync(
            () => questions.GetByRole(AriaRole.Button, new() { Name = "Neue Frage" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#questionKey")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#questionKey").FillAsync(key);
        await page.Locator("#questionText").FillAsync(text);
        await page.Locator("#questionType").SelectOptionAsync(type);
        await questions.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();

        await Assertions.Expect(questions.Locator("tbody tr").Filter(new() { HasText = text }))
            .ToHaveCountAsync(1, SlowCount);
    }

    /// <summary>
    /// Ergänzt die Antwortoptionen der Einfachauswahl <c>more</c>. Die pflegt bewusst nicht der
    /// Dialog-Editor, sondern der Frage-Editor (#39) – also wird dorthin gewechselt und zurück.
    /// </summary>
    private static async Task AddChoicesToMoreQuestionAsync(IPage page)
    {
        await Section(page, "Fragen").Locator("tbody tr").Filter(new() { HasText = "Weitere Position?" })
            .GetByRole(AriaRole.Button, new() { Name = "Bearbeiten" }).ClickAsync();
        await page.WaitForURLAsync(QuestionUrl);

        await CreateAnswerOptionAsync(page, "yes", "Ja");
        await CreateAnswerOptionAsync(page, "no", "Nein");

        await page.Locator("p.back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
    }

    private static async Task CreateAnswerOptionAsync(IPage page, string key, string label)
    {
        var options = Section(page, "Antwortoptionen");

        await InteractWhenReadyAsync(
            () => options.GetByRole(AriaRole.Button, new() { Name = "Neue Option" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#optionKey")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#optionKey").FillAsync(key);
        await page.Locator("#optionLabel").FillAsync(label);
        // Gespeichert und validiert wird der Wert – genau der taucht später im Ausdruck auf.
        await page.Locator("#optionValue").FillAsync(key);
        await options.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();

        await Assertions.Expect(options.Locator("tbody tr").Filter(new() { HasText = label }))
            .ToHaveCountAsync(1, SlowCount);
    }

    private static async Task SetStartQuestionAsync(IPage page)
    {
        // Das Badge „Einstieg" an der position-Zeile ist zugleich die Wirkungsprüfung und der Beleg,
        // dass die Options-Indizes oben die erwarteten Fragen treffen. Dieselbe Frage erneut zu wählen
        // und zu speichern ist folgenlos – die Interaktion darf also wiederholt werden.
        var startBadge = Section(page, "Fragen").Locator("tbody tr")
            .Filter(new() { HasText = "Welche Position?" }).Locator(".badge-start");

        await InteractWhenReadyAsync(
            async () =>
            {
                await page.Locator("#startQuestion").SelectOptionAsync(new SelectOptionValue { Index = PositionOption });
                await Section(page, "Metadaten").GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            },
            () => Assertions.Expect(startBadge).ToBeVisibleAsync(QuickVisible));
    }

    /// <summary>
    /// Legt das Branching an: <c>position</c> → <c>more</c> (Default), von <c>more</c> aus der bedingte
    /// Rücksprung auf <c>position</c> und als Default der Ausstieg auf <c>summary</c>.
    /// </summary>
    private static async Task CreateTransitionsAsync(IPage page)
    {
        await CreateTransitionAsync(page, PositionOption, MoreOption, isDefault: true);
        await CreateTransitionAsync(page, MoreOption, PositionOption, isDefault: false);
        await CreateTransitionAsync(page, MoreOption, SummaryOption, isDefault: true);

        var transitions = Section(page, "Übergänge (Branching)");
        await Assertions.Expect(transitions.Locator("tbody tr")).ToHaveCountAsync(3, SlowCount);
        // Der Designer erkennt den Zyklus von selbst.
        await Assertions.Expect(transitions.Locator(".badge-loop")).ToHaveTextAsync("Rücksprung", SlowText);
    }

    private static async Task CreateTransitionAsync(IPage page, int from, int target, bool isDefault)
    {
        var transitions = Section(page, "Übergänge (Branching)");

        await InteractWhenReadyAsync(
            () => transitions.GetByRole(AriaRole.Button, new() { Name = "Neuer Übergang" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#transitionFrom")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#transitionFrom").SelectOptionAsync(new SelectOptionValue { Index = from });
        await page.Locator("#transitionTarget").SelectOptionAsync(new SelectOptionValue { Index = target });
        if (isDefault)
        {
            await page.Locator("#transitionDefault").CheckAsync();
        }

        await transitions.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();
        await Assertions.Expect(page.Locator("#transitionFrom")).ToHaveCountAsync(0, SlowCount);
    }

    /// <summary>
    /// Pflegt die Bedingung des Rücksprungs im Übergangs-Editor (#40) und prüft dabei die
    /// <b>Live-Validierung</b>: Der Ausdruck wird schon beim Tippen gegen den Musterkontext des
    /// Dialogs kompiliert.
    /// </summary>
    private static async Task SetBackJumpConditionAsync(IPage page)
    {
        await Section(page, "Übergänge (Branching)").Locator("tbody tr").Filter(new() { HasText = "Rücksprung" })
            .GetByRole(AriaRole.Button, new() { Name = "Bearbeiten" }).ClickAsync();
        await page.WaitForURLAsync(TransitionUrl);

        await InteractWhenReadyAsync(
            () => page.Locator("#expression").FillAsync("more == \"yes\""),
            () => Assertions.Expect(page.Locator(".expr-status"))
                .ToContainTextAsync("Ausdruck ist gültig", QuickContains));

        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
        await Assertions.Expect(page.Locator(".banner.ok")).ToContainTextAsync("gespeichert", SlowContains);

        await page.Locator("p.back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
    }

    /// <summary>
    /// Markiert den Zyklus als Schleife (#41) – über den Vorschlag, den der Designer für unmarkierte
    /// Rücksprünge selbst anbietet (inklusive vorbelegtem Collection-Schlüssel).
    /// </summary>
    private static async Task MarkLoopAsync(IPage page)
    {
        var loops = Section(page, "Schleifen (Loops)");

        await InteractWhenReadyAsync(
            () => loops.GetByRole(AriaRole.Button, new() { Name = "als Schleife markieren" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#loopKey")).ToBeVisibleAsync(QuickVisible));

        // Der Collection-Schlüssel ist aus dem Rücksprung vorbelegt (LoopFormModel.SuggestCollectionKey).
        await Assertions.Expect(page.Locator("#loopKey")).ToHaveValueAsync("positions", new() { Timeout = 15_000 });

        await loops.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();
        await Assertions.Expect(loops.Locator("tbody tr")).ToHaveCountAsync(1, SlowCount);
    }

    // ---- Test-Runner ---------------------------------------------------------------------------------

    /// <summary>Der Abschnitt mit der offenen Frage bzw. – nach dem letzten Schritt – dem Ergebnis.</summary>
    private static ILocator CurrentStep(IPage page)
        => page.Locator(".editor").Filter(new() { Has = page.Locator("h2", new() { HasTextRegex = new Regex("^(Aktuelle Frage|Ergebnis)$") }) });

    private static async Task AnswerTextAsync(IPage page, string text)
    {
        await CurrentStep(page).Locator(".answer-input input.input").FillAsync(text);
        await CurrentStep(page).GetByRole(AriaRole.Button, new() { Name = "Antworten" }).ClickAsync();
    }

    private static Task ChooseAsync(IPage page, string label)
        => CurrentStep(page).GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();

    // ---- Helfer --------------------------------------------------------------------------------------

    /// <summary>Ein Abschnitt („editor"-Karte) der Seite, adressiert über seine Überschrift.</summary>
    /// <param name="page">Die Seite.</param>
    /// <param name="heading">Der exakte Text der <c>h2</c>-Überschrift.</param>
    private static ILocator Section(IPage page, string heading)
        => page.Locator(".editor").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }) });

    /// <summary>
    /// Führt die <b>erste</b> Interaktion nach einem Seitenwechsel aus und wiederholt sie, bis sie wirkt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Blazor Server ist eine frisch gerenderte Seite zunächst nur vorgerendertes DOM; bis der Circuit
    /// sie übernommen hat, verpuffen Klicks und Eingaben <b>still</b> – kein Fehler, keine Wirkung. Das
    /// gilt nicht nur nach <c>GotoAsync</c>, sondern auch nach jeder <c>NavigateTo</c>-Navigation des
    /// Designers: Der Router ist statisch, jede Seite wird per Enhanced Navigation neu geliefert und ihre
    /// interaktive Komponente erst danach an den Circuit gehängt.
    /// </para>
    /// <para>
    /// Ein zuverlässiges JS-Signal dafür gibt es nicht: <c>window.Blazor.reconnect</c> ist definiert und
    /// die <c>&lt;!--Blazor:…--&gt;</c>-Boot-Marker sind verschwunden, <i>bevor</i> der Circuit Ereignisse
    /// verarbeitet (beides nachgemessen). Deshalb wird die Interaktion wiederholt, bis ihre Wirkung
    /// eintritt – sie muss dafür <b>idempotent</b> sein (ein Formular öffnen, ein Feld füllen, denselben
    /// Wert nochmals speichern).
    /// </para>
    /// </remarks>
    /// <param name="interaction">Die – idempotente – Interaktion.</param>
    /// <param name="verify">Prüfung der Wirkung; sollte ein kurzes Timeout verwenden.</param>
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
                // Der Circuit hatte die Seite noch nicht übernommen – erneut versuchen.
            }
        }
    }

    /// <summary>Kurzes Timeout für die Wirkungsprüfung in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToBeVisibleOptions QuickVisible = new() { Timeout = 2_000 };

    /// <summary>Kurzes Timeout für die Wirkungsprüfung in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToContainTextOptions QuickContains = new() { Timeout = 2_000 };
}
