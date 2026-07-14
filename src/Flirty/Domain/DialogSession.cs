namespace Flirty.Domain;

/// <summary>
/// Der Laufzeitzustand eines von einem Anwender durchlaufenen <see cref="Dialog"/> und zugleich
/// das Aggregat-Root der Runtime-Ebene. Ermöglicht das Fortsetzen (Resume) über
/// <see cref="CurrentQuestionId"/>. Die Session pinnt mit <see cref="DialogVersion"/> die
/// Dialogversion und ist dadurch vom editierbaren Konfigurationsgraphen entkoppelt.
/// </summary>
public sealed class DialogSession
{
    /// <summary>Eindeutiger Primärschlüssel der Session.</summary>
    public Guid Id { get; set; }

    /// <summary>Verweis auf den durchlaufenen <see cref="Dialog"/> (<see cref="Dialog.Id"/>).</summary>
    public Guid DialogId { get; set; }

    /// <summary>
    /// Die zum Startzeitpunkt gepinnte <see cref="Dialog.Version"/>, damit spätere Änderungen
    /// am Dialog diese Session nicht brechen.
    /// </summary>
    public int DialogVersion { get; set; }

    /// <summary>Fachlicher Schlüssel des Anwenders/Kontexts der Host-App (z. B. Benutzer-Id).</summary>
    public required string ExternalUserKey { get; set; }

    /// <summary>Der aktuelle Lebenszyklus-Status der Session.</summary>
    public SessionStatus Status { get; set; }

    /// <summary>
    /// Verweis auf die aktuell offene Frage (<see cref="Question.Id"/>) für den Resume;
    /// <see langword="null"/>, sobald der Dialog abgeschlossen ist.
    /// </summary>
    public Guid? CurrentQuestionId { get; set; }

    /// <summary>Zeitpunkt des Starts der Session.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Zeitpunkt des Abschlusses der Session oder <see langword="null"/>, solange sie läuft.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Die im Verlauf der Session gegebenen Antworten.</summary>
    public ICollection<SessionAnswer> Answers { get; set; } = [];
}
