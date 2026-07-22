using Flirty.Domain;

namespace Flirty.Tests.Domain;

/// <summary>
/// Prüft den öffentlichen Schema-Typ <see cref="TriggerConfig"/> (#42): das Lesen des gespeicherten
/// JSON, das Schreiben im camelCase-Format und die kanal-abhängige Prüfung der Pflichtfelder. Der Typ
/// ist die eine Quelle des <see cref="TriggerDefinition.Config"/>-Schemas – Admin-Commands,
/// Webhook-Auslieferung und Designer hängen daran.
/// </summary>
public sealed class TriggerConfigTests
{
    /// <summary>Bekannte Felder werden case-insensitiv gelesen.</summary>
    [Theory]
    [InlineData("{\"url\":\"https://host.example/hook\",\"name\":\"order-created\"}")]
    [InlineData("{\"Url\":\"https://host.example/hook\",\"Name\":\"order-created\"}")]
    public void TryParse_liest_die_bekannten_Felder(string json)
    {
        Assert.True(TriggerConfig.TryParse(json, out var config, out var error));
        Assert.Null(error);
        Assert.Equal("https://host.example/hook", config.Url);
        Assert.Equal("order-created", config.Name);
    }

    /// <summary>Ein leerer Text gilt als leere Konfiguration (kein Fehler).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_behandelt_leere_Eingabe_als_leere_Konfiguration(string? json)
    {
        Assert.True(TriggerConfig.TryParse(json, out var config, out _));
        Assert.Null(config.Url);
        Assert.Null(config.Name);
    }

    /// <summary>Kaputtes JSON und Nicht-Objekte werden mit deutscher Meldung abgelehnt.</summary>
    [Theory]
    [InlineData("kein json")]
    [InlineData("[1, 2]")]
    [InlineData("\"nur ein Text\"")]
    public void TryParse_lehnt_ungueltiges_JSON_ab(string json)
    {
        Assert.False(TriggerConfig.TryParse(json, out var config, out var error));
        Assert.Null(config);
        Assert.NotNull(error);
    }

    /// <summary>Unbekannte Felder brechen das Lesen nicht – sie werden beim Schreiben aber verworfen.</summary>
    [Fact]
    public void TryParse_ignoriert_unbekannte_Felder()
    {
        Assert.True(
            TriggerConfig.TryParse("{\"url\":\"https://host.example/hook\",\"retries\":3}", out var config, out _));

        Assert.Equal("https://host.example/hook", config.Url);
        Assert.DoesNotContain("retries", config.ToJson(), StringComparison.Ordinal);
    }

    /// <summary>Geschrieben wird camelCase; nicht gesetzte Felder fehlen im JSON.</summary>
    [Fact]
    public void ToJson_schreibt_camelCase_ohne_leere_Felder()
    {
        var json = new TriggerConfig { Url = "https://host.example/hook" }.ToJson();

        Assert.Equal("{\"url\":\"https://host.example/hook\"}", json);
    }

    /// <summary>Ein Round-Trip erhält die bekannten Felder.</summary>
    [Fact]
    public void ToJson_und_TryParse_sind_verlustfrei()
    {
        var original = new TriggerConfig { Name = "order-created", Url = "https://host.example/hook" };

        Assert.True(TriggerConfig.TryParse(original.ToJson(), out var roundTrip, out _));
        Assert.Equal(original, roundTrip);
    }

    /// <summary>Ein Webhook braucht eine absolute http-/https-URL.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nicht-absolut")]
    [InlineData("/relativ/hook")]
    [InlineData("ftp://host.example/hook")]
    public void TryValidate_lehnt_untaugliche_Webhook_URLs_ab(string? url)
    {
        var config = new TriggerConfig { Url = url };

        Assert.False(config.TryValidate(TriggerKind.Webhook, out var error));
        Assert.NotNull(error);
    }

    /// <summary>Eine absolute http-/https-URL wird akzeptiert.</summary>
    [Theory]
    [InlineData("https://host.example/hook")]
    [InlineData("http://localhost:5000/flirty/hook")]
    public void TryValidate_akzeptiert_absolute_Webhook_URLs(string url)
    {
        var config = new TriggerConfig { Url = url };

        Assert.True(config.TryValidate(TriggerKind.Webhook, out var error));
        Assert.Null(error);
    }

    /// <summary>In-Process-Trigger brauchen keine URL – sie stellen nichts zu.</summary>
    [Fact]
    public void TryValidate_verlangt_bei_InProcess_keine_URL()
    {
        var config = new TriggerConfig { Name = "abschluss" };

        Assert.True(config.TryValidate(TriggerKind.InProcess, out var error));
        Assert.Null(error);
    }
}
