using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Ergebnis von <see cref="StartDialogCommand"/> bzw. <see cref="IFlirtyEngine.StartDialogAsync"/>:
/// die (neu angelegte oder wiederaufgenommene) Session samt der aktuell zu beantwortenden Frage.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der laufenden <see cref="DialogSession"/>.</param>
/// <param name="IsResumed">
/// <see langword="true"/>, wenn eine bereits laufende Session fortgesetzt wurde; <see langword="false"/>,
/// wenn der Dialog neu gestartet wurde.
/// </param>
/// <param name="CurrentQuestion">Die aktuell offene Frage, die dem Anwender zu präsentieren ist.</param>
public sealed record StartDialogResult(Guid SessionId, bool IsResumed, QuestionView CurrentQuestion);

/// <summary>
/// Schlanke, unveränderliche Sicht auf eine <see cref="Question"/> für die Laufzeit-API – ohne
/// EF-Core-Navigationen, damit Host-Apps die Frage darstellen können, ohne den Konfigurationsgraphen
/// zu kennen.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Frage.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Frage.</param>
/// <param name="Text">Der anzuzeigende Fragetext.</param>
/// <param name="Type">Der Antworttyp der Frage.</param>
/// <param name="Options">
/// Die Antwortoptionen der Frage in Anzeigereihenfolge (leer bei Frei-Text-/Wert-Typen).
/// </param>
public sealed record QuestionView(
    Guid Id,
    string Key,
    string Text,
    QuestionType Type,
    IReadOnlyList<AnswerOptionView> Options);

/// <summary>
/// Schlanke, unveränderliche Sicht auf eine <see cref="AnswerOption"/> für die Laufzeit-API.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Antwortoption.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option.</param>
/// <param name="Label">Die anzuzeigende Beschriftung der Option.</param>
/// <param name="Value">Der zu speichernde Wert der Option.</param>
public sealed record AnswerOptionView(Guid Id, string Key, string Label, string Value);
