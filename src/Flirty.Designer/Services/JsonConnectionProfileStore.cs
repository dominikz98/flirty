using System.Text.Json;
using System.Text.Json.Serialization;
using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// <see cref="IConnectionProfileStore"/>-Implementierung, die die Profile als Klartext-JSON in einer
/// lokalen Datei ablegt. Für ein lokales Entwickler-Werkzeug bewusst einfach gehalten; die Datei kann
/// Secrets (Passwörter in Verbindungszeichenfolgen) enthalten und ist daher per <c>.gitignore</c>
/// ausgeschlossen (siehe <c>docs/DESIGNER.md</c>).
/// </summary>
internal sealed class JsonConnectionProfileStore : IConnectionProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    private ProfileDocument _document;

    /// <summary>Erstellt den Store und lädt eine ggf. vorhandene Profil-Datei.</summary>
    /// <param name="filePath">Vollständiger Pfad zur JSON-Datei.</param>
    public JsonConnectionProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _document = Load(filePath);
    }

    /// <inheritdoc />
    public IReadOnlyList<ConnectionProfile> GetAll()
    {
        lock (_gate)
        {
            return _document.Profiles.Select(p => p.Clone()).ToList();
        }
    }

    /// <inheritdoc />
    public ConnectionProfile? Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            return _document.Profiles.FirstOrDefault(p => p.Id == id)?.Clone();
        }
    }

    /// <inheritdoc />
    public void Save(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            var copy = profile.Clone();
            var index = _document.Profiles.FindIndex(p => p.Id == copy.Id);
            if (index >= 0)
            {
                _document.Profiles[index] = copy;
            }
            else
            {
                _document.Profiles.Add(copy);
            }

            Persist();
        }
    }

    /// <inheritdoc />
    public void Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            _document.Profiles.RemoveAll(p => p.Id == id);
            if (_document.DefaultProfileId == id)
            {
                _document.DefaultProfileId = null;
            }

            Persist();
        }
    }

    /// <inheritdoc />
    public string? DefaultProfileId
    {
        get
        {
            lock (_gate)
            {
                return _document.DefaultProfileId;
            }
        }
    }

    /// <inheritdoc />
    public void SetDefault(string? id)
    {
        lock (_gate)
        {
            _document.DefaultProfileId = id;
            Persist();
        }
    }

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_document, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }

    private static ProfileDocument Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ProfileDocument();
        }

        var json = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProfileDocument();
        }

        return JsonSerializer.Deserialize<ProfileDocument>(json, SerializerOptions) ?? new ProfileDocument();
    }

    /// <summary>Serialisierungs-Container für die JSON-Datei.</summary>
    private sealed class ProfileDocument
    {
        public List<ConnectionProfile> Profiles { get; set; } = [];

        public string? DefaultProfileId { get; set; }
    }
}
