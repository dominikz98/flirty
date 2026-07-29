using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>
/// German display texts for the <see cref="QuestionType"/> values. Central, so that the question list
/// (<c>DialogEditor</c>) and question editor (<c>QuestionEditor</c>) use the same designations.
/// </summary>
internal static class QuestionTypeLabels
{
    /// <summary>Returns the display text of the given question type.</summary>
    /// <param name="type">The question type.</param>
    /// <returns>The German display text (including the technical name for recognition).</returns>
    public static string Describe(QuestionType type) => type switch
    {
        QuestionType.SingleChoice => "Einfachauswahl (SingleChoice)",
        QuestionType.MultiChoice => "Mehrfachauswahl (MultiChoice)",
        QuestionType.FreeText => "Freitext (FreeText)",
        QuestionType.Number => "Zahl (Number)",
        QuestionType.Date => "Datum (Date)",
        QuestionType.Boolean => "Ja/Nein (Boolean)",
        _ => type.ToString(),
    };

    /// <summary>Indicates whether the question type evaluates answer options (choice types).</summary>
    /// <param name="type">The question type.</param>
    /// <returns><see langword="true"/> for <see cref="QuestionType.SingleChoice"/> and <see cref="QuestionType.MultiChoice"/>.</returns>
    public static bool UsesOptions(QuestionType type)
        => type is QuestionType.SingleChoice or QuestionType.MultiChoice;
}
