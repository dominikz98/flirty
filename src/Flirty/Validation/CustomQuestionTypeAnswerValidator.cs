using Flirty.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flirty.Validation;

/// <summary>
/// Decorates the built-in <see cref="IAnswerValidator"/> and adds the semantics of a host-declared
/// custom question type: it resolves <see cref="Question.CustomTypeKey"/> against the
/// <see cref="FlirtyQuestionTypeRegistry"/> and hands the answer to the registered
/// <see cref="IQuestionTypeValidator"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam is created in DI, not in the class.</b> <see cref="AnswerValidator"/> is sealed with
/// only private helpers, so there is no chain to hook into inside it – and there should not be: it
/// stays the single owner of the seven built-in types. This decorator is registered only when at
/// least one custom type was declared, which is what keeps the registration a plain singleton for
/// every host that does not use the feature.
/// </para>
/// <para>
/// It takes the <see cref="IServiceProvider"/> rather than an <c>IServiceScopeFactory</c>: injected
/// into a <b>scoped</b> service that provider <i>is</i> the request scope, so a host validator shares
/// the <c>FlirtyDbContext</c> with the handler. A scope factory would open a second scope and give it
/// a different context.
/// </para>
/// </remarks>
internal sealed class CustomQuestionTypeAnswerValidator : IAnswerValidator
{
    private readonly IAnswerValidator _inner;
    private readonly FlirtyQuestionTypeRegistry _registry;
    private readonly IServiceProvider _scope;
    private readonly ILogger<CustomQuestionTypeAnswerValidator> _logger;

    /// <summary>Creates the decorator around the built-in validator.</summary>
    /// <param name="inner">The built-in validator that checks the seven <see cref="QuestionType"/> arms.</param>
    /// <param name="registry">The types the host declared.</param>
    /// <param name="scope">The request scope a host validator is resolved from.</param>
    /// <param name="logger">Logger for the degradation path (an undeclared key).</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public CustomQuestionTypeAnswerValidator(
        IAnswerValidator inner,
        FlirtyQuestionTypeRegistry registry,
        IServiceProvider scope,
        ILogger<CustomQuestionTypeAnswerValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _registry = registry;
        _scope = scope;
        _logger = logger;
    }

    /// <inheritdoc />
    public AnswerValidationResult Validate(Question question, string value)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(value);

        // Structure first, semantics second. A custom validator must never be handed a value the
        // built-in Json check already refused - otherwise every one of them would have to defend
        // against malformed input the engine has already rejected.
        var result = _inner.Validate(question, value);
        if (!result.IsValid
            || question.Type != QuestionType.Json
            || string.IsNullOrWhiteSpace(question.CustomTypeKey))
        {
            return result;
        }

        if (!_registry.TryGet(question.CustomTypeKey, out var declared))
        {
            // Degrade, never throw. A published dialog cannot be edited (ADR 0005), so throwing here
            // would be an error nobody can repair - and well-formed JSON is the most permissive
            // meaningful check, which is exactly what the type promises without its registration.
            _logger.LogWarning(
                "Question {QuestionId} declares the custom question type '{CustomTypeKey}', which this "
                + "host has not declared with AddQuestionType. The answer was validated as plain JSON.",
                question.Id,
                question.CustomTypeKey);
            return result;
        }

        // A type declared without a validator is a legitimate declaration, not a misconfiguration:
        // it names a shape for clients and leaves the checking at well-formed JSON. No warning.
        return declared!.ValidatorType is null
            ? result
            : ((IQuestionTypeValidator)_scope.GetRequiredService(declared.ValidatorType))
                .Validate(question, value);
    }
}
