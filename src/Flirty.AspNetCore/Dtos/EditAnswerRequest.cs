namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Anfrage-Körper für <c>PUT /flirty/sessions/{id}/answers/{questionId}</c>: überschreibt die bereits
/// gegebene Antwort auf eine frühere Frage. Session-Id und Frage-Id stammen aus der Route; dieser Körper
/// trägt den neuen Wert und optional die Schleifen-Iteration. Wird auf das
/// <see cref="Flirty.Runtime.EditAnswerCommand"/> gemappt.
/// </summary>
/// <param name="Value">
/// Der neue Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp, z. B. der Wert einer
/// Auswahloption).
/// </param>
/// <param name="IterationIndex">
/// Optionaler nullbasierter Iterationsindex, um innerhalb einer Schleife gezielt die Antwort einer
/// bestimmten Iteration zu editieren; <see langword="null"/> editiert die früheste Antwort der Frage.
/// </param>
public sealed record EditAnswerRequest(string Value, int? IterationIndex = null);
