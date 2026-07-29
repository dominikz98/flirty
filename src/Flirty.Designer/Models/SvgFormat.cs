using System.Globalization;

namespace Flirty.Designer.Models;

/// <summary>
/// Number formatting for SVG attributes – the <b>only</b> permitted way to write a number into
/// SVG markup in the designer.
/// </summary>
/// <remarks>
/// <para>
/// <c>DesignerApp.ConfigureServices</c> sets <c>CultureInfo.DefaultThreadCurrentCulture</c> to
/// <c>de-DE</c> (#95: date formats in German running text). A <see cref="double"/> coordinate inserted by
/// interpolation thereby writes <c>12,5</c> instead of <c>12.5</c> – and since a comma in
/// the SVG path syntax is a <b>separator</b>, a coordinate silently becomes a
/// wrong number sequence. There is neither an exception nor a console message, only a wrong picture.
/// </para>
/// <para>
/// Affects all numeric SVG attributes: <c>d</c>, <c>transform</c>, <c>viewBox</c>, <c>x</c>/<c>y</c>,
/// <c>width</c>/<c>height</c>, <c>stroke-dasharray</c>. In review, deliberately look for interpolated
/// <see cref="double"/> values in <c>.razor</c> files.
/// </para>
/// <para>
/// The type is <see langword="public"/> because it is called from Razor markup.
/// </para>
/// </remarks>
public static class SvgFormat
{
    /// <summary>
    /// Formats a number for an SVG attribute: at most two decimal places, decimal <b>point</b>
    /// independent of the culture of the circuit.
    /// </summary>
    /// <param name="value">The number value.</param>
    /// <returns>The culture-independent text form.</returns>
    public static string N(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
