using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für das <see cref="FlirtyAdminGateway"/> (#38): Ausführung der Admin-CRUD-Nachrichten gegen das
/// aktive Connection-Profil, das Fehler-Mapping auf anzeigbare Meldungen und – als Regression – dass ein
/// Profilwechsel sofort greift (der scoped <see cref="FlirtyDbContext"/> lebt sonst über den ganzen
/// Blazor-Circuit und bliebe an das zuerst benutzte Profil gepinnt).
/// </summary>
public sealed class FlirtyAdminGatewayTests
{
    [Fact]
    public async Task ExecuteAsync_legt_Dialog_an_und_liefert_ihn_in_der_Liste()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);

            var created = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateDialogCommand("onboarding", "Onboarding", "Beschreibung"), token));

            Assert.True(created.Success, created.Error);
            Assert.Equal("onboarding", created.Value!.Key);
            Assert.False(created.Value.IsPublished);

            var listed = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));

            Assert.True(listed.Success, listed.Error);
            Assert.Contains(listed.Value!, dialog => dialog.Id == created.Value.Id);
        });
    }

    [Fact]
    public async Task ExecuteAsync_meldet_Konflikt_bei_doppeltem_Schluessel()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateDialogCommand("onboarding", "Onboarding", null), token));

            var zweiter = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateDialogCommand("onboarding", "Noch mal", null), token));

            Assert.False(zweiter.Success);
            Assert.Contains("onboarding", zweiter.Error);
        });
    }

    [Fact]
    public async Task ExecuteAsync_meldet_unbekannten_Dialog()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);

            var result = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new GetDialogQuery(Guid.NewGuid()), token));

            Assert.False(result.Success);
            Assert.Contains("Kein Dialog", result.Error);
        });
    }

    [Fact]
    public async Task ExecuteAsync_meldet_fehlendes_Connection_Profil()
    {
        await RunWithTempDbAsync(async (gateway, _, _) =>
        {
            // Bewusst KEIN Activate: die FlirtyDesignerDbContextFactory muss verständlich melden.
            var result = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));

            Assert.False(result.Success);
            Assert.Contains("Verbindungen", result.Error);
        });
    }

    [Fact]
    public async Task ExecuteAsync_meldet_nicht_migrierte_Datenbank()
    {
        var store = new DesignerTestHost.InMemoryConnectionProfileStore();
        using var provider = DesignerTestHost.BuildProvider(store);
        using var scope = provider.CreateScope();

        // Erreichbare, aber leere Datenbank (Mode=Memory -> kein Dateimüll): das Schema fehlt, weil nie
        // migriert wurde. SQLite meldet "no such table: Dialogs" -> muss als Hinweis auf „Migrieren“ ankommen.
        var profile = new ConnectionProfile
        {
            Name = "Nicht migriert",
            Provider = FlirtyDatabaseProvider.Sqlite,
            ConnectionString = $"Data Source=nicht-migriert-{Guid.NewGuid():N};Mode=Memory;Pooling=False",
        };
        scope.ServiceProvider.GetRequiredService<ActiveConnectionProfile>().Activate(profile);

        var result = await scope.ServiceProvider.GetRequiredService<FlirtyAdminGateway>()
            .ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));

        Assert.False(result.Success);
        Assert.Contains("Migrieren", result.Error);
    }

    /// <summary>
    /// Regression zum Blazor-Circuit-Problem: nach einem Profilwechsel muss die nächste Operation gegen
    /// die <b>neue</b> Datenbank laufen. Das funktioniert nur, weil das Gateway je Operation einen
    /// frischen DI-Scope (und damit einen frischen <see cref="FlirtyDbContext"/>) öffnet.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_verwendet_nach_Profilwechsel_die_neue_Datenbank()
    {
        await RunWithTempDbAsync(async (gateway, active, profilA) =>
        {
            await RunWithTempDbAsync(async (_, _, profilB) =>
            {
                active.Activate(profilA);
                var created = await gateway.ExecuteAsync((sender, token) =>
                    sender.Send(new CreateDialogCommand("nur-in-a", "Nur in A", null), token));
                Assert.True(created.Success, created.Error);

                active.Activate(profilB);
                var inB = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
                Assert.True(inB.Success, inB.Error);
                Assert.Empty(inB.Value!);

                active.Activate(profilA);
                var inA = await gateway.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
                Assert.True(inA.Success, inA.Error);
                Assert.Single(inA.Value!);
            });
        });
    }

    /// <summary>
    /// Frage-Editor (#39): Fragen und Antwortoptionen laufen über dieselben Admin-Commands und müssen
    /// im <c>GetDialogQuery</c> sortiert wieder auftauchen – darauf baut die Anzeige der Fragenliste auf.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_legt_Frage_mit_Optionen_an_und_liefert_sie_sortiert_im_Dialog_Graphen()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");

            var frage = await gateway.ExecuteAsync((sender, token) => sender.Send(
                new CreateQuestionCommand(
                    dialogId, "farbe", "Welche Farbe?", QuestionType.SingleChoice, 0, true, null),
                token));
            Assert.True(frage.Success, frage.Error);

            // Bewusst in verdrehter Reihenfolge anlegen: die Projektion muss nach Order sortieren.
            foreach (var (key, order) in new[] { ("gruen", 1), ("rot", 0) })
            {
                var option = await gateway.ExecuteAsync((sender, token) => sender.Send(
                    new CreateAnswerOptionCommand(dialogId, frage.Value!.Id, key, key, key, order), token));
                Assert.True(option.Success, option.Error);
            }

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            var geladen = Assert.Single(detail.Value!.Questions);
            Assert.Equal(QuestionType.SingleChoice, geladen.Type);
            Assert.Equal(["rot", "gruen"], geladen.Options.Select(option => option.Key));
        });
    }

    /// <summary>
    /// Der Frage-Editor schreibt beim Sortieren mehrere <c>UpdateQuestionCommand</c>s in <b>einem</b>
    /// <see cref="FlirtyAdminGateway.ExecuteAsync{TValue}"/>-Aufruf (ein Scope, ein Fehlerpfad).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_vertauscht_die_Reihenfolge_zweier_Fragen_in_einer_Operation()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");

            var erste = await CreateQuestionAsync(gateway, dialogId, "vorname", 0);
            var zweite = await CreateQuestionAsync(gateway, dialogId, "alter", 1);

            var getauscht = await gateway.ExecuteAsync(async (sender, token) =>
            {
                _ = await sender.Send(
                    new UpdateQuestionCommand(
                        dialogId, zweite.Id, zweite.Key, zweite.Text, zweite.Type, 0, zweite.IsRequired, null),
                    token);
                return await sender.Send(
                    new UpdateQuestionCommand(
                        dialogId, erste.Id, erste.Key, erste.Text, erste.Type, 1, erste.IsRequired, null),
                    token);
            });
            Assert.True(getauscht.Success, getauscht.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Equal(["alter", "vorname"], detail.Value!.Questions.Select(question => question.Key));
        });
    }

    /// <summary>
    /// Löscht der Frage-Editor die Einstiegsfrage, muss der Dialog wieder ohne Einstiegsfrage dastehen –
    /// die Ansicht sperrt daraufhin das Veröffentlichen.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_setzt_beim_Loeschen_der_Einstiegsfrage_die_Einstiegsfrage_zurueck()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var frage = await CreateQuestionAsync(gateway, dialogId, "vorname", 0);

            var gesetzt = await gateway.ExecuteAsync((sender, token) => sender.Send(
                new UpdateDialogCommand(dialogId, "onboarding", "Onboarding", null, frage.Id), token));
            Assert.True(gesetzt.Success, gesetzt.Error);
            Assert.Equal(frage.Id, gesetzt.Value!.StartQuestionId);

            var geloescht = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteQuestionCommand(dialogId, frage.Id), token));
            Assert.True(geloescht.Success, geloescht.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Null(detail.Value!.Dialog.StartQuestionId);
            Assert.Empty(detail.Value.Questions);
        });
    }

    /// <summary>
    /// Branching-Editor (#40): Übergänge laufen über dieselben Admin-Commands und müssen mit Bedingung,
    /// Priorität und Default-Kennzeichen im <c>GetDialogQuery</c> wieder auftauchen – darauf baut die
    /// Übergangsliste des Dialog-Editors auf.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_legt_Uebergang_an_und_loescht_ihn_wieder()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var role = await CreateQuestionAsync(gateway, dialogId, "role", 0);
            var language = await CreateQuestionAsync(gateway, dialogId, "language", 1);

            var angelegt = await gateway.ExecuteAsync((sender, token) => sender.Send(
                new CreateTransitionCommand(dialogId, role.Id, language.Id, "role == \"dev\"", 0, false), token));
            Assert.True(angelegt.Success, angelegt.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));
            Assert.True(detail.Success, detail.Error);

            var geladen = Assert.Single(detail.Value!.Transitions);
            Assert.Equal(role.Id, geladen.FromQuestionId);
            Assert.Equal(language.Id, geladen.TargetQuestionId);
            Assert.Equal("role == \"dev\"", geladen.Expression);
            Assert.False(geladen.IsDefault);

            var geloescht = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteTransitionCommand(dialogId, geladen.Id), token));
            Assert.True(geloescht.Success, geloescht.Error);

            var danach = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));
            Assert.Empty(danach.Value!.Transitions);
        });
    }

    /// <summary>
    /// Die ↑/↓-Schaltflächen schreiben den Positionsindex als neue <c>Priority</c> – in <b>einer</b>
    /// Gateway-Operation. Der Test startet bewusst mit lückenhaften Prioritäten (5/9): ein bloßes
    /// Vertauschen der Zahlen bliebe hier folgenlos.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_vergibt_die_Prioritaeten_der_Uebergaenge_in_einer_Operation_neu()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var role = await CreateQuestionAsync(gateway, dialogId, "role", 0);
            var language = await CreateQuestionAsync(gateway, dialogId, "language", 1);
            var product = await CreateQuestionAsync(gateway, dialogId, "product", 2);

            var bedingt = await CreateTransitionAsync(gateway, dialogId, role.Id, language.Id, "role == \"dev\"", 5, false);
            var standard = await CreateTransitionAsync(gateway, dialogId, role.Id, product.Id, null, 9, true);

            var sortiert = await gateway.ExecuteAsync(async (sender, token) =>
            {
                _ = await sender.Send(
                    new UpdateTransitionCommand(
                        dialogId, standard.Id, standard.FromQuestionId, standard.TargetQuestionId,
                        standard.Expression, 0, standard.IsDefault),
                    token);
                return await sender.Send(
                    new UpdateTransitionCommand(
                        dialogId, bedingt.Id, bedingt.FromQuestionId, bedingt.TargetQuestionId,
                        bedingt.Expression, 1, bedingt.IsDefault),
                    token);
            });
            Assert.True(sortiert.Success, sortiert.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Equal([standard.Id, bedingt.Id], detail.Value!.Transitions.Select(transition => transition.Id));
            Assert.Equal([0, 1], detail.Value.Transitions.Select(transition => transition.Priority));
        });
    }

    /// <summary>
    /// Der Musterkontext der Ausdrucks-Validierung braucht die Loop-Collections, sonst gälte
    /// <c>skills.Count &gt; 0</c> im Designer als unbekannter Bezeichner. Geprüft wird deshalb der ganze
    /// Weg des Loop-CRUD (#41): anlegen, im Dialog-Graphen wiederfinden, ändern und löschen.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_legt_Loop_Marker_an_aendert_und_loescht_ihn()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var skill = await CreateQuestionAsync(gateway, dialogId, "skill", 0);
            var more = await CreateQuestionAsync(gateway, dialogId, "more", 1);

            var created = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", skill.Id, more.Id), token));

            Assert.True(created.Success, created.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            var loop = Assert.Single(detail.Value!.Loops);
            Assert.Equal("skills", loop.CollectionKey);
            Assert.Equal(skill.Id, loop.EntryQuestionId);
            Assert.Equal(more.Id, loop.BreakingQuestionId);

            var geaendert = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new UpdateLoopCommand(dialogId, loop.Id, "faehigkeiten", skill.Id, more.Id), token));

            Assert.True(geaendert.Success, geaendert.Error);
            Assert.Equal("faehigkeiten", geaendert.Value!.CollectionKey);

            var geloescht = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteLoopCommand(dialogId, loop.Id), token));

            Assert.True(geloescht.Success, geloescht.Error);

            var danach = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(danach.Success, danach.Error);
            Assert.Empty(danach.Value!.Loops);
        });
    }

    /// <summary>
    /// Zwei Marker mit demselben Collection-Schlüssel würden sich zur Laufzeit still überschreiben
    /// (der zuletzt aufgebaute gewinnt im Ausdruckskontext) – deshalb lehnt der Handler das ab.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_meldet_Konflikt_bei_doppeltem_Collection_Schluessel()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var skill = await CreateQuestionAsync(gateway, dialogId, "skill", 0);
            var more = await CreateQuestionAsync(gateway, dialogId, "more", 1);

            await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", skill.Id, more.Id), token));

            var zweiter = await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", more.Id, skill.Id), token));

            Assert.False(zweiter.Success);
            Assert.Contains("skills", zweiter.Error);
        });
    }

    /// <summary>
    /// <c>LoopDefinition</c> referenziert Fragen FK-los. Bliebe ein Marker auf einer gelöschten Frage
    /// stehen, rechnete der <c>LoopResolver</c> zur Laufzeit gegen einen Bereich, den es im Graphen nicht
    /// mehr gibt – deshalb räumt <c>DeleteQuestionCommand</c> ihn wie die Übergänge mit ab.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_entfernt_Loop_Marker_beim_Loeschen_der_Frage()
    {
        await RunWithTempDbAsync(async (gateway, active, profile) =>
        {
            active.Activate(profile);
            var dialogId = await CreateDialogAsync(gateway, "onboarding");
            var skill = await CreateQuestionAsync(gateway, dialogId, "skill", 0);
            var more = await CreateQuestionAsync(gateway, dialogId, "more", 1);

            await gateway.ExecuteAsync((sender, token) =>
                sender.Send(new CreateLoopCommand(dialogId, "skills", skill.Id, more.Id), token));

            var geloescht = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new DeleteQuestionCommand(dialogId, more.Id), token));

            Assert.True(geloescht.Success, geloescht.Error);

            var detail = await gateway.ExecuteAsync(
                (sender, token) => sender.Send(new GetDialogQuery(dialogId), token));

            Assert.True(detail.Success, detail.Error);
            Assert.Empty(detail.Value!.Loops);
        });
    }

    /// <summary>Legt über das Gateway einen Dialog an und liefert dessen Id.</summary>
    /// <param name="gateway">Das zu verwendende Gateway.</param>
    /// <param name="key">Der Schlüssel des Dialogs.</param>
    /// <returns>Die Id des angelegten Dialogs.</returns>
    private static async Task<Guid> CreateDialogAsync(FlirtyAdminGateway gateway, string key)
    {
        var created = await gateway.ExecuteAsync(
            (sender, token) => sender.Send(new CreateDialogCommand(key, key, null), token));

        Assert.True(created.Success, created.Error);
        return created.Value!.Id;
    }

    /// <summary>Legt über das Gateway eine Freitext-Frage an.</summary>
    /// <param name="gateway">Das zu verwendende Gateway.</param>
    /// <param name="dialogId">Die Id des Dialogs.</param>
    /// <param name="key">Der Schlüssel der Frage.</param>
    /// <param name="order">Der Reihenfolge-Index.</param>
    /// <returns>Die angelegte Frage.</returns>
    private static async Task<QuestionDetail> CreateQuestionAsync(
        FlirtyAdminGateway gateway, Guid dialogId, string key, int order)
    {
        var created = await gateway.ExecuteAsync((sender, token) => sender.Send(
            new CreateQuestionCommand(dialogId, key, $"Frage {key}?", QuestionType.FreeText, order, false, null),
            token));

        Assert.True(created.Success, created.Error);
        return created.Value!;
    }

    /// <summary>Legt über das Gateway einen Übergang an.</summary>
    /// <param name="gateway">Das zu verwendende Gateway.</param>
    /// <param name="dialogId">Die Id des Dialogs.</param>
    /// <param name="fromQuestionId">Die Ausgangsfrage.</param>
    /// <param name="targetQuestionId">Die Zielfrage.</param>
    /// <param name="expression">Der optionale Bedingungsausdruck.</param>
    /// <param name="priority">Die Priorität.</param>
    /// <param name="isDefault">Ob es der Default-Übergang ist.</param>
    /// <returns>Der angelegte Übergang.</returns>
    private static async Task<TransitionDetail> CreateTransitionAsync(
        FlirtyAdminGateway gateway, Guid dialogId, Guid fromQuestionId, Guid targetQuestionId,
        string? expression, int priority, bool isDefault)
    {
        var created = await gateway.ExecuteAsync((sender, token) => sender.Send(
            new CreateTransitionCommand(dialogId, fromQuestionId, targetQuestionId, expression, priority, isDefault),
            token));

        Assert.True(created.Success, created.Error);
        return created.Value!;
    }

    /// <summary>
    /// Adapter auf <see cref="DesignerTestHost.RunWithTempDbAsync"/>: löst die beiden hier durchgängig
    /// gebrauchten Dienste aus dem Circuit-Scope auf. Der DI-Stack und die Temp-Datenbank liegen im
    /// gemeinsamen <see cref="DesignerTestHost"/>, damit sie nicht je Testklasse nachgezogen werden müssen.
    /// </summary>
    /// <param name="test">Der Testkörper (Gateway, aktives Profil des Scopes, migriertes Profil).</param>
    private static Task RunWithTempDbAsync(
        Func<FlirtyAdminGateway, ActiveConnectionProfile, ConnectionProfile, Task> test)
        => DesignerTestHost.RunWithTempDbAsync((services, profile) => test(
            services.GetRequiredService<FlirtyAdminGateway>(),
            services.GetRequiredService<ActiveConnectionProfile>(),
            profile));
}
