using System.Net;
using Flirty.Runtime;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Hand-written <c>HttpMessageHandler</c> spy for the webhook handler tests (#33) – the repo
/// deliberately uses no mocking library. Records every request (method/URL/event header/body) and
/// returns scriptable status codes (the last one is repeated on further calls) or throws an
/// <see cref="HttpRequestException"/> to simulate a delivery failure.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpStatusCode> _statuses;
    private readonly bool _throws;

    /// <summary>Creates the spy with the status codes to return one after another (default: 200 once).</summary>
    /// <param name="statuses">The status codes returned per call; the last one applies to all further calls.</param>
    public RecordingHttpMessageHandler(params HttpStatusCode[] statuses)
        => _statuses = new Queue<HttpStatusCode>(statuses.Length == 0 ? [HttpStatusCode.OK] : statuses);

    private RecordingHttpMessageHandler(bool throws)
        : this() => _throws = throws;

    /// <summary>The recorded requests in call order.</summary>
    public List<RecordedWebhookRequest> Requests { get; } = [];

    /// <summary>Creates a spy that throws an <see cref="HttpRequestException"/> on every call.</summary>
    /// <returns>The throwing spy.</returns>
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
            throw new HttpRequestException("Simulated delivery failure.");
        }

        var status = _statuses.Count > 1 ? _statuses.Dequeue() : _statuses.Peek();
        return new HttpResponseMessage(status);
    }
}

/// <summary>An HTTP request recorded by the <see cref="RecordingHttpMessageHandler"/>.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Url">The target URL.</param>
/// <param name="Event">The value of the <c>X-Flirty-Event</c> header (or <see langword="null"/>).</param>
/// <param name="Trigger">The value of the <c>X-Flirty-Trigger</c> header (or <see langword="null"/>).</param>
/// <param name="Body">The (raw) request body.</param>
internal sealed record RecordedWebhookRequest(
    HttpMethod Method, Uri? Url, string? Event, string? Trigger, string? Body);
