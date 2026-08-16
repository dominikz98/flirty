using System.Globalization;
using Flirty.Placeholders;

namespace Flirty.Samples.Web;

/// <summary>
/// A second worked placeholder (#140): resolves <c>{{today}}</c> to the delivery date. Declared in
/// <c>WebSampleApp</c> as <c>today</c> and referenced by the demo dialog's final question text.
/// </summary>
/// <remarks>
/// It reads the point in time from <see cref="PlaceholderContext.ExpressionContext"/> – the same
/// evaluation context a branching condition sees – rather than calling <c>DateTimeOffset.UtcNow</c>
/// itself, so what the placeholder shows and what a condition would evaluate against agree.
/// </remarks>
public sealed class TodayPlaceholderFiller : IPlaceholderFiller
{
    /// <inheritdoc />
    public ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var today = context.ExpressionContext.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new ValueTask<string?>(today);
    }
}
