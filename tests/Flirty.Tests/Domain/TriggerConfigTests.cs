using Flirty.Domain;

namespace Flirty.Tests.Domain;

/// <summary>
/// Checks the public schema type <see cref="TriggerConfig"/> (#42): reading the stored JSON, writing
/// it in camelCase and the channel-dependent check of the required fields. The type is the single
/// source of the <see cref="TriggerDefinition.Config"/> schema – admin commands, webhook delivery and
/// the designer all hang on it.
/// </summary>
public sealed class TriggerConfigTests
{
    /// <summary>Known fields are read case-insensitively.</summary>
    [Theory]
    [InlineData("{\"url\":\"https://host.example/hook\",\"name\":\"order-created\"}")]
    [InlineData("{\"Url\":\"https://host.example/hook\",\"Name\":\"order-created\"}")]
    public void TryParse_reads_the_known_fields(string json)
    {
        Assert.True(TriggerConfig.TryParse(json, out var config, out var error));
        Assert.Null(error);
        Assert.Equal("https://host.example/hook", config.Url);
        Assert.Equal("order-created", config.Name);
    }

    /// <summary>An empty text counts as an empty configuration (not an error).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_treats_empty_input_as_an_empty_configuration(string? json)
    {
        Assert.True(TriggerConfig.TryParse(json, out var config, out _));
        Assert.Null(config.Url);
        Assert.Null(config.Name);
    }

    /// <summary>Broken JSON and non-objects are rejected with a message.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"just a text\"")]
    public void TryParse_rejects_invalid_JSON(string json)
    {
        Assert.False(TriggerConfig.TryParse(json, out var config, out var error));
        Assert.Null(config);
        Assert.NotNull(error);
    }

    /// <summary>Unknown fields do not break reading – but they are dropped when writing.</summary>
    [Fact]
    public void TryParse_ignores_unknown_fields()
    {
        Assert.True(
            TriggerConfig.TryParse("{\"url\":\"https://host.example/hook\",\"retries\":3}", out var config, out _));

        Assert.Equal("https://host.example/hook", config.Url);
        Assert.DoesNotContain("retries", config.ToJson(), StringComparison.Ordinal);
    }

    /// <summary>Writing produces camelCase; unset fields are absent from the JSON.</summary>
    [Fact]
    public void ToJson_writes_camelCase_without_empty_fields()
    {
        var json = new TriggerConfig { Url = "https://host.example/hook" }.ToJson();

        Assert.Equal("{\"url\":\"https://host.example/hook\"}", json);
    }

    /// <summary>A round trip preserves the known fields.</summary>
    [Fact]
    public void ToJson_and_TryParse_are_lossless()
    {
        var original = new TriggerConfig { Name = "order-created", Url = "https://host.example/hook" };

        Assert.True(TriggerConfig.TryParse(original.ToJson(), out var roundTrip, out _));
        Assert.Equal(original, roundTrip);
    }

    /// <summary>A webhook needs an absolute http/https URL.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-absolute")]
    [InlineData("/relative/hook")]
    [InlineData("ftp://host.example/hook")]
    public void TryValidate_rejects_unusable_webhook_URLs(string? url)
    {
        var config = new TriggerConfig { Url = url };

        Assert.False(config.TryValidate(TriggerKind.Webhook, out var error));
        Assert.NotNull(error);
    }

    /// <summary>An absolute http/https URL is accepted.</summary>
    [Theory]
    [InlineData("https://host.example/hook")]
    [InlineData("http://localhost:5000/flirty/hook")]
    public void TryValidate_accepts_absolute_webhook_URLs(string url)
    {
        var config = new TriggerConfig { Url = url };

        Assert.True(config.TryValidate(TriggerKind.Webhook, out var error));
        Assert.Null(error);
    }

    /// <summary>In-process triggers need no URL – they deliver nothing.</summary>
    [Fact]
    public void TryValidate_requires_no_URL_for_InProcess()
    {
        var config = new TriggerConfig { Name = "completion" };

        Assert.True(config.TryValidate(TriggerKind.InProcess, out var error));
        Assert.Null(error);
    }
}
