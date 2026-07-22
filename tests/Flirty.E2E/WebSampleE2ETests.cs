using Microsoft.Playwright;

namespace Flirty.E2E;

/// <summary>
/// Playwright-E2E der Web-Sample-Chat-UI (#45/#47) gegen ein echtes, in-Prozess gehostetes Kestrel
/// (<see cref="WebSampleAppFixture"/>). Deckt das Akzeptanzkriterium des Issues in beide Richtungen ab:
/// <b>Branching</b> (dev-Zweig <i>und</i> Default-Zweig), <b>Loop über Liste</b> (zwei Iterationen inkl.
/// Abschluss, In-Process-Trigger und vollem Outbound→Inbound-Webhook-Rundlauf), <b>Reload→Resume</b>
/// mitten in der Schleife sowie das <b>Editieren einer früheren Antwort</b> (Freitext, Verzweigungsfrage,
/// gezielte Loop-Iteration). Sind keine Playwright-Browser installiert, überspringen sich die Tests
/// (<see cref="SkippableFactAttribute"/>) – Installation z. B. via
/// <c>pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium</c>.
/// </summary>
/// <remarks>
/// <para>
/// Die Tests teilen sich über die Fixture <b>eine</b> App samt Datenbank, bekommen aber je einen frischen
/// Browser-Context (leeres <c>localStorage</c> → eigener <c>externalUserKey</c> → eigene Session). Was sie
/// sich dadurch <i>doch</i> teilen, sind die Singletons hinter den beiden Trigger-Panels
/// (<c>TriggerLog</c>, <c>WebhookInbox</c>): jeder Test, der den Dialog abschließt, schreibt dort hinein.
/// Die Panel-Assertions sind deshalb bewusst <c>Contains</c>-basiert und dürfen nicht auf eine Anzahl
/// umgestellt werden.
/// </para>
/// <para>
/// Die Chat-UI verwirft bei jedem Render den kompletten Verlauf und baut ihn aus dem Server-Zustand neu auf
/// (<c>refreshAndRender</c>). Es gibt daher stets genau <b>eine</b> Bot-Blase – die offene Frage –, was die
/// Prüfung „welche Frage steht gerade offen?" über <see cref="CurrentPromptKey"/> exakt macht, und die
/// Reihenfolge der Antwort-Blasen entspricht der Sequenz auf dem Server.
/// </para>
/// </remarks>
public sealed class WebSampleE2ETests : IClassFixture<WebSampleAppFixture>
{
    private static readonly LocatorAssertionsToContainTextOptions SlowContains = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveTextOptions SlowText = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveValueOptions SlowValue = new() { Timeout = 15_000 };

    private readonly WebSampleAppFixture _fixture;

    /// <summary>Initialisiert den Test mit dem gemeinsam genutzten App-Host.</summary>
    /// <param name="fixture">Der in-Prozess gehostete Sample-App-Host.</param>
    public WebSampleE2ETests(WebSampleAppFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Der vollständige Durchlauf: dev-Zweig, zwei Schleifen-Iterationen, Abschluss – und der Nachweis,
    /// dass der Abschluss den In-Process-Handler auslöst <b>und</b> der ausgehende Webhook beim eigenen
    /// Inbound-Empfänger ankommt (der Rundlauf braucht echtes Kestrel und ist nur hier prüfbar).
    /// </summary>
    [SkippableFact]
    public async Task Durchlauf_Branching_Loop_und_Trigger_Rundlauf()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        // Loop über Liste: zwei skill-Iterationen (Ja = Loop-Back), dann Nein = Exit.
        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "Ja");
        await FillAndSendAsync(page, "Blazor");
        await ChooseAsync(page, "Nein");

        // Abschlussfrage (Boolean) -> Dialog abgeschlossen.
        await ChooseAsync(page, "Ja");

        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("abgeschlossen", SlowContains);

        // Die Iterations-Badges belegen, dass die Schleife wirklich gesammelt hat: beide Loop-Fragen
        // (Einstieg skill und Breaking Question more) tragen je Durchlauf einen eigenen Index.
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(
            ["role", "language", "skill #1", "more #1", "skill #2", "more #2", "summary"], SlowText);
        await Assertions.Expect(page.Locator("#skillsList li")).ToHaveTextAsync(["EF Core", "Blazor"], SlowText);

        // In-Process-Handler und Outbound→Inbound-Webhook-Rundlauf werden im Panel sichtbar (Polling).
        await Assertions.Expect(page.Locator("#triggersList")).ToContainTextAsync("web-onboarding", SlowContains);
        await Assertions.Expect(page.Locator("#webhooksList")).ToContainTextAsync("OnDialogCompleted", SlowContains);
    }

    /// <summary>
    /// Die Gegenprobe zum dev-Zweig: „Product Manager" trifft keine Bedingung, also greift der
    /// <c>IsDefault</c>-Übergang auf <c>product</c> – und beide Zweige laufen anschließend in dieselbe
    /// Schleife.
    /// </summary>
    [SkippableFact]
    public async Task Branching_Default_Zweig_fuehrt_ueber_product_in_die_Schleife()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await session.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await ChooseAsync(page, "Product Manager");

        // Nicht language: die Bedingung role == "dev" trifft nicht, der Default-Übergang greift.
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("product", SlowText);

        await FillAndSendAsync(page, "Flirty");

        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("skill", SlowText);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(["role", "product"], SlowText);
    }

    /// <summary>
    /// Reload <b>mitten in der Schleife</b>: Nach dem Neuladen kommt der komplette Verlauf über
    /// <c>GET /flirty/sessions/{id}</c> vom Server – inklusive der Iterationszuordnung der bereits
    /// gesammelten Antworten und der offenen Frage.
    /// </summary>
    [SkippableFact]
    public async Task Reload_stellt_die_Session_mitten_in_der_Schleife_wieder_her()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "Ja");
        await FillAndSendAsync(page, "Blazor");
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("more", SlowText);

        await page.ReloadAsync();

        // Exakt statt Contains: „Resume" steht auch im Statustext des serverseitigen Resume-Pfads
        // (POST /flirty/sessions mit isResumed) – geprüft werden soll hier der localStorage-Pfad.
        await Assertions.Expect(page.Locator("#statusLine"))
            .ToHaveTextAsync("Session nach Reload wiederhergestellt (Resume).", SlowText);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(
            ["role", "language", "skill #1", "more #1", "skill #2"], SlowText);
        await Assertions.Expect(page.Locator("#skillsList li")).ToHaveTextAsync(["EF Core", "Blazor"], SlowText);
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("more", SlowText);
    }

    /// <summary>
    /// Editieren einer Freitext-Antwort: Der neue Wert ersetzt den alten, alle <b>nachgelagerten</b>
    /// Antworten werden verworfen und der Pfad wird ab der editierten Frage neu berechnet.
    /// </summary>
    [SkippableFact]
    public async Task Editieren_einer_Antwort_verwirft_nachgelagerte_Antworten()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");

        await EditAsync(page, "language");

        // Vorbelegt ist der gespeicherte Wert – nicht seine Anzeigeform.
        await Assertions.Expect(EditField(page)).ToHaveValueAsync("C#", SlowValue);
        await SaveEditAsync(page, "Rust");

        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("1 nachgelagerte Antwort(en) verworfen", SlowContains);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(["role", "language"], SlowText);
        await Assertions.Expect(Bubble(page, "language")).ToContainTextAsync("Rust", SlowContains);
        await Assertions.Expect(page.Locator("#skillsList")).ToContainTextAsync("Noch keine Fähigkeit erfasst", SlowContains);

        // Der Pfad wurde ab language neu berechnet – die Schleife beginnt von vorn.
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("skill", SlowText);
    }

    /// <summary>
    /// Der Hauptfall des Issues: die <b>Verzweigungsfrage</b> nachträglich ändern. Aus dev wird pm, also
    /// berechnet die Engine den Pfad neu und schaltet auf den Default-Zweig – die Antworten des dev-Zweigs
    /// sind damit hinfällig und werden verworfen.
    /// </summary>
    /// <remarks>
    /// Zugleich der Regressionstest zum Fehler, den dieser Test aufgedeckt hat: Das Edit-Formular hat für
    /// jede Frage ein Textfeld gerendert und dieses mit der <i>Anzeigeform</i> vorbelegt. Bei einer
    /// Einfachauswahl wurde damit das Label („Product Manager") statt des Werts („pm") gespeichert – der
    /// <c>AnswerValidator</c> lehnte das mit 400 ab, die Statuszeile zeigte nur „Fehler: 400 …".
    /// </remarks>
    [SkippableFact]
    public async Task Editieren_der_Verzweigungsfrage_wechselt_den_Zweig()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");

        await EditAsync(page, "role");
        // Einfachauswahl: das Edit-Formular bietet dieselben Options-Buttons wie die normale Eingabe,
        // der Klick speichert direkt.
        await ChooseAsync(page, "Product Manager");

        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("2 nachgelagerte Antwort(en) verworfen", SlowContains);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(["role"], SlowText);
        await Assertions.Expect(Bubble(page, "role")).ToContainTextAsync("Product Manager", SlowContains);
        await Assertions.Expect(page.Locator("#skillsList")).ToContainTextAsync("Noch keine Fähigkeit erfasst", SlowContains);

        // Der Zweig ist gewechselt: statt language steht jetzt product offen.
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("product", SlowText);
    }

    /// <summary>
    /// Editieren innerhalb der Schleife: Die UI schickt den <c>iterationIndex</c> der angeklickten Blase
    /// mit, sodass gezielt <i>diese</i> Iteration überschrieben wird. Der abgeschlossene Dialog wird dabei
    /// wieder geöffnet, weil die Neuberechnung auf eine nicht-terminale Frage führt.
    /// </summary>
    [SkippableFact]
    public async Task Editieren_einer_Loop_Iteration_trifft_genau_diese_Iteration()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "Ja");
        await FillAndSendAsync(page, "Blazor");
        await ChooseAsync(page, "Nein");
        await ChooseAsync(page, "Ja");
        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("abgeschlossen", SlowContains);

        await EditAsync(page, "skill #2");
        await SaveEditAsync(page, "Rust");

        // Verworfen werden nur die nach der zweiten Iteration gegebenen Antworten (more #2, summary).
        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("2 nachgelagerte Antwort(en) verworfen", SlowContains);
        await Assertions.Expect(AnsweredKeys(page)).ToHaveTextAsync(
            ["role", "language", "skill #1", "more #1", "skill #2"], SlowText);

        // Iteration 1 bleibt unangetastet, nur Iteration 2 trägt den neuen Wert.
        await Assertions.Expect(page.Locator("#skillsList li")).ToHaveTextAsync(["EF Core", "Rust"], SlowText);

        // Die abgeschlossene Session ist wieder offen – bei der Breaking Question der Schleife.
        await Assertions.Expect(page.Locator(".msg--system")).ToHaveCountAsync(0);
        await Assertions.Expect(CurrentPromptKey(page)).ToHaveTextAsync("more", SlowText);
    }

    /// <summary>
    /// Die Ja/Nein-Frage am Ende erneut mit „Ja" beantworten: Der Wert muss <b>erhalten</b> bleiben. Der
    /// Test klingt trivial, ist aber die zweite Hälfte des oben beschriebenen Fehlers – und die
    /// gefährlichere: Ein Textfeld mit der Anzeigeform „Ja" lief beim Speichern durch
    /// <c>encodeAnswer</c>, das alles außer <c>"true"</c> auf <c>false</c> abbildet. Die Antwort kippte
    /// also <b>still</b> auf „Nein", ohne Fehlermeldung.
    /// </summary>
    [SkippableFact]
    public async Task Editieren_einer_Ja_Nein_Antwort_behaelt_den_gewaehlten_Wert()
    {
        await using var session = await PlaywrightSession.LaunchAsync();
        var page = await ArrangeDevBranchAsync(session);

        await FillAndSendAsync(page, "EF Core");
        await ChooseAsync(page, "Nein");
        await ChooseAsync(page, "Ja");
        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("abgeschlossen", SlowContains);

        await EditAsync(page, "summary");
        await ChooseAsync(page, "Ja");

        // summary ist terminal: es gibt nichts zu verwerfen, die Session bleibt abgeschlossen.
        await Assertions.Expect(page.Locator("#statusLine"))
            .ToContainTextAsync("0 nachgelagerte Antwort(en) verworfen", SlowContains);
        await Assertions.Expect(Bubble(page, "summary")).ToContainTextAsync("Ja", SlowContains);
        await Assertions.Expect(Bubble(page, "summary")).Not.ToContainTextAsync("Nein", SlowContains);
        await Assertions.Expect(page.Locator(".msg--system")).ToContainTextAsync("abgeschlossen", SlowContains);
    }

    // ---- Ablauf-Helfer -------------------------------------------------------------------------------

    /// <summary>
    /// Öffnet die Chat-UI und beantwortet den Einstieg im dev-Zweig (Rolle „Entwickler", Sprache „C#"),
    /// sodass anschließend die Einstiegsfrage der Schleife offen steht.
    /// </summary>
    /// <param name="session">Die Browser-Sitzung des Tests.</param>
    /// <returns>Die Seite mit der geöffneten Chat-UI.</returns>
    private async Task<IPage> ArrangeDevBranchAsync(PlaywrightSession session)
    {
        var page = await session.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await ChooseAsync(page, "Entwickler");
        await FillAndSendAsync(page, "C#");

        return page;
    }

    /// <summary>Füllt das Eingabefeld der Eingabezeile und sendet die Antwort.</summary>
    private static async Task FillAndSendAsync(IPage page, string text)
    {
        await EditField(page).FillAsync(text);
        await InputArea(page).GetByRole(AriaRole.Button, new() { Name = "Senden", Exact = true }).ClickAsync();
    }

    /// <summary>
    /// Klickt einen Auswahl-Button der Eingabezeile (Antwortoption bzw. Ja/Nein). Bewusst auf die
    /// Eingabezeile eingegrenzt: dort erscheinen sowohl die Buttons der offenen Frage als auch die des
    /// Edit-Formulars.
    /// </summary>
    private static Task ChooseAsync(IPage page, string label)
        => InputArea(page).GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();

    /// <summary>Öffnet das Edit-Formular über den Stift an der Antwort-Blase.</summary>
    /// <param name="page">Die Seite.</param>
    /// <param name="keyBadge">Das Schlüssel-Badge der Blase, z. B. <c>language</c> oder <c>skill #2</c>.</param>
    private static Task EditAsync(IPage page, string keyBadge)
        => Bubble(page, keyBadge).Locator(".msg__edit").ClickAsync();

    /// <summary>Überschreibt den Wert im Edit-Formular einer Freitext-/Zahlen-/Datumsfrage und speichert.</summary>
    private static async Task SaveEditAsync(IPage page, string text)
    {
        await EditField(page).FillAsync(text);
        await InputArea(page).GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
    }

    // ---- Locator-Helfer ------------------------------------------------------------------------------

    /// <summary>Die Eingabezeile – trägt je nach Zustand die offene Frage oder das Edit-Formular.</summary>
    private static ILocator InputArea(IPage page) => page.Locator(".chat__input");

    /// <summary>Das Texteingabefeld der Eingabezeile.</summary>
    private static ILocator EditField(IPage page) => InputArea(page).Locator("input.field");

    /// <summary>Die Antwort-Blase zum angegebenen Schlüssel-Badge.</summary>
    private static ILocator Bubble(IPage page, string keyBadge)
        => page.Locator($".msg--user:has-text('{keyBadge}')");

    /// <summary>Die Schlüssel-Badges aller Antwort-Blasen in Antwort-Reihenfolge (inkl. Iterations-Index).</summary>
    private static ILocator AnsweredKeys(IPage page) => page.Locator(".msg--user .msg__key");

    /// <summary>Das Schlüssel-Badge der aktuell offenen Frage; leer, wenn der Dialog abgeschlossen ist.</summary>
    private static ILocator CurrentPromptKey(IPage page) => page.Locator(".msg--bot .msg__key");
}
