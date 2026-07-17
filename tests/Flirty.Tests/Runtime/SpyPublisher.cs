using Mediator;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Handgeschriebener Spy für <see cref="IPublisher"/> (das Repo nutzt bewusst keine Mock-Bibliothek):
/// zeichnet alle publizierten Notifications in Aufrufreihenfolge auf, damit Tests die von den
/// Command-Handlern ausgelösten In-Process-Trigger verifizieren können.
/// </summary>
internal sealed class SpyPublisher : IPublisher
{
    private readonly List<INotification> _published = [];

    /// <summary>Die publizierten Notifications in der Reihenfolge ihrer Auslösung.</summary>
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
