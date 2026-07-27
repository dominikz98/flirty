using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für <see cref="ActiveConnectionProfile"/>: das Übernehmen des Store-Standards, das Aktivieren,
/// das Durchreichen in einen Kind-Scope (<c>Adopt</c>) und – als Regression zu einem Befund aus dem
/// Abnahme-Durchlauf – das Freigeben beim Löschen des aktiven Profils (<c>Clear</c>). Ohne
/// <c>Clear</c> hielt der Circuit das gelöschte Profil weiter, und die Admin-Operationen liefen gegen
/// eine Verbindung, die in der Verwaltung nicht mehr existierte.
/// </summary>
public sealed class ActiveConnectionProfileTests
{
    [Fact]
    public void Current_ist_null_ohne_Standardprofil()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);

            Assert.Null(new ActiveConnectionProfile(store).Current);
        });
    }

    [Fact]
    public void Current_uebernimmt_das_Standardprofil_des_Stores()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Lokal");
            store.Save(profile);
            store.SetDefault(profile.Id);

            var active = new ActiveConnectionProfile(store);

            Assert.Equal(profile.Id, active.Current?.Id);
        });
    }

    [Fact]
    public void Activate_merkt_das_Profil_als_Store_Standard()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Lokal");
            store.Save(profile);

            new ActiveConnectionProfile(store).Activate(profile);

            Assert.Equal(profile.Id, store.DefaultProfileId);
        });
    }

    /// <summary>
    /// <c>Adopt</c> übernimmt das Profil nur in diesen Scope – der Store-Standard bleibt unberührt
    /// (mehrere Circuits können unterschiedliche Profile aktiv haben).
    /// </summary>
    [Fact]
    public void Adopt_aendert_den_Store_Standard_nicht()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Lokal");
            store.Save(profile);

            var active = new ActiveConnectionProfile(store);
            active.Adopt(profile);

            Assert.Equal(profile.Id, active.Current?.Id);
            Assert.Null(store.DefaultProfileId);
        });
    }

    /// <summary>
    /// Nach <c>Clear</c> ist kein Profil aktiv – auch dann nicht, wenn der Store noch einen Standard
    /// führt. Das ist die Regression: <c>Current</c> darf den (gelöschten) Standard nicht erneut lesen.
    /// </summary>
    [Fact]
    public void Clear_gibt_das_aktive_Profil_frei_und_liest_den_Standard_nicht_erneut()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Lokal");
            store.Save(profile);
            store.SetDefault(profile.Id);

            var active = new ActiveConnectionProfile(store);
            Assert.NotNull(active.Current);

            active.Clear();

            Assert.Null(active.Current);
        });
    }

    /// <summary>Der typische Ablauf beim Löschen des aktiven Profils: Store räumt auf, Scope gibt frei.</summary>
    [Fact]
    public void Loeschen_des_aktiven_Profils_hinterlaesst_keinen_aktiven_Zustand()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Lokal");
            store.Save(profile);

            var active = new ActiveConnectionProfile(store);
            active.Activate(profile);

            store.Delete(profile.Id);
            active.Clear();

            Assert.Null(active.Current);
            Assert.Null(store.DefaultProfileId);
            Assert.Empty(store.GetAll());
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
        var path = Path.Combine(Path.GetTempPath(), $"flirty-active-profile-{Guid.NewGuid():N}.json");
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
