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
        var store = new InMemoryConnectionProfileStore();
        using var provider = BuildProvider(store);
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

    /// <summary>
    /// Baut denselben DI-Stack wie <c>src/Flirty.Designer/Program.cs</c>: Engine ohne fest verdrahteten
    /// Provider, Kontext-Factory gegen das aktive Profil und das Gateway darüber.
    /// </summary>
    /// <param name="store">Der (In-Memory-)Profil-Store.</param>
    /// <returns>Der fertige Container.</returns>
    private static ServiceProvider BuildProvider(IConnectionProfileStore store)
        => new ServiceCollection()
            .AddLogging()
            .AddFlirty()
            .AddSingleton(store)
            .AddScoped<ActiveConnectionProfile>()
            .AddScoped<IDbContextFactory<FlirtyDbContext>, FlirtyDesignerDbContextFactory>()
            .AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FlirtyDbContext>>().CreateDbContext())
            .AddScoped<FlirtyAdminGateway>()
            .BuildServiceProvider();

    /// <summary>
    /// Legt eine migrierte SQLite-Temp-Datenbank samt Container/Scope an, führt den Test aus und räumt
    /// die Dateien wieder weg (Muster aus <see cref="ConnectionProfileOperationsTests"/>).
    /// </summary>
    /// <param name="test">Der Testkörper (Gateway, aktives Profil des Scopes, migriertes Profil).</param>
    private static async Task RunWithTempDbAsync(
        Func<FlirtyAdminGateway, ActiveConnectionProfile, ConnectionProfile, Task> test)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"flirty-designer-{Guid.NewGuid():N}.db");
        var profile = new ConnectionProfile
        {
            Name = "Temp",
            Provider = FlirtyDatabaseProvider.Sqlite,
            // Pooling=False: sonst hält der SQLite-Connection-Pool die Datei offen und der Cleanup scheitert.
            ConnectionString = $"Data Source={dbPath};Pooling=False",
        };

        await new ConnectionProfileOperations().ApplyMigrationsAsync(profile);

        var store = new InMemoryConnectionProfileStore();
        store.Save(profile);

        try
        {
            using var provider = BuildProvider(store);
            using var scope = provider.CreateScope();

            await test(
                scope.ServiceProvider.GetRequiredService<FlirtyAdminGateway>(),
                scope.ServiceProvider.GetRequiredService<ActiveConnectionProfile>(),
                profile);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var file = dbPath + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }

    /// <summary>
    /// Handgeschriebenes TestDouble des <see cref="IConnectionProfileStore"/> (kein Mocking-Framework,
    /// Projektkonvention): hält die Profile im Speicher statt in einer JSON-Datei.
    /// </summary>
    private sealed class InMemoryConnectionProfileStore : IConnectionProfileStore
    {
        private readonly List<ConnectionProfile> _profiles = [];

        public IReadOnlyList<ConnectionProfile> GetAll() => [.. _profiles];

        public ConnectionProfile? Get(string id) => _profiles.FirstOrDefault(profile => profile.Id == id);

        public void Save(ConnectionProfile profile)
        {
            _profiles.RemoveAll(existing => existing.Id == profile.Id);
            _profiles.Add(profile);
        }

        public void Delete(string id) => _profiles.RemoveAll(profile => profile.Id == id);

        public string? DefaultProfileId { get; private set; }

        public void SetDefault(string? id) => DefaultProfileId = id;
    }
}
