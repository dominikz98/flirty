using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für den <see cref="JsonConnectionProfileStore"/> (#37): CRUD, Kopier-Semantik und
/// Persistenz (inkl. Standardprofil) über eine echte JSON-Datei im Temp-Verzeichnis.
/// </summary>
public sealed class JsonConnectionProfileStoreTests
{
    [Fact]
    public void Save_und_GetAll_legt_Profil_an()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            store.Save(SqliteProfile("Lokal"));

            var all = store.GetAll();
            var profile = Assert.Single(all);
            Assert.Equal("Lokal", profile.Name);
            Assert.Equal(FlirtyDatabaseProvider.Sqlite, profile.Provider);
        });
    }

    [Fact]
    public void Save_aktualisiert_bestehendes_Profil_ueber_Id()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Alt");
            store.Save(profile);

            profile.Name = "Neu";
            store.Save(profile);

            var single = Assert.Single(store.GetAll());
            Assert.Equal("Neu", single.Name);
        });
    }

    [Fact]
    public void Delete_entfernt_Profil_und_loescht_Default()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Lokal");
            store.Save(profile);
            store.SetDefault(profile.Id);

            store.Delete(profile.Id);

            Assert.Empty(store.GetAll());
            Assert.Null(store.DefaultProfileId);
        });
    }

    [Fact]
    public void Default_und_Profile_werden_ueber_Neuladen_persistiert()
    {
        RunWithTempFile(path =>
        {
            var profile = SqliteProfile("Lokal");

            var first = new JsonConnectionProfileStore(path);
            first.Save(profile);
            first.SetDefault(profile.Id);

            // Neue Instanz auf derselben Datei -> beweist die Persistenz.
            var second = new JsonConnectionProfileStore(path);
            Assert.Equal(profile.Id, second.DefaultProfileId);
            var reloaded = Assert.Single(second.GetAll());
            Assert.Equal("Lokal", reloaded.Name);
            Assert.Equal(FlirtyDatabaseProvider.Sqlite, reloaded.Provider);
        });
    }

    [Fact]
    public void GetAll_liefert_Kopien_die_den_Store_nicht_veraendern()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            store.Save(SqliteProfile("Lokal"));

            var fetched = Assert.Single(store.GetAll());
            fetched.Name = "Manipuliert";

            Assert.Equal("Lokal", Assert.Single(store.GetAll()).Name);
        });
    }

    private static ConnectionProfile SqliteProfile(string name) => new()
    {
        Name = name,
        Provider = FlirtyDatabaseProvider.Sqlite,
        ConnectionString = "Data Source=flirty.designer.db",
    };

    private static void RunWithTempFile(Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"flirty-profiles-{Guid.NewGuid():N}.json");
        try
        {
            test(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
