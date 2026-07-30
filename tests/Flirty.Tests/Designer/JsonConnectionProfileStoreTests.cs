using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="JsonConnectionProfileStore"/> (#37): CRUD, copy semantics and persistence
/// (incl. the default profile) over a real JSON file in the temp directory.
/// </summary>
public sealed class JsonConnectionProfileStoreTests
{
    [Fact]
    public void Save_and_GetAll_create_a_profile()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            store.Save(SqliteProfile("Local"));

            var all = store.GetAll();
            var profile = Assert.Single(all);
            Assert.Equal("Local", profile.Name);
            Assert.Equal(FlirtyDatabaseProvider.Sqlite, profile.Provider);
        });
    }

    [Fact]
    public void Save_updates_an_existing_profile_by_id()
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
    public void Delete_removes_the_profile_and_clears_the_default()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Local");
            store.Save(profile);
            store.SetDefault(profile.Id);

            store.Delete(profile.Id);

            Assert.Empty(store.GetAll());
            Assert.Null(store.DefaultProfileId);
        });
    }

    [Fact]
    public void Default_and_profiles_are_persisted_across_a_reload()
    {
        RunWithTempFile(path =>
        {
            var profile = SqliteProfile("Local");

            var first = new JsonConnectionProfileStore(path);
            first.Save(profile);
            first.SetDefault(profile.Id);

            // A new instance on the same file -> proves the persistence.
            var second = new JsonConnectionProfileStore(path);
            Assert.Equal(profile.Id, second.DefaultProfileId);
            var reloaded = Assert.Single(second.GetAll());
            Assert.Equal("Local", reloaded.Name);
            Assert.Equal(FlirtyDatabaseProvider.Sqlite, reloaded.Provider);
        });
    }

    [Fact]
    public void GetAll_returns_copies_that_do_not_change_the_store()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            store.Save(SqliteProfile("Local"));

            var fetched = Assert.Single(store.GetAll());
            fetched.Name = "Manipuliert";

            Assert.Equal("Local", Assert.Single(store.GetAll()).Name);
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
