using Flirty.Runtime;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Eine auswählbare Antwortoption für die Eingabe im Test-Runner (#43) – reduziert auf Wert und
/// Beschriftung.
/// </summary>
/// <remarks>
/// <para>
/// Nötig, weil dieselbe Eingabe zwei Quellen bedient: die aktuell offene Frage liefert ihre Optionen als
/// <see cref="AnswerOptionView"/> (Laufzeit-Sicht), das Editieren einer früheren Antwort greift auf
/// <see cref="AnswerOptionDetail"/> aus dem Dialog-Graphen zurück.
/// </para>
/// <para>
/// Abweichend von den übrigen Designer-Modellen <c>public</c>: Der Typ wird als
/// <c>[Parameter]</c> der Komponente <c>AnswerInput</c> übergeben, und Razor erzeugt Komponenten als
/// <c>public</c> Klassen – ein <c>internal</c> Parametertyp wäre nicht zugänglich (CS0053). Der Designer
/// ist <c>IsPackable=false</c>, es entsteht dadurch keine Paket-API.
/// </para>
/// </remarks>
/// <param name="Value">Der bei Auswahl einzureichende Wert.</param>
/// <param name="Label">Die anzuzeigende Beschriftung.</param>
public sealed record AnswerChoice(string Value, string Label)
{
    /// <summary>Bildet die Optionen der Laufzeit-Sicht ab.</summary>
    /// <param name="options">Die Optionen der aktuell offenen Frage.</param>
    /// <returns>Die Auswahlmöglichkeiten in Anzeigereihenfolge.</returns>
    public static IReadOnlyList<AnswerChoice> From(IReadOnlyList<AnswerOptionView> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return [.. options.Select(option => new AnswerChoice(option.Value, option.Label))];
    }

    /// <summary>Bildet die Optionen aus dem Dialog-Graphen ab.</summary>
    /// <param name="options">Die Optionen der Frage aus dem Admin-CRUD.</param>
    /// <returns>Die Auswahlmöglichkeiten in Anzeigereihenfolge.</returns>
    public static IReadOnlyList<AnswerChoice> From(IReadOnlyList<AnswerOptionDetail> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return [.. options.Select(option => new AnswerChoice(option.Value, option.Label))];
    }
}
