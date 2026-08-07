using Flirty.Placeholders;

namespace Flirty.Samples.Web;

/// <summary>
/// The worked example of a message placeholder (#140): resolves <c>{{user-name}}</c> to a live value at
/// delivery time. Declared in <c>WebSampleApp</c> as <c>user-name</c> and referenced by the demo dialog's
/// entry question text (<c>"Hi {{user-name}}! …"</c>).
/// </summary>
/// <remarks>
/// <para>
/// It takes a constructor dependency purely to show that it can: a filler is resolved from the
/// <b>request scope</b>, so it may use scoped services – an <c>HttpClient</c>, options, or the same
/// <c>FlirtyDbContext</c> the handler uses. That is the whole reason a filler is an interface rather than a
/// <c>Func&lt;&gt;</c> delegate.
/// </para>
/// <para>
/// A real host would look the name up (a user table via the <c>FlirtyDbContext</c>, a profile service over
/// <c>HttpClient</c>) keyed by <see cref="PlaceholderContext.ExternalUserKey"/>. The sample simply greets
/// by that key, which keeps the demonstration self-contained.
/// </para>
/// </remarks>
public sealed class UserNamePlaceholderFiller : IPlaceholderFiller
{
    private readonly ILogger<UserNamePlaceholderFiller> _logger;

    /// <summary>Creates the filler.</summary>
    /// <param name="logger">Logger, present to demonstrate scoped resolution.</param>
    public UserNamePlaceholderFiller(ILogger<UserNamePlaceholderFiller> logger) => _logger = logger;

    /// <inheritdoc />
    public ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation(
            "Resolving {{user-name}} for session {SessionId}.", context.SessionId);

        return new ValueTask<string?>(context.ExternalUserKey);
    }
}
