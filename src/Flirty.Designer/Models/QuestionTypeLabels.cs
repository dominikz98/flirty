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
    /// <returns>The English display text (including the technical name for recognition).</returns>
    public static string Describe(QuestionType type) => type switch
    {
        QuestionType.SingleChoice => "Single choice (SingleChoice)",
        QuestionType.MultiChoice => "Multiple choice (MultiChoice)",
        QuestionType.FreeText => "Free text (FreeText)",
        QuestionType.Number => "Number (Number)",
        QuestionType.Date => "Date (Date)",
        QuestionType.Boolean => "Yes/No (Boolean)",
        _ => type.ToString(),
    };

    /// <summary>Indicates whether the question type evaluates answer options (choice types).</summary>
    /// <param name="type">The question type.</param>
    /// <returns><see langword="true"/> for <see cref="QuestionType.SingleChoice"/> and <see cref="QuestionType.MultiChoice"/>.</returns>
    public static bool UsesOptions(QuestionType type)
        => type is QuestionType.SingleChoice or QuestionType.MultiChoice;
}
