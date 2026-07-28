using System.Globalization;

namespace Flirty.Designer.Models;

/// <summary>
/// Zahlformatierung für SVG-Attribute – die <b>einzige</b> erlaubte Art, im Designer eine Zahl in
/// SVG-Markup zu schreiben.
/// </summary>
/// <remarks>
/// <para>
/// <c>DesignerApp.ConfigureServices</c> setzt <c>CultureInfo.DefaultThreadCurrentCulture</c> auf
/// <c>de-DE</c> (#95: Datumsformate in deutschem Fließtext). Eine per Interpolation eingesetzte
/// <see cref="double"/>-Koordinate schreibt damit <c>12,5</c> statt <c>12.5</c> – und da ein Komma in
/// der SVG-Pfadsyntax ein <b>Trennzeichen</b> ist, wird aus einer Koordinate stillschweigend eine
/// falsche Zahlenfolge. Es gibt weder eine Ausnahme noch eine Konsolenmeldung, nur ein falsches Bild.
/// </para>
/// <para>
/// Betrifft alle numerischen SVG-Attribute: <c>d</c>, <c>transform</c>, <c>viewBox</c>, <c>x</c>/<c>y</c>,
/// <c>width</c>/<c>height</c>, <c>stroke-dasharray</c>. Beim Review gezielt nach interpolierten
/// <see cref="double"/>-Werten in <c>.razor</c>-Dateien suchen.
/// </para>
/// <para>
/// Der Typ ist <see langword="public"/>, weil er aus Razor-Markup heraus aufgerufen wird.
/// </para>
/// </remarks>
public static class SvgFormat
{
    /// <summary>
    /// Formatiert eine Zahl für ein SVG-Attribut: höchstens zwei Nachkommastellen, Dezimal<b>punkt</b>
    /// unabhängig von der Kultur des Circuits.
    /// </summary>
    /// <param name="value">Der Zahlwert.</param>
    /// <returns>Die kulturunabhängige Textform.</returns>
    public static string N(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
