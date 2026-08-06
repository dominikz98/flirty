using Flirty.Domain;
using Flirty.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flirty.Tests.Validation;

/// <summary>
/// Verifies the <see cref="CustomQuestionTypeAnswerValidator"/> (issue #136): the dispatch to a
/// host-declared <see cref="IQuestionTypeValidator"/>, the order (built-in check first), and the
/// degradation when a question names a type this host did not declare.
/// </summary>
public sealed class CustomQuestionTypeAnswerValidatorTests
{
    private const string Key = "color";

    /// <summary>Hand-written test double: records whether it was called (no mocking framework).</summary>
    private sealed class SpyQuestionTypeValidator : IQuestionTypeValidator
    {
        public int Calls { get; private set; }

        public AnswerValidationResult Result { get; init; } = AnswerValidationResult.Valid;

        public AnswerValidationResult Validate(Question question, string value)
        {
            Calls++;
            return Result;
        }
    }

    private static Question NewQuestion(QuestionType type = QuestionType.Json, string? customTypeKey = Key)
        => new()
        {
            Id = Guid.NewGuid(),
            DialogId = Guid.NewGuid(),
            Key = "q",
            Text = "Question?",
            Type = type,
            CustomTypeKey = customTypeKey,
        };

    private static (CustomQuestionTypeAnswerValidator Validator, RecordingLoggerProvider Logs) Build(
        FlirtyQuestionTypeRegistry registry, IQuestionTypeValidator? hostValidator = null)
    {
        var logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        if (hostValidator is not null)
        {
            services.AddSingleton(hostValidator.GetType(), hostValidator);
        }

        var provider = services.BuildServiceProvider();

        return (
            new CustomQuestionTypeAnswerValidator(
                new AnswerValidator(),
                registry,
                provider,
                provider.GetRequiredService<ILogger<CustomQuestionTypeAnswerValidator>>()),
            logs);
    }

    private static FlirtyQuestionTypeRegistry Registry(Type? validatorType, string key = Key)
        => new(new Dictionary<string, FlirtyQuestionType>(StringComparer.Ordinal)
        {
            [key] = new(key, "Colour picker", validatorType, "\"#ff0000\""),
        });

    [Fact]
    public void A_declared_type_is_dispatched_to_its_validator()
    {
        var spy = new SpyQuestionTypeValidator { Result = AnswerValidationResult.Invalid("nope") };
        var (validator, _) = Build(Registry(typeof(SpyQuestionTypeValidator)), spy);

        var result = validator.Validate(NewQuestion(), "\"#ff0000\"");

        Assert.Equal(1, spy.Calls);
        Assert.False(result.IsValid);
        Assert.Equal("nope", Assert.Single(result.Errors));
    }

    /// <summary>
    /// Structure before semantics: a custom validator must never be handed a value the built-in Json
    /// check already refused, otherwise every one of them would have to defend against malformed input.
    /// </summary>
    [Fact]
    public void A_malformed_value_never_reaches_the_custom_validator()
    {
        var spy = new SpyQuestionTypeValidator();
        var (validator, _) = Build(Registry(typeof(SpyQuestionTypeValidator)), spy);

        var result = validator.Validate(NewQuestion(), "#ff0000");

        Assert.Equal(0, spy.Calls);
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// A published dialog cannot be edited (ADR 0005), so a question naming a type this host does not
    /// know must degrade to the plain JSON check instead of throwing – with exactly one warning that
    /// names the key, so the gap is findable in a log.
    /// </summary>
    [Fact]
    public void An_undeclared_key_degrades_to_the_json_check_and_logs_one_warning()
    {
        var (validator, logs) = Build(FlirtyQuestionTypeRegistry.Empty);

        var result = validator.Validate(NewQuestion(), "\"#ff0000\"");

        Assert.True(result.IsValid);
        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(Key, warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A type declared without a validator is a legitimate declaration – it names a shape for clients
    /// and leaves the checking at well-formed JSON. That is not a gap, so it must not warn.
    /// </summary>
    [Fact]
    public void A_type_declared_without_a_validator_passes_through_without_a_warning()
    {
        var (validator, logs) = Build(Registry(validatorType: null));

        Assert.True(validator.Validate(NewQuestion(), "{}").IsValid);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public void A_question_without_a_custom_type_key_is_not_dispatched()
    {
        var spy = new SpyQuestionTypeValidator { Result = AnswerValidationResult.Invalid("nope") };
        var (validator, _) = Build(Registry(typeof(SpyQuestionTypeValidator)), spy);

        Assert.True(validator.Validate(NewQuestion(customTypeKey: null), "{}").IsValid);
        Assert.Equal(0, spy.Calls);
    }

    /// <summary>
    /// The key is stored ordinally, so a differently cased one simply does not resolve. That is the
    /// documented safe outcome, not a silent mismatch – see <see cref="FlirtyQuestionTypeRegistry"/>.
    /// </summary>
    [Fact]
    public void The_key_lookup_is_case_sensitive()
    {
        var spy = new SpyQuestionTypeValidator { Result = AnswerValidationResult.Invalid("nope") };
        var (validator, logs) = Build(Registry(typeof(SpyQuestionTypeValidator)), spy);

        Assert.True(validator.Validate(NewQuestion(customTypeKey: "Color"), "{}").IsValid);
        Assert.Equal(0, spy.Calls);
        Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>
    /// The command guard refuses this combination on create and update, but the decorator must not
    /// depend on that: a row written before the guard existed still has to behave.
    /// </summary>
    [Fact]
    public void A_non_json_question_is_not_dispatched_even_with_a_key()
    {
        var spy = new SpyQuestionTypeValidator { Result = AnswerValidationResult.Invalid("nope") };
        var (validator, _) = Build(Registry(typeof(SpyQuestionTypeValidator)), spy);

        Assert.True(validator.Validate(NewQuestion(QuestionType.FreeText), "\"x\"").IsValid);
        Assert.Equal(0, spy.Calls);
    }
}
