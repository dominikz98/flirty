using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime;

namespace Flirty.Designer.Models;

/// <summary>
/// Eingabezustand einer Antwort im Test-Runner (#43) – für die aktuell offene Frage ebenso wie für das
/// Editieren einer bereits gegebenen Antwort. Bewusst veränderbar, damit die Blazor-Eingabefelder direkt
/// daran binden können.
/// </summary>
/// <remarks>
/// <para>
/// Das Modell hält den Wert in der Form, in der er <b>eingegeben</b> wird (Text, Wahrheitswert als
/// <c>"true"</c>/<c>"false"</c>, Options-Werte einer Mehrfachauswahl). Die Übersetzung in den rohen
/// JSON-Antwortwert der Engine macht ausschließlich der <c>AnswerValueCodec</c> – hier wird bewusst
/// nichts davon nachgebaut.
/// </para>
/// <para>
/// Abweichend von den übrigen Designer-Modellen <c>public</c>, aus demselben Grund wie
/// <see cref="AnswerChoice"/>: Der Typ ist <c>[Parameter]</c> der Komponente <c>AnswerInput</c>.
/// </para>
/// </remarks>
public sealed class AnswerInputModel
{
    /// <summary>Erstellt den Eingabezustand für die angegebene Frage.</summary>
    /// <param name="type">Der Antworttyp der Frage.</param>
    private AnswerInputModel(QuestionType type) => Type = type;

    /// <summary>Der Antworttyp der zu beantwortenden Frage.</summary>
    public QuestionType Type { get; }

    /// <summary>
    /// Der eingegebene Einzelwert: Freitext, Datum (ISO), Zahl, gewählter Options-Wert oder
    /// <c>"true"</c>/<c>"false"</c>. Bei <see cref="QuestionType.MultiChoice"/> ungenutzt.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Die gewählten Options-Werte einer <see cref="QuestionType.MultiChoice"/>-Frage.</summary>
    public HashSet<string> Selected { get; } = new(StringComparer.Ordinal);

    /// <summary>Erstellt einen leeren Eingabezustand für eine neu zu beantwortende Frage.</summary>
    /// <param name="question">Die aktuell offene Frage.</param>
    /// <returns>Der leere Eingabezustand.</returns>
    public static AnswerInputModel For(QuestionView question)
    {
        ArgumentNullException.ThrowIfNull(question);

        return new AnswerInputModel(question.Type);
    }

    /// <summary>
    /// Erstellt den Eingabezustand aus einer bereits gegebenen Antwort – Ausgangspunkt für das Editieren.
    /// </summary>
    /// <param name="type">Der Antworttyp der Frage.</param>
    /// <param name="value">Der gespeicherte rohe JSON-Antwortwert.</param>
    /// <returns>Der befüllte Eingabezustand.</returns>
    public static AnswerInputModel From(QuestionType type, string value)
    {
        var (text, selected) = AnswerValueCodec.Decode(type, value);
        var model = new AnswerInputModel(type) { Text = text };

        foreach (var entry in selected)
        {
            _ = model.Selected.Add(entry);
        }

        return model;
    }

    /// <summary>Setzt oder entfernt eine Option der Mehrfachauswahl.</summary>
    /// <param name="value">Der Options-Wert.</param>
    /// <param name="isSelected">Ob die Option gewählt sein soll.</param>
    public void Toggle(string value, bool isSelected)
    {
        _ = isSelected ? Selected.Add(value) : Selected.Remove(value);
    }

    /// <summary>
    /// Gibt an, ob die Eingabe abgeschickt werden kann. Verhindert nur das offensichtlich Leere; die
    /// fachliche Prüfung bleibt bewusst bei der Engine (<c>AnswerValidator</c>), damit der Runner genau
    /// die Meldungen zeigt, die eine Host-App auch bekäme.
    /// </summary>
    public bool CanSubmit
        => Type == QuestionType.MultiChoice ? Selected.Count > 0 : !string.IsNullOrWhiteSpace(Text);

    /// <summary>Kodiert die Eingabe als rohen JSON-Antwortwert für die Engine.</summary>
    /// <returns>Der rohe JSON-Text.</returns>
    public string Encode() => AnswerValueCodec.Encode(Type, Text, [.. Selected]);
}
