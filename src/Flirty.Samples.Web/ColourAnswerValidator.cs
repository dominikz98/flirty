using System.Text.Json;
using System.Text.RegularExpressions;
using Flirty.Domain;
using Flirty.Validation;

namespace Flirty.Samples.Web;

/// <summary>
/// The worked example of a <b>scalar</b> host-declared question type: a colour, stored as a JSON string
/// in the form <c>#rrggbb</c>. Declared in <c>WebSampleApp</c> as <c>color</c> and selected by a question
/// of type <see cref="QuestionType.Json"/> carrying that key in
/// <see cref="Question.CustomTypeKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// It takes a constructor dependency purely to show that it can: a validator is resolved from the
/// <b>request scope</b>, so it may use scoped services – an <c>HttpClient</c>, options, or the same
/// <c>FlirtyDbContext</c> the handler uses.
/// </para>
/// <para>
/// Note what is <i>not</i> here: nothing about an input control. The engine stores an opaque JSON value
/// and the chat UI picks its control from the question's <c>customTypeKey</c> – the two sides share a
/// key, not a schema.
/// </para>
/// </remarks>
public sealed partial class ColourAnswerValidator : IQuestionTypeValidator
{
    private readonly ILogger<ColourAnswerValidator> _logger;

    /// <summary>Creates the validator.</summary>
    /// <param name="logger">Logger, present to demonstrate scoped resolution.</param>
    public ColourAnswerValidator(ILogger<ColourAnswerValidator> logger) => _logger = logger;

    /// <inheritdoc />
    public AnswerValidationResult Validate(Question question, string value)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(value);

        // The value is guaranteed to be well-formed JSON by the time it arrives - the built-in Json
        // check runs first. So this only has to care about the shape it expects.
        string? colour;
        try
        {
            using var document = JsonDocument.Parse(value);
            colour = document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            colour = null;
        }

        if (colour is null || !HexColour().IsMatch(colour))
        {
            _logger.LogInformation("Refused colour answer {Value} of question {Key}.", value, question.Key);
            return AnswerValidationResult.Invalid(
                $"'{colour ?? value}' is not a colour – expected a JSON string in the form \"#rrggbb\".");
        }

        return AnswerValidationResult.Valid;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}
