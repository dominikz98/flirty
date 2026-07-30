using Mediator;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Hand-written spy for <see cref="IPublisher"/> (the repo deliberately uses no mocking library):
/// records all published notifications in call order, so tests can verify the in-process triggers
/// fired by the command handlers.
/// </summary>
internal sealed class SpyPublisher : IPublisher
{
    private readonly List<INotification> _published = [];

    /// <summary>The published notifications in the order they were fired.</summary>
    public IReadOnlyList<INotification> Published => _published;

    /// <inheritdoc />
    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        _published.Add(notification);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
    {
        if (notification is INotification typed)
        {
            _published.Add(typed);
        }

        return ValueTask.CompletedTask;
    }
}
