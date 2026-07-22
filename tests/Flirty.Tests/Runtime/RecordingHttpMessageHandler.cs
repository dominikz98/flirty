using System.Net;
using Flirty.Runtime;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Handgeschriebener <c>HttpMessageHandler</c>-Spy für die Webhook-Handler-Tests (#33) – das Repo nutzt
/// bewusst keine Mock-Bibliothek. Zeichnet jede Anfrage (Methode/URL/Event-Header/Body) auf und liefert
/// skriptbare Statuscodes (der letzte wird bei weiteren Aufrufen wiederholt) bzw. wirft eine
/// <see cref="HttpRequestException"/>, um Zustellfehler zu simulieren.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpStatusCode> _statuses;
    private readonly bool _throws;

    /// <summary>Erstellt den Spy mit den nacheinander zu liefernden Statuscodes (Default: einmal 200).</summary>
    /// <param name="statuses">Die je Aufruf gelieferten Statuscodes; der letzte gilt für alle weiteren Aufrufe.</param>
    public RecordingHttpMessageHandler(params HttpStatusCode[] statuses)
        => _statuses = new Queue<HttpStatusCode>(statuses.Length == 0 ? [HttpStatusCode.OK] : statuses);

    private RecordingHttpMessageHandler(bool throws)
        : this() => _throws = throws;

    /// <summary>Die aufgezeichneten Anfragen in Aufrufreihenfolge.</summary>
    public List<RecordedWebhookRequest> Requests { get; } = [];

    /// <summary>Erzeugt einen Spy, der bei jedem Aufruf eine <see cref="HttpRequestException"/> wirft.</summary>
    /// <returns>Der werfende Spy.</returns>
    public static RecordingHttpMessageHandler Throwing() => new(throws: true);

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var @event = request.Headers.TryGetValues(WebhookNotificationHandler.EventHeaderName, out var values)
            ? values.FirstOrDefault()
            : null;
        var trigger = request.Headers.TryGetValues(WebhookNotificationHandler.TriggerHeaderName, out var names)
            ? names.FirstOrDefault()
            : null;
        Requests.Add(new RecordedWebhookRequest(request.Method, request.RequestUri, @event, trigger, body));

        if (_throws)
        {
            throw new HttpRequestException("Simulierter Zustellfehler.");
        }

        var status = _statuses.Count > 1 ? _statuses.Dequeue() : _statuses.Peek();
        return new HttpResponseMessage(status);
    }
}

/// <summary>Eine vom <see cref="RecordingHttpMessageHandler"/> aufgezeichnete HTTP-Anfrage.</summary>
/// <param name="Method">Die HTTP-Methode.</param>
/// <param name="Url">Die Ziel-URL.</param>
/// <param name="Event">Der Wert des <c>X-Flirty-Event</c>-Headers (oder <see langword="null"/>).</param>
/// <param name="Trigger">Der Wert des <c>X-Flirty-Trigger</c>-Headers (oder <see langword="null"/>).</param>
/// <param name="Body">Der (roh gelesene) Anfrage-Body.</param>
internal sealed record RecordedWebhookRequest(
    HttpMethod Method, Uri? Url, string? Event, string? Trigger, string? Body);
