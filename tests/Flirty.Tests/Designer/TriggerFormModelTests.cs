using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="TriggerFormModel"/> (#42): the mapping between the trigger editor's input
/// fields and the <see cref="TriggerDefinition.Config"/> stored as JSON. The core is that
/// serialization goes against the core type <see cref="TriggerConfig"/> (no duplicate) and that
/// unknown fields are not silently lost.
/// </summary>
public sealed class TriggerFormModelTests
{
    [Fact]
    public void From_reads_the_known_configuration_into_the_individual_fields()
    {
        var model = TriggerFormModel.From(
            Trigger("""{"url":"https://host.example/hook","name":"order-created"}"""));

        Assert.False(model.UseRawJson);
        Assert.Equal("https://host.example/hook", model.Url);
        Assert.Equal("order-created", model.Name);
    }

    [Fact]
    public void From_falls_back_to_raw_JSON_on_unknown_fields()
    {
        const string config = """{"url":"https://host.example/hook","retries":3}""";

        var model = TriggerFormModel.From(Trigger(config));

        Assert.True(model.UseRawJson);
        Assert.Equal(config, model.RawJson);
    }

    [Fact]
    public void From_falls_back_to_raw_JSON_on_invalid_JSON()
    {
        var model = TriggerFormModel.From(Trigger("not json"));

        Assert.True(model.UseRawJson);
        Assert.Equal("not json", model.RawJson);
    }

    [Fact]
    public void TryBuildConfig_writes_the_JSON_of_the_core_type()
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
    public void TryBuildConfig_reports_a_missing_webhook_URL()
    {
        var model = new TriggerFormModel { Kind = TriggerKind.Webhook, Name = "ohne-url" };

        Assert.False(model.TryBuildConfig(out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryBuildConfig_preserves_foreign_fields_in_raw_mode()
    {
        const string config = """{"url":"https://host.example/hook","retries":3}""";
        var model = TriggerFormModel.From(Trigger(config));

        Assert.True(model.TryBuildConfig(out var json, out _));
        Assert.Equal(config, json);
    }

    [Fact]
    public void TryBuildConfig_checks_against_the_channel_in_raw_mode_too()
    {
        var model = TriggerFormModel.From(Trigger("""{"name":"ohne-url","retries":3}"""));

        Assert.True(model.UseRawJson);
        Assert.False(model.TryBuildConfig(out _, out var error));
        Assert.NotNull(error);
    }

    /// <summary>
    /// The question reference applies only to <see cref="TriggerScope.AfterQuestion"/> – otherwise
    /// the admin commands reject the request with a 400.
    /// </summary>
    [Fact]
    public void NormalizedQuestionId_discards_the_reference_outside_AfterQuestion()
    {
        var questionId = Guid.NewGuid();

        var bound = new TriggerFormModel { Scope = TriggerScope.AfterQuestion, QuestionId = questionId };
        var unbound = new TriggerFormModel { Scope = TriggerScope.AfterAnswer, QuestionId = questionId };

        Assert.Equal(questionId, bound.NormalizedQuestionId());
        Assert.Null(unbound.NormalizedQuestionId());
    }

    /// <summary>An empty expression lands in the column as <see langword="null"/>, not as "".</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  role == \"dev\"  ", "role == \"dev\"")]
    public void NormalizedExpression_normalizes_the_expression(string? input, string? expected)
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
