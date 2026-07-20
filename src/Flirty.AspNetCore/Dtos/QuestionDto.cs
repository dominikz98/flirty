using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Schlanke, serialisierbare Sicht auf eine Frage für die WebAPI-Antworten. Spiegelt
/// <see cref="Flirty.Runtime.QuestionView"/> als HTTP-Contract des Pakets <c>Flirty.AspNetCore</c>.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Frage.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Frage.</param>
/// <param name="Text">Der anzuzeigende Fragetext.</param>
/// <param name="Type">Der Antworttyp der Frage.</param>
/// <param name="Options">
/// Die Antwortoptionen der Frage in Anzeigereihenfolge (leer bei Frei-Text-/Wert-Typen).
/// </param>
public sealed record QuestionDto(
    Guid Id,
    string Key,
    string Text,
    QuestionType Type,
    IReadOnlyList<AnswerOptionDto> Options);

/// <summary>
/// Schlanke, serialisierbare Sicht auf eine Antwortoption für die WebAPI-Antworten. Spiegelt
/// <see cref="Flirty.Runtime.AnswerOptionView"/>.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Antwortoption.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option.</param>
/// <param name="Label">Die anzuzeigende Beschriftung der Option.</param>
/// <param name="Value">Der zu speichernde Wert der Option.</param>
public sealed record AnswerOptionDto(Guid Id, string Key, string Label, string Value);
