using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für das <see cref="TriggerFormModel"/> (#42): die Abbildung zwischen den Eingabefeldern des
/// Trigger-Editors und der als JSON gespeicherten <see cref="TriggerDefinition.Config"/>. Kern ist,
/// dass gegen den Core-Typ <see cref="TriggerConfig"/> serialisiert wird (kein Duplikat) und dass
/// unbekannte Felder nicht stillschweigend verloren gehen.
/// </summary>
public sealed class TriggerFormModelTests
{
    [Fact]
    public void From_liest_die_bekannte_Konfiguration_in_die_Einzelfelder()
    {
        var model = TriggerFormModel.From(
            Trigger("""{"url":"https://host.example/hook","name":"order-created"}"""));

        Assert.False(model.UseRawJson);
        Assert.Equal("https://host.example/hook", model.Url);
        Assert.Equal("order-created", model.Name);
    }

    [Fact]
    public void From_faellt_bei_unbekannten_Feldern_auf_Roh_JSON_zurueck()
    {
        const string config = """{"url":"https://host.example/hook","retries":3}""";

        var model = TriggerFormModel.From(Trigger(config));

        Assert.True(model.UseRawJson);
        Assert.Equal(config, model.RawJson);
    }

    [Fact]
    public void From_faellt_bei_ungueltigem_JSON_auf_Roh_JSON_zurueck()
    {
        var model = TriggerFormModel.From(Trigger("kein json"));

        Assert.True(model.UseRawJson);
        Assert.Equal("kein json", model.RawJson);
    }

    [Fact]
    public void TryBuildConfig_schreibt_das_JSON_des_Core_Typs()
    {
        var model = new TriggerFormModel
        {
            Kind = TriggerKind.Webhook,
            Url = "  https://host.example/hook  ",
            Name = "order-created",
        };

        Assert.True(model.TryBuildConfig(out var json, out var error));
        Assert.Null(error);
        Assert.True(TriggerConfig.TryParse(json, out var parsed, out _));
        Assert.Equal("https://host.example/hook", parsed.Url);
        Assert.Equal("order-created", parsed.Name);
    }

    [Fact]
    public void TryBuildConfig_meldet_eine_fehlende_Webhook_URL()
    {
        var model = new TriggerFormModel { Kind = TriggerKind.Webhook, Name = "ohne-url" };

        Assert.False(model.TryBuildConfig(out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryBuildConfig_erhaelt_im_Roh_Modus_fremde_Felder()
    {
        const string config = """{"url":"https://host.example/hook","retries":3}""";
        var model = TriggerFormModel.From(Trigger(config));

        Assert.True(model.TryBuildConfig(out var json, out _));
        Assert.Equal(config, json);
    }

    [Fact]
    public void TryBuildConfig_prueft_auch_im_Roh_Modus_gegen_den_Kanal()
    {
        var model = TriggerFormModel.From(Trigger("""{"name":"ohne-url","retries":3}"""));

        Assert.True(model.UseRawJson);
        Assert.False(model.TryBuildConfig(out _, out var error));
        Assert.NotNull(error);
    }

    /// <summary>
    /// Der Frage-Bezug gilt nur für <see cref="TriggerScope.AfterQuestion"/> – sonst weisen die
    /// Admin-Commands die Anfrage mit 400 zurück.
    /// </summary>
    [Fact]
    public void NormalizedQuestionId_verwirft_den_Bezug_ausserhalb_von_AfterQuestion()
    {
        var questionId = Guid.NewGuid();

        var gebunden = new TriggerFormModel { Scope = TriggerScope.AfterQuestion, QuestionId = questionId };
        var ungebunden = new TriggerFormModel { Scope = TriggerScope.AfterAnswer, QuestionId = questionId };

        Assert.Equal(questionId, gebunden.NormalizedQuestionId());
        Assert.Null(ungebunden.NormalizedQuestionId());
    }

    /// <summary>Ein leerer Ausdruck landet als <see langword="null"/> in der Spalte, nicht als "".</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  role == \"dev\"  ", "role == \"dev\"")]
    public void NormalizedExpression_normalisiert_den_Ausdruck(string? input, string? expected)
    {
        var model = new TriggerFormModel { Expression = input };

        Assert.Equal(expected, model.NormalizedExpression());
    }

    private static TriggerDetail Trigger(string config)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TriggerScope.OnDialogCompleted,
            QuestionId: null,
            TriggerKind.Webhook,
            config,
            Expression: null);
}
