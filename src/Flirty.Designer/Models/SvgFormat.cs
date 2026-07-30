using System.Globalization;

namespace Flirty.Designer.Models;

/// <summary>
/// Number formatting for SVG attributes – the <b>only</b> allowed way to write a number into SVG markup
/// in the designer.
/// </summary>
/// <remarks>
/// <para>
/// <c>DesignerApp.ConfigureServices</c> sets <c>CultureInfo.DefaultThreadCurrentCulture</c> to the
/// configurable display culture (<c>DesignerApp.DisplayCulture</c>). If that is a comma-decimal culture,
/// an interpolated <see cref="double"/> coordinate writes <c>12,5</c> instead of <c>12.5</c> – and since
/// a comma is a <b>separator</b> in SVG path syntax, a coordinate silently turns into a wrong number
/// sequence. There is neither an exception nor a console message, only a wrong picture. The guard is
/// therefore against the display culture, whatever it is set to, not against one specific culture.
/// </para>
/// <para>
/// Affects all numeric SVG attributes: <c>d</c>, <c>transform</c>, <c>viewBox</c>, <c>x</c>/<c>y</c>,
/// <c>width</c>/<c>height</c>, <c>stroke-dasharray</c>. When reviewing, specifically look for interpolated
/// <see cref="double"/> values in <c>.razor</c> files.
/// </para>
/// <para>
/// The type is <see langword="public"/> because it is called from Razor markup.
/// </para>
/// </remarks>
public static class SvgFormat
{
    /// <summary>
    /// Formats a number for an SVG attribute: at most two decimal places, a decimal <b>point</b>
    /// regardless of the circuit's culture.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>The culture-independent text form.</returns>
    public static string N(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
