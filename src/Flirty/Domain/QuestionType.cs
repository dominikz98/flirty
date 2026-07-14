namespace Flirty.Domain;

/// <summary>
/// Legt fest, welche Art von Antwort eine <see cref="Question"/> erwartet und wie diese
/// geparst und validiert wird.
/// </summary>
public enum QuestionType
{
    /// <summary>Genau eine Antwortoption aus einer vorgegebenen Liste (<see cref="AnswerOption"/>).</summary>
    SingleChoice = 0,

    /// <summary>Beliebig viele Antwortoptionen aus einer vorgegebenen Liste (<see cref="AnswerOption"/>).</summary>
    MultiChoice = 1,

    /// <summary>Freier Text ohne vorgegebene Optionen.</summary>
    FreeText = 2,

    /// <summary>Eine numerische Eingabe.</summary>
    Number = 3,

    /// <summary>Eine Datums-(/Zeit-)Eingabe.</summary>
    Date = 4,

    /// <summary>Eine Ja/Nein-Eingabe (wahr/falsch).</summary>
    Boolean = 5,
}
