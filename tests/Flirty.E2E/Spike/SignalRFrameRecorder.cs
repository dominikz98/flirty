using System.Collections.Concurrent;
using System.Text;
using Microsoft.Playwright;

namespace Flirty.E2E.Spike;

/// <summary>Richtung einer SignalR-Nachricht aus Sicht der Seite.</summary>
internal enum FrameDirection
{
    /// <summary>Seite → Server.</summary>
    Sent,

    /// <summary>Server → Seite.</summary>
    Received,
}

/// <summary>Eine einzelne SignalR-Nachricht.</summary>
internal sealed record SignalRMessage(FrameDirection Direction, int MessageType, string Target, int Bytes);

/// <summary>Die Bilanz einer Geste.</summary>
/// <param name="Sent">Nutz-Nachrichten Seite → Server (ohne Ping/Ack).</param>
/// <param name="Received">Nutz-Nachrichten Server → Seite (ohne Ping/Ack).</param>
/// <param name="Bytes">Gesamte Nutzlast beider Richtungen – die Zahl, die jede Diskussion über Framing überlebt.</param>
/// <param name="Breakdown">Aufschlüsselung nach erkanntem Ziel, z. B. <c>DispatchBrowserEvent</c>.</param>
internal sealed record GestureTraffic(int Sent, int Received, int Bytes, IReadOnlyDictionary<string, int> Breakdown)
{
    /// <summary>Nutz-Nachrichten insgesamt.</summary>
    public int Total => Sent + Received;

    /// <summary>Kurzform der Aufschlüsselung für die Ergebnistabelle.</summary>
    public string BreakdownText
        => string.Join(", ", Breakdown.OrderByDescending(p => p.Value).Select(p => $"{p.Key}×{p.Value}"));
}

/// <summary>
/// SPIKE #100 (Wegwerf, wird NICHT gemergt): zählt, was eine Zieh-Geste den Circuit tatsächlich kostet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ein WebSocket-Frame ist nicht eine SignalR-Nachricht.</b> Das binäre <c>blazorpack</c>-Protokoll
/// packt beliebig viele Nachrichten mit einem 7-Bit-Varint-Längenpräfix in einen Frame; wer Frames
/// zählt, zählt zu wenig. Deshalb läuft der Recorder über jedes Payload und trennt die Nachrichten
/// selbst.
/// </para>
/// <para>
/// <b>Was herausgerechnet wird:</b> der Protokoll-Handshake (<c>0x7B</c>…<c>0x1E</c>, JSON statt
/// Längenpräfix) sowie Keep-Alive-Pings (Typ 6, exakt <c>02 91 06</c>) und Ack/Sequence (Typ 8/9).
/// <b>Was bewusst drin bleibt:</b> <c>OnRenderCompleted</c>. Blazor Server quittiert jeden Render-Batch
/// – ein Pointer-Move kostet deshalb drei Nachrichten, nicht zwei. Das gehört zu den Kosten des
/// Verfahrens und wird ausgewiesen statt wegdefiniert.
/// </para>
/// </remarks>
internal sealed class SignalRFrameRecorder
{
    private readonly ConcurrentQueue<SignalRMessage> _messages = new();

    private volatile bool _recording;
    private long _lastMessageTicks;

    /// <summary>Wurde überhaupt eine Blazor-WebSocket beobachtet?</summary>
    public bool WebSocketSeen { get; private set; }

    /// <summary>
    /// Hängt sich an die Blazor-WebSocket der Seite. Muss <b>vor</b> der Navigation aufgerufen werden,
    /// sonst ist die Verbindung schon aufgebaut und das Ereignis kommt nie.
    /// </summary>
    /// <param name="page">Die Seite.</param>
    public void Attach(IPage page)
    {
        page.WebSocket += (_, socket) =>
        {
            if (!socket.Url.Contains("_blazor", StringComparison.Ordinal))
            {
                return;
            }

            WebSocketSeen = true;
            socket.FrameSent += (_, frame) => Record(FrameDirection.Sent, frame.Binary);
            socket.FrameReceived += (_, frame) => Record(FrameDirection.Received, frame.Binary);
        };
    }

    /// <summary>Beginnt die Zählung – unmittelbar vor der Geste aufrufen.</summary>
    public void Start()
    {
        _messages.Clear();
        _lastMessageTicks = DateTime.UtcNow.Ticks;
        _recording = true;
    }

    /// <summary>
    /// Beendet die Zählung. Wartet vorher <paramref name="minimumWait"/> ab und danach, bis 250 ms lang
    /// keine Nachricht mehr eintraf (Deckel 3 s).
    /// </summary>
    /// <remarks>
    /// Die Mindestwartezeit ist nicht Vorsicht, sondern notwendig: Ein Prototyp, der <b>während</b> der
    /// Geste schweigt, hätte beim Aufruf schon länger als die Leerlaufschwelle nichts gesendet – die
    /// Schleife bräche sofort ab, noch bevor die verzögerte <c>pointerup</c>-Nachricht eintrifft, und
    /// zählte fälschlich <b>null</b>. Genau das ist beim ersten Messlauf passiert.
    /// </remarks>
    /// <param name="minimumWait">Mindestwartezeit; sinnvoll ist mindestens eine Umlaufzeit plus Puffer.</param>
    /// <returns>Die Bilanz der Geste.</returns>
    public async Task<GestureTraffic> StopAsync(TimeSpan minimumWait)
    {
        await Task.Delay(minimumWait);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var idle = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastMessageTicks), DateTimeKind.Utc);
            if (idle > TimeSpan.FromMilliseconds(250))
            {
                break;
            }

            await Task.Delay(50);
        }

        _recording = false;

        var messages = _messages.ToArray();
        var breakdown = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            breakdown[message.Target] = breakdown.GetValueOrDefault(message.Target) + 1;
        }

        return new GestureTraffic(
            messages.Count(m => m.Direction == FrameDirection.Sent),
            messages.Count(m => m.Direction == FrameDirection.Received),
            messages.Sum(m => m.Bytes),
            breakdown);
    }

    private void Record(FrameDirection direction, byte[]? payload)
    {
        if (!_recording || payload is null || payload.Length == 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastMessageTicks, DateTime.UtcNow.Ticks);

        // Handshake: 0x1E-terminiertes JSON statt Längenpräfix. Liegt normalerweise weit vor dem
        // Messfenster, wird aber sicherheitshalber erkannt und übersprungen.
        if (payload[0] == 0x7B)
        {
            return;
        }

        foreach (var body in SplitMessages(payload))
        {
            var type = MessageTypeOf(body);
            if (type is 6 or 8 or 9)
            {
                // 6 = Ping (Keep-Alive), 8 = Ack, 9 = Sequence: Protokoll-Rauschen, keine Gestenkosten.
                continue;
            }

            _messages.Enqueue(new SignalRMessage(direction, type, TargetOf(body, direction, type), body.Length));
        }
    }

    /// <summary>Trennt die Nachrichten eines Frames anhand ihres 7-Bit-Varint-Längenpräfixes.</summary>
    private static IEnumerable<byte[]> SplitMessages(byte[] payload)
    {
        var offset = 0;
        while (offset < payload.Length)
        {
            var length = 0;
            var shift = 0;
            byte current;
            do
            {
                if (offset >= payload.Length || shift > 28)
                {
                    yield break;
                }

                current = payload[offset++];
                length |= (current & 0x7F) << shift;
                shift += 7;
            }
            while ((current & 0x80) != 0);

            if (length <= 0 || offset + length > payload.Length)
            {
                yield break;
            }

            yield return payload[offset..(offset + length)];
            offset += length;
        }
    }

    /// <summary>
    /// Der SignalR-Nachrichtentyp: erstes Element des MessagePack-Arrays. Das Array beginnt mit einem
    /// fixarray-Kopf (<c>0x9X</c>), danach steht der Typ als positive fixint.
    /// </summary>
    private static int MessageTypeOf(byte[] body)
        => body.Length >= 2 && (body[0] & 0xF0) == 0x90 && body[1] <= 0x7F ? body[1] : -1;

    /// <summary>
    /// Der Zielname einer Invocation, aus dem MessagePack gelesen statt geraten. Layout einer
    /// SignalR-Invocation: <c>[1, Headers(map), InvocationId(str|nil), Target(str), Arguments(array), …]</c>
    /// – also zwei Werte überspringen, dann die Zeichenkette lesen.
    /// </summary>
    /// <remarks>
    /// Der erste Entwurf suchte den Namen als Teilstring in einer Liste bekannter Ziele. Das ist kein
    /// Parsen, sondern Raten: Die Liste bestimmt das Ergebnis, und ein unbekanntes Ziel wird still als
    /// ein bekanntes verbucht. Für Zahlen, die in einen ADR wandern, ist das zu wenig.
    /// </remarks>
    private static string TargetOf(byte[] body, FrameDirection direction, int type)
    {
        // Nur Invocation (1) und StreamInvocation (4) tragen ein Ziel.
        if (type is not (1 or 4))
        {
            return $"{direction}:Typ{type}";
        }

        var offset = 1;
        if (!SkipValue(body, ref offset)      // Nachrichtentyp
            || !SkipValue(body, ref offset)   // Headers
            || !SkipValue(body, ref offset))  // InvocationId
        {
            return $"{direction}:Typ{type}";
        }

        return ReadString(body, ref offset) ?? $"{direction}:Typ{type}";
    }

    /// <summary>Liest eine MessagePack-Zeichenkette, sofern an dieser Stelle eine steht.</summary>
    private static string? ReadString(byte[] body, ref int offset)
    {
        if (offset >= body.Length)
        {
            return null;
        }

        var header = body[offset];
        int length;
        if (header is >= 0xA0 and <= 0xBF)
        {
            length = header & 0x1F;
            offset++;
        }
        else if (header == 0xD9 && offset + 1 < body.Length)
        {
            length = body[offset + 1];
            offset += 2;
        }
        else if (header == 0xDA && offset + 2 < body.Length)
        {
            length = (body[offset + 1] << 8) | body[offset + 2];
            offset += 3;
        }
        else
        {
            return null;
        }

        if (offset + length > body.Length)
        {
            return null;
        }

        var value = Encoding.UTF8.GetString(body, offset, length);
        offset += length;
        return value;
    }

    /// <summary>
    /// Überspringt genau einen MessagePack-Wert. Deckt die Typen ab, die SignalR/Blazor benutzt;
    /// alles Unbekannte führt zu <c>false</c> statt zu einer stillen Fehlinterpretation.
    /// </summary>
    private static bool SkipValue(byte[] body, ref int offset)
    {
        if (offset >= body.Length)
        {
            return false;
        }

        // Aufsteigend geordnet, damit die Untergrenzen implizit sind (der Compiler beanstandet sonst
        // redundante Muster). Die tatsächlichen Bereiche stehen im Kommentar.
        var header = body[offset++];
        switch (header)
        {
            case <= 0x7F: // 0x00–0x7F positive fixint
                return true;
            case <= 0x8F: // 0x80–0x8F fixmap
                return SkipMany(body, ref offset, 2 * (header & 0x0F));
            case <= 0x9F: // 0x90–0x9F fixarray
                return SkipMany(body, ref offset, header & 0x0F);
            case <= 0xBF: // 0xA0–0xBF fixstr
                return Advance(body, ref offset, header & 0x1F);
            case >= 0xE0: // 0xE0–0xFF negative fixint
            case 0xC0 or 0xC2 or 0xC3: // nil / false / true
                return true;
            case 0xC4 or 0xD9: // bin8 / str8
                return ReadLengthThenAdvance(body, ref offset, 1);
            case 0xC5 or 0xDA: // bin16 / str16
                return ReadLengthThenAdvance(body, ref offset, 2);
            case 0xC6 or 0xDB: // bin32 / str32
                return ReadLengthThenAdvance(body, ref offset, 4);
            case 0xCA or 0xCE or 0xD2: // float32 / uint32 / int32
                return Advance(body, ref offset, 4);
            case 0xCB or 0xCF or 0xD3: // float64 / uint64 / int64
                return Advance(body, ref offset, 8);
            case 0xCC or 0xD0: // uint8 / int8
                return Advance(body, ref offset, 1);
            case 0xCD or 0xD1: // uint16 / int16
                return Advance(body, ref offset, 2);
            case 0xDC or 0xDE: // array16 / map16
            {
                if (offset + 2 > body.Length)
                {
                    return false;
                }

                var count = (body[offset] << 8) | body[offset + 1];
                offset += 2;
                return SkipMany(body, ref offset, header == 0xDE ? 2 * count : count);
            }

            default:
                return false;
        }
    }

    private static bool SkipMany(byte[] body, ref int offset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!SkipValue(body, ref offset))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Advance(byte[] body, ref int offset, int count)
    {
        offset += count;
        return offset <= body.Length;
    }

    private static bool ReadLengthThenAdvance(byte[] body, ref int offset, int lengthBytes)
    {
        if (offset + lengthBytes > body.Length)
        {
            return false;
        }

        var length = 0;
        for (var i = 0; i < lengthBytes; i++)
        {
            length = (length << 8) | body[offset + i];
        }

        offset += lengthBytes;
        return Advance(body, ref offset, length);
    }
}
