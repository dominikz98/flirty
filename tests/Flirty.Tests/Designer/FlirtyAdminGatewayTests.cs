using Flirty.Designer.Models;
using Flirty.Designer.Services;
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
