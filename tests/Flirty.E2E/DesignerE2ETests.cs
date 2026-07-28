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
/// <c>position</c>, sonst weiter auf <c>summary</c>; Schleife <c>position_liste</c>. So beschreiben
/// Service-Tests und E2E denselben Dialog – nur einmal über die Commands, einmal durch die UI.
/// </remarks>
public sealed class DesignerE2ETests : IClassFixture<DesignerAppFixture>
{
    private static readonly LocatorAssertionsToContainTextOptions SlowContains = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveTextOptions SlowText = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveCountOptions SlowCount = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveValueOptions SlowValue = new() { Timeout = 15_000 };

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
        var loopRow = Section(page, "Schleifen (Loops)").Locator("tbody tr").Filter(new() { HasText = "position_liste" });
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
        var collection = Section(page, "Ausdruckskontext").Locator("tbody tr").Filter(new() { HasText = "position_liste" });
        await Assertions.Expect(collection).ToContainTextAsync("Backend", SlowContains);
        await Assertions.Expect(collection).ToContainTextAsync("Frontend", SlowContains);
    }

    /// <summary>
    /// Rauchprobe der Graph-Ansicht (#101): Der Canvas bindet sein JS-Modul, zeichnet den Graphen und
    /// führt über die Auswahl in den bestehenden Frage-Editor. Die vollständige Canvas-Abdeckung bleibt
    /// Stufe 5 (#105) – hier geht es um den Nachweis, dass die Seite im Browser überhaupt lebt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Der Test wartet auf <c>data-canvas-ready</c> statt <c>InteractWhenReadyAsync</c> zu benutzen.
    /// Das Wiederholmuster setzt <b>idempotente</b> Aktionen voraus – für einen Canvas gilt das nicht:
    /// Ein wiederholtes Ziehen verschöbe doppelt, ein wiederholter Zoomschritt zoomte zweimal. Das
    /// Attribut setzt das JS-Modul, sobald es gebunden ist; es ist damit ein echtes Signal statt einer
    /// Vermutung (ADR 0006).
    /// </para>
    /// <para>
    /// Es ist bewusst das <b>erste</b> <c>data-</c>-Attribut im Designer – die übrige Suite adressiert
    /// über Rolle, Überschrift, Feld-<c>id</c> und CSS-Klasse.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Ansicht_zeigt_den_Ablauf_und_fuehrt_in_den_Frage_Editor()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);
        await CreateQuestionTriggerAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Graph ansehen" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));

        // Das Bereitschaftssignal des Canvas – erst danach ist eine Interaktion verlässlich.
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // Drei Fragen, drei Übergänge – derselbe Graph, den die Listenansicht zeigt.
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(3, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(3, SlowCount);

        // Die Marker, die den Ablauf lesbar machen: Einstieg, Abschluss und der Schleifen-Rahmen.
        await Assertions.Expect(page.Locator(".graph-node.is-start")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-terminal")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-loop-label")).ToContainTextAsync("position_liste", SlowContains);

        // Der Trigger hängt als Chip an genau der Frage, nach der er feuert – nicht an allen.
        var triggerNode = page.Locator(".graph-node").Filter(new() { HasText = "summary" });
        await Assertions.Expect(triggerNode.Locator(".chip")).ToContainTextAsync("hooks.test", SlowContains);
        await Assertions.Expect(page.Locator(".graph-node .chip")).ToHaveCountAsync(1, SlowCount);

        // Auswahl öffnet den Inspector. Adressiert wird die Karte über ihre Klasse, nicht über die Rolle:
        // Seit #103 trägt ein Knoten einen zweiten Button (den Ausgangs-Port), und GetByRole(Button) wäre
        // damit eine Strict-Mode-Verletzung.
        await page.Locator(".graph-node").Filter(new() { HasText = "position" }).First
            .Locator(".graph-node-card").ClickAsync();
        var inspector = page.Locator(".graph-inspector");
        await Assertions.Expect(inspector).ToContainTextAsync("Einstiegsfrage", SlowContains);

        // … und der Inspector führt in den bestehenden Editor, statt ihn nachzubauen.
        await inspector.GetByRole(AriaRole.Button, new() { Name = "Frage bearbeiten →" }).ClickAsync();
        await page.WaitForURLAsync(QuestionUrl);
        await Assertions.Expect(page.Locator("#key")).ToHaveValueAsync("position", SlowValue);
    }

    /// <summary>
    /// Rauchprobe der Layout-Persistenz (#102): Ein Knoten wird gezogen, die Position überlebt den
    /// Reload – also den vollständigen Neuaufbau der Seite aus der Datenbank – und „Layout zurücksetzen"
    /// stellt die Position des Auto-Layouts wieder her.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gezogen wird der <b>veröffentlichte</b> Dialog. Damit belegt der Test zugleich das Kernversprechen
    /// der Stufe: Wo jede Graph-Änderung 409 liefert, geht das Verschieben durch (ADR 0007) – hier
    /// sichtbar daran, dass keine Fehlermeldung erscheint und die Position wirklich gespeichert wird.
    /// </para>
    /// <para>
    /// Auch dieser Test wartet auf <c>data-canvas-ready</c> statt <c>InteractWhenReadyAsync</c> zu
    /// benutzen – ein wiederholter Zug verschöbe doppelt. Der Zug läuft über mehrere
    /// <c>Mouse.MoveAsync</c>-Schritte: Ein einziger Sprung erzeugt genau ein <c>pointermove</c>, was die
    /// 4-px-Schwelle zwar überschritte, aber nicht wie eine echte Geste aussähe.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Knoten_verschieben_ueberlebt_den_Reload()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Veröffentlichen" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1 .badge")).ToHaveTextAsync("Veröffentlicht", SlowText);

        await page.GetByRole(AriaRole.Link, new() { Name = "Graph ansehen" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        var node = page.Locator(".graph-node").Filter(new() { HasText = "summary" });
        var pinnedNode = page.Locator(".graph-node.is-pinned").Filter(new() { HasText = "summary" });
        var before = await TransformOfAsync(node);

        // Vor dem Zug ist nichts gepinnt – sonst prüfte der Reload unten eine Position, die schon stand.
        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Layout zurücksetzen" })).ToBeHiddenAsync();

        await DragByAsync(page, node, 180, 120);

        // Der Knoten trägt jetzt eine eigene Position – die Markierung kommt aus dem neu gebauten
        // Modell, also erst nachdem der Server den Zug übernommen hat.
        await Assertions.Expect(pinnedNode).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        var afterDrag = await TransformOfAsync(node);
        Assert.NotEqual(before, afterDrag);

        // Der Nachweis: Nach dem Reload rendert der Server den Knoten aus der Datenbank.
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });
        await Assertions.Expect(pinnedNode).ToHaveCountAsync(1, SlowCount);
        Assert.Equal(afterDrag, await TransformOfAsync(node));

        // Zurücksetzen verwirft die Zeile – danach liegt der Knoten wieder, wo das Auto-Layout ihn hatte.
        await page.GetByRole(AriaRole.Button, new() { Name = "Layout zurücksetzen" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Ja, zurücksetzen" }).ClickAsync();

        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(0, SlowCount);
        Assert.Equal(before, await TransformOfAsync(node));
    }

    /// <summary>
    /// Rauchprobe des Editierens auf dem Canvas (#103): Ein Baustein wird aus der Palette auf die Fläche
    /// gezogen, ein zweiter per Klick angefügt, beide werden am Ausgangs-Port verbunden – und alles
    /// erscheint unmittelbar auch in der Listenansicht.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Der Test beginnt auf einem <b>leeren</b> Dialog. Das ist Absicht: Bis #103 ersetzte ein Hinweis
    /// den Canvas, solange es keine Fragen gab – auf eine nicht vorhandene Fläche lässt sich nichts
    /// ziehen. Der leere Fall ist damit der eigentliche Beweis.
    /// </para>
    /// <para>
    /// Die letzte Prüfung ist die wichtigste: Die Fragen stehen in der Fragenliste des Dialog-Editors.
    /// Der Canvas ruft dieselben Admin-Commands wie die Formulare – es gibt keine zweite Wahrheit.
    /// </para>
    /// <para>
    /// Die vollständige Gesten-Abdeckung (Ziehen ins Leere, Umsortieren, Löschen samt Kaskade, Trigger,
    /// Schleife) bleibt Stufe 5 (#105); jede dieser Regeln ist auf Command-Ebene in
    /// <c>tests/Flirty.Tests/Designer</c> belegt.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Palette_und_Port_legen_Fragen_und_Uebergang_an()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeEmptyDialogAsync(session);

        await page.GetByRole(AriaRole.Link, new() { Name = "Graph ansehen" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // Der leere Dialog zeigt trotzdem eine Zeichenfläche – sonst gäbe es kein Ziel für den Zug.
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(page.Locator(".graph-palette-item").First).ToBeEnabledAsync();

        // 1) Ziehen: Der Baustein landet an der Loslass-Stelle, also mit eigener Position (is-pinned).
        await DragToCanvasAsync(page, page.Locator(".graph-palette-item").First, 260, 140);

        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        // 2) Klick: der zeigerlose Weg. Ohne Position – die vergibt das Auto-Layout.
        await page.Locator(".graph-palette-item").Nth(1).ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);

        // 3) Verbinden: vom Ausgangs-Port des einen Knotens auf den anderen ziehen.
        var quelle = page.Locator(".graph-node").First;
        var ziel = page.Locator(".graph-node").Nth(1);
        await DragToTargetAsync(page, quelle.Locator(".graph-port"), ziel.Locator(".graph-node-card"));

        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);

        // Der Reload beweist, dass alles geschrieben wurde – nicht nur im DOM steht.
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, SlowCount);

        // Keine zweite Wahrheit: Dieselben Fragen stehen in der Liste des Dialog-Editors.
        await page.Locator(".back a").ClickAsync();
        await page.WaitForURLAsync(DialogUrl);
        await Assertions.Expect(Section(page, "Fragen").Locator("tbody tr")).ToHaveCountAsync(2, SlowCount);
        await Assertions.Expect(Section(page, "Übergänge (Branching)").Locator("tbody tr"))
            .ToHaveCountAsync(1, SlowCount);
    }

    /// <summary>
    /// Der Inspector ist seit #103 ein Editor: Kopffelder speichern, verbinden, Default umschalten,
    /// löschen. Der Test prüft die <b>Verdrahtung</b> dieser Pfade – jeder von ihnen ist ein eigener
    /// <c>EventCallback</c> von Panel über Inspector zur Seite, und ein falsch verbundener fiele durch
    /// jeden Unit-Test.
    /// </summary>
    /// <remarks>
    /// Der Schluss ist zugleich der Nachweis für „die Mit-Aufräumung wird sichtbar nachgezogen": Mit der
    /// gelöschten Frage verschwindet die Kante, die an ihr hing – gemeldet als Zählung, nicht behauptet.
    /// </remarks>
    [SkippableFact]
    public async Task Graph_Inspector_bearbeitet_Frage_Uebergang_und_loescht_mit_Kaskade()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeEmptyDialogAsync(session);

        await page.GetByRole(AriaRole.Link, new() { Name = "Graph ansehen" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // Zwei Fragen über den zeigerlosen Weg – hier geht es um den Inspector, nicht um die Geste.
        await page.Locator(".graph-palette-item").First.ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(1, SlowCount);
        await page.Locator(".graph-palette-item").First.ClickAsync();
        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(2, SlowCount);

        var inspector = page.Locator(".graph-inspector");

        // 1) Kopffelder speichern: Der Knoten trägt danach den neuen Schlüssel.
        await SelectNodeAsync(page, page.Locator(".graph-node").First);

        // Adressiert wird über den Schlüssel, nicht über die DOM-Reihenfolge: Die Anordnung entsteht aus
        // Schicht und Spalte und ist keine Zusage an den Test.
        var start = page.Locator(".graph-node").Filter(new() { HasText = "start" });

        // Füllen UND speichern in einer wiederholbaren Einheit, und geprüft wird das Ergebnis am
        // Graphen – nicht der Feldinhalt.
        //
        // Der Grund ist eine Falle, die diesen Test zweimal rot gemacht hat: Ein Blick auf den DOM-Wert
        // beweist NICHT, dass Blazor die Eingabe gesehen hat. Verpufft die erste Interaktion auf einem
        // frisch gerenderten Feld (Blazor Server verdrahtet es erst mit dem nächsten Circuit-Update),
        // steht der getippte Wert trotzdem im DOM – bis der nächste Render ihn mit dem gebundenen Wert
        // überschreibt. Wer in diesem Fenster prüft, sieht Erfolg und speichert den alten Wert.
        // Belastbar ist nur eine Wirkung, die der Server erzeugt hat: der Knoten mit dem neuen Schlüssel.
        // Beides ist idempotent – denselben Wert erneut zu speichern ändert nichts.
        await InteractWhenReadyAsync(
            async () =>
            {
                await page.Locator("#inspectorKey").FillAsync("start");
                await page.Locator("#inspectorText").FillAsync("Wie heißt du?");
                await inspector.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            },
            () => Assertions.Expect(start).ToHaveCountAsync(1, QuickCount));

        // 2) Verbinden über die Auswahlliste – das Tastaturäquivalent zum Ziehen am Port.
        await SelectNodeAsync(page, start);
        await page.Locator("#inspectorConnect").SelectOptionAsync(new SelectOptionValue { Index = 2 });
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Verbinden" }).ClickAsync(),
            () => Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(1, QuickCount));

        // 3) Default umschalten: Die Kante wechselt ihre Kennzeichnung, und damit verschwindet die
        //    Warnung „Kein Default-Übergang" aus dem Graphen.
        await SelectNodeAsync(page, start);
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Default" }).First.ClickAsync(),
            () => Assertions.Expect(page.Locator(".graph-edge.is-default")).ToHaveCountAsync(1, QuickCount));

        // 4) Löschen mit sichtbarer Kaskade: Die Frage geht, ihre Kante geht mit.
        await SelectNodeAsync(page, start);
        await InteractWhenReadyAsync(
            () => inspector.GetByRole(AriaRole.Button, new() { Name = "Löschen" }).ClickAsync(),
            () => Assertions.Expect(inspector).ToContainTextAsync("Ja, löschen", QuickContains));
        await inspector.GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();

        await Assertions.Expect(page.Locator(".graph-node")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-edge")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(page.Locator(".banner.ok")).ToContainTextAsync("mit entfernt", SlowContains);

        // Der Inspector fällt sichtbar auf die Legende zurück – die Auswahl zeigte sonst ins Leere.
        await Assertions.Expect(inspector).ToContainTextAsync("Legende", SlowContains);
    }

    /// <summary>
    /// Bei veröffentlichtem Dialog sind die Graph-Gesten <b>deaktiviert</b> statt in einen Konflikt zu
    /// laufen: Es gibt keinen Ausgangs-Port, die Palette ist gesperrt, und der Hinweis bietet die neue
    /// Version an. Verschieben funktioniert weiter (ADR 0007) – und erzeugt keine Fehlermeldung.
    /// </summary>
    [SkippableFact]
    public async Task Graph_Gesten_sind_bei_veroeffentlichtem_Dialog_deaktiviert()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDialogAsync(session);

        await page.GetByRole(AriaRole.Button, new() { Name = "Veröffentlichen" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1 .badge")).ToHaveTextAsync("Veröffentlicht", SlowText);

        await page.GetByRole(AriaRole.Link, new() { Name = "Graph ansehen" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/graph$"));
        await page.WaitForSelectorAsync("svg[data-canvas-ready='true']", new() { Timeout = 30_000 });

        // Das Attribut ist C#-Zustand: Das JS-Modul liest es bei jeder Geste, statt ihn beim Binden zu
        // kopieren.
        await Assertions.Expect(page.Locator("svg[data-editable='false']")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".graph-port")).ToHaveCountAsync(0, SlowCount);
        await Assertions.Expect(page.Locator(".graph-palette-item").First).ToBeDisabledAsync();
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Neue Version anlegen" })).ToBeVisibleAsync();

        // Verschieben bleibt erlaubt – und läuft nicht in einen Konflikt.
        await DragByAsync(page, page.Locator(".graph-node").Filter(new() { HasText = "summary" }), 150, 90);

        await Assertions.Expect(page.Locator(".graph-node.is-pinned")).ToHaveCountAsync(1, SlowCount);
        await Assertions.Expect(page.Locator(".banner.err")).ToHaveCountAsync(0, SlowCount);
    }

    /// <summary>Liest das <c>transform</c> eines Knotens – die im DOM sichtbare Position.</summary>
    private static async Task<string?> TransformOfAsync(ILocator node)
        => await node.GetAttributeAsync("transform");

    /// <summary>
    /// Zieht ein Element um den angegebenen Versatz. Bewusst über <c>Mouse</c> statt
    /// <c>DragToAsync</c>: Letzteres nutzt die HTML5-Drag-and-Drop-API, die auf einem SVG-Canvas mit
    /// Pointer-Events gar nicht auslöst.
    /// </summary>
    private static async Task DragByAsync(IPage page, ILocator target, int deltaX, int deltaY)
    {
        // Der Canvas-Host ist 70vh hoch und steht unter Kopfzeile, Hinweis und Werkzeugleiste – ein
        // Knoten der unteren Schichten liegt damit leicht außerhalb des Fensters. Mouse-Koordinaten
        // sind fensterbezogen; ohne das Scrollen zielte die Geste ins Leere.
        await target.ScrollIntoViewIfNeededAsync();

        var box = await target.BoundingBoxAsync();
        Assert.NotNull(box);

        const int steps = 5;
        var startX = box.X + (box.Width / 2);
        var startY = box.Y + (box.Height / 2);

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();

        // Mehrere Schritte: eine echte Geste, und der erste überschreitet die 4-px-Schwelle des Moduls.
        for (var step = 1; step <= steps; step++)
        {
            await page.Mouse.MoveAsync(
                startX + (deltaX * step / (float)steps), startY + (deltaY * step / (float)steps));
        }

        await page.Mouse.UpAsync();
    }

    /// <summary>
    /// Zieht ein Element auf eine Stelle der Zeichenfläche – die Palette-Geste (#103).
    /// </summary>
    /// <remarks>
    /// Wie <see cref="DragByAsync"/> über <c>Mouse</c>: Die Palette-Einträge sind zwar HTML außerhalb des
    /// SVG, ihre Geste läuft aber im selben Pointer-Events-Modell wie der Canvas – <c>DragToAsync</c>
    /// (HTML5-Drag-and-Drop) löst dort nichts aus.
    /// </remarks>
    /// <param name="page">Die Seite.</param>
    /// <param name="source">Der Palette-Eintrag.</param>
    /// <param name="offsetX">Der waagerechte Abstand vom linken Rand der Zeichenfläche in px.</param>
    /// <param name="offsetY">Der senkrechte Abstand von deren oberem Rand in px.</param>
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

    /// <summary>Zieht von einem Element auf die Mitte eines anderen – die Verbindungsgeste (#103).</summary>
    /// <param name="page">Die Seite.</param>
    /// <param name="source">Der Ausgangs-Port.</param>
    /// <param name="target">Das Ziel (die Knotenkarte).</param>
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
    /// Der gemeinsame Zug zwischen zwei Fensterkoordinaten. Mehrere Schritte, damit der erste die
    /// 4-px-Schwelle des Moduls überschreitet und die Geste wie eine echte aussieht.
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
    /// Legt einen Dialog <b>ohne</b> Fragen an – die Ausgangslage für die Canvas-Gesten, die ihre Fragen
    /// selbst anlegen.
    /// </summary>
    /// <param name="session">Die Browser-Sitzung des Tests.</param>
    /// <returns>Die Seite, die auf dem leeren Dialog-Editor steht.</returns>
    private async Task<IPage> ArrangeEmptyDialogAsync(PlaywrightSession session)
    {
        var page = await session.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/dialogs");

        await InteractWhenReadyAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Neuer Dialog" }).ClickAsync(),
            () => Assertions.Expect(page.Locator("#key")).ToBeVisibleAsync(QuickVisible));

        await page.Locator("#key").FillAsync($"e2e-{Guid.NewGuid():N}"[..12]);
        await page.Locator("#name").FillAsync("E2E-Canvas-Dialog");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();

        await page.WaitForURLAsync(DialogUrl);

        return page;
    }

    /// <summary>
    /// Legt einen Webhook-Trigger auf die Abschlussfrage an – die Vorbedingung dafür, dass die
    /// Graph-Ansicht überhaupt einen Trigger-Chip zeigen kann.
    /// </summary>
    /// <param name="page">Die Seite, die auf dem Dialog-Editor steht.</param>
    private static async Task CreateQuestionTriggerAsync(IPage page)
    {
        await Section(page, "Trigger").GetByRole(AriaRole.Button, new() { Name = "Neuer Trigger" }).ClickAsync();
        await page.Locator("#triggerScope").SelectOptionAsync("AfterQuestion");

        // Das Frage-Auswahlfeld erscheint erst, wenn der Zeitpunkt eine Frage verlangt.
        await Assertions.Expect(page.Locator("#triggerQuestion")).ToBeVisibleAsync(QuickVisible);
        await page.Locator("#triggerQuestion").SelectOptionAsync(new SelectOptionValue { Index = SummaryOption });
        await page.Locator("#triggerUrl").FillAsync("https://hooks.test/fertig");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();

        await Assertions.Expect(Section(page, "Trigger").Locator("tbody tr")).ToHaveCountAsync(1, SlowCount);
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
        await Assertions.Expect(page.Locator("#loopKey")).ToHaveValueAsync("position_liste", new() { Timeout = 15_000 });

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

    /// <summary>Kurzes Timeout für die Wirkungsprüfung in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToHaveValueOptions QuickValue = new() { Timeout = 2_000 };

    /// <summary>Kurzes Timeout für die Wirkungsprüfung in <see cref="InteractWhenReadyAsync"/>.</summary>
    private static readonly LocatorAssertionsToHaveCountOptions QuickCount = new() { Timeout = 2_000 };

    /// <summary>
    /// Wählt einen Knoten aus und wartet, bis das Inspector-Panel dazu steht.
    /// </summary>
    /// <remarks>
    /// Wiederholt, weil die Auswahl ein frisch gerendertes Panel erzeugt und die erste Interaktion darauf
    /// verpuffen kann. Eine Auswahl ist idempotent – zweimal denselben Knoten zu wählen ändert nichts.
    /// </remarks>
    private static async Task SelectNodeAsync(IPage page, ILocator node)
        => await InteractWhenReadyAsync(
            () => node.Locator(".graph-node-card").ClickAsync(),
            () => Assertions.Expect(page.Locator("#inspectorKey")).ToBeVisibleAsync(QuickVisible));
}
