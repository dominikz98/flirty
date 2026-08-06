using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>
/// English display texts for the <see cref="QuestionType"/> values. Centralized, so that the question list
/// (<c>DialogEditor</c>) and the question editor (<c>QuestionEditor</c>) use the same labels.
/// </summary>
internal static class QuestionTypeLabels
{
    /// <summary>Returns the display text of the given question type.</summary>
    /// <param name="type">The question type.</param>
    /// <param name="customTypeKey">
    /// The question's <see cref="Flirty.Domain.Question.CustomTypeKey"/>, if any. Passing it makes the
    /// host-declared type visible wherever a question is described – the designer does not know that
    /// type's registry, so its key is the only thing it can honestly show.
    /// </param>
    /// <returns>The English display text (including the technical name for recognition).</returns>
    public static string Describe(QuestionType type, string? customTypeKey = null) => type switch
    {
        QuestionType.SingleChoice => "Single choice (SingleChoice)",
        QuestionType.MultiChoice => "Multiple choice (MultiChoice)",
        QuestionType.FreeText => "Free text (FreeText)",
        QuestionType.Number => "Number (Number)",
        QuestionType.Date => "Date (Date)",
        QuestionType.Boolean => "Yes/No (Boolean)",
        QuestionType.Json when !string.IsNullOrWhiteSpace(customTypeKey)
            => $"Custom type \"{customTypeKey}\" (Json)",
        QuestionType.Json => "JSON or custom type (Json)",
        _ => type.ToString(),
    };

    /// <summary>Indicates whether the question type evaluates answer options (choice types).</summary>
    /// <param name="type">The question type.</param>
    /// <returns><see langword="true"/> for <see cref="QuestionType.SingleChoice"/> and <see cref="QuestionType.MultiChoice"/>.</returns>
    /// <remarks>
    /// Deliberately unchanged for <see cref="QuestionType.Json"/>: this is a positive predicate about the
    /// <b>engine</b>, and the engine does not evaluate options for a JSON question. It says nothing about
    /// a host's custom validator, which receives <c>question.Options</c> and may well read them – so a
    /// caller must not turn a <see langword="false"/> here into "these options are useless".
    /// </remarks>
    public static bool UsesOptions(QuestionType type)
        => type is QuestionType.SingleChoice or QuestionType.MultiChoice;

    /// <summary>
    /// Indicates whether the designer's test runner can offer an input control for this type.
    /// </summary>
    /// <param name="type">The question type.</param>
    /// <returns><see langword="false"/> for <see cref="QuestionType.Json"/>, otherwise <see langword="true"/>.</returns>
    /// <remarks>
    /// A documented limit rather than a gap. A test run writes a <b>real</b> session and delivers real
    /// webhooks, and the designer does not know what shape a host's custom type expects – so a guessed
    /// value would be worse than none. This is the single source of that decision; the control, the
    /// submit guard and the edit buttons all read it.
    /// </remarks>
    public static bool IsAnswerableInDesigner(QuestionType type) => type != QuestionType.Json;
}
