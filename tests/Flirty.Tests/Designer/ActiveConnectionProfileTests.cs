using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for <see cref="ActiveConnectionProfile"/>: taking over the store default, activating,
/// passing it into a child scope (<c>Adopt</c>) and – as a regression for a finding from the
/// acceptance pass – releasing it when the active profile is deleted (<c>Clear</c>). Without
/// <c>Clear</c> the circuit kept holding the deleted profile, and the admin operations ran against a
/// connection that no longer existed in the management view.
/// </summary>
public sealed class ActiveConnectionProfileTests
{
    [Fact]
    public void Current_is_null_without_a_default_profile()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);

            Assert.Null(new ActiveConnectionProfile(store).Current);
        });
    }

    [Fact]
    public void Current_takes_over_the_stores_default_profile()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Local");
            store.Save(profile);
            store.SetDefault(profile.Id);

            var active = new ActiveConnectionProfile(store);

            Assert.Equal(profile.Id, active.Current?.Id);
        });
    }

    [Fact]
    public void Activate_remembers_the_profile_as_the_store_default()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Local");
            store.Save(profile);

            new ActiveConnectionProfile(store).Activate(profile);

            Assert.Equal(profile.Id, store.DefaultProfileId);
        });
    }

    /// <summary>
    /// <c>Adopt</c> takes the profile over only into this scope – the store default stays untouched
    /// (several circuits can have different profiles active).
    /// </summary>
    [Fact]
    public void Adopt_does_not_change_the_store_default()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Local");
            store.Save(profile);

            var active = new ActiveConnectionProfile(store);
            active.Adopt(profile);

            Assert.Equal(profile.Id, active.Current?.Id);
            Assert.Null(store.DefaultProfileId);
        });
    }

    /// <summary>
    /// After <c>Clear</c> no profile is active – not even when the store still carries a default.
    /// That is the regression: <c>Current</c> must not read the (deleted) default again.
    /// </summary>
    [Fact]
    public void Clear_releases_the_active_profile_and_does_not_read_the_default_again()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Local");
            store.Save(profile);
            store.SetDefault(profile.Id);

            var active = new ActiveConnectionProfile(store);
            Assert.NotNull(active.Current);

            active.Clear();

            Assert.Null(active.Current);
        });
    }

    /// <summary>The typical flow when the active profile is deleted: the store cleans up, the scope releases.</summary>
    [Fact]
    public void Deleting_the_active_profile_leaves_no_active_state()
    {
        RunWithTempFile(path =>
        {
            var store = new JsonConnectionProfileStore(path);
            var profile = SqliteProfile("Local");
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
