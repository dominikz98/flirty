using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>
/// Deutsche Anzeigetexte für die <see cref="QuestionType"/>-Werte. Zentral, damit Fragenliste
/// (<c>DialogEditor</c>) und Frage-Editor (<c>QuestionEditor</c>) dieselben Bezeichnungen verwenden.
/// </summary>
internal static class QuestionTypeLabels
{
    /// <summary>Liefert den Anzeigetext des angegebenen Fragetyps.</summary>
    /// <param name="type">Der Fragetyp.</param>
    /// <returns>Der deutsche Anzeigetext (inklusive des technischen Namens zur Wiedererkennung).</returns>
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

    /// <summary>Gibt an, ob der Fragetyp Antwortoptionen auswertet (Choice-Typen).</summary>
    /// <param name="type">Der Fragetyp.</param>
    /// <returns><see langword="true"/> bei <see cref="QuestionType.SingleChoice"/> und <see cref="QuestionType.MultiChoice"/>.</returns>
    public static bool UsesOptions(QuestionType type)
        => type is QuestionType.SingleChoice or QuestionType.MultiChoice;
}
