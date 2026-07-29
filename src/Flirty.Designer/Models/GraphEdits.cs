using Flirty.Domain;

namespace Flirty.Designer.Models;

/*
 * Die Nutzlasten, mit denen die Inspector-Panels des Canvas (#103) ihre Eingaben nach oben melden.
 *
 * Warum eigene Typen und nicht die Formularmodelle: QuestionFormModel, TransitionFormModel,
 * LoopFormModel und TriggerFormModel sind `internal`, und Razor erzeugt Komponenten als `public` – ein
 * internal Typ an einem [Parameter] ist CS0053 und unter TreatWarningsAsErrors ein Buildfehler. Die
 * Modelle bleiben deshalb PRIVATER Zustand des jeweiligen Panels (dort darf `internal` stehen, weil es
 * nicht in der Parameterliste auftaucht), und über die Komponentengrenze geht nur das Ergebnis.
 *
 * Der Nebeneffekt ist die klarere Zuständigkeit: Panel = Formular samt Vorprüfungen, Seite = Commands.
 * Damit gibt es genau eine Stelle für den Gesten-Riegel und den Fehlerpfad.
 */

/// <summary>Die geänderten Kopffelder einer Frage.</summary>
/// <param name="QuestionId">Die bearbeitete Frage.</param>
/// <param name="Key">Der fachliche Schlüssel.</param>
/// <param name="Text">Der Fragetext.</param>
/// <param name="Type">Der Antworttyp.</param>
/// <param name="IsRequired">Ob eine Antwort erforderlich ist.</param>
public sealed record QuestionEdit(
    Guid QuestionId,
    string Key,
    string Text,
    QuestionType Type,
    bool IsRequired);

/// <summary>Die geänderten Felder eines Übergangs.</summary>
/// <param name="TransitionId">Der bearbeitete Übergang.</param>
/// <param name="TargetQuestionId">Die Zielfrage.</param>
/// <param name="Expression">Die Bedingung; <see langword="null"/> heißt „bedingungslos".</param>
/// <param name="IsDefault">Ob der Übergang greift, wenn keine Bedingung zutrifft.</param>
public sealed record TransitionEdit(
    Guid TransitionId,
    Guid TargetQuestionId,
    string? Expression,
    bool IsDefault);

/// <summary>Eine Umsortierung der Auswertungsreihenfolge innerhalb einer Ausgangsfrage.</summary>
/// <param name="FromQuestionId">Die Ausgangsfrage, deren Übergänge umsortiert werden.</param>
/// <param name="From">Die aktuelle Position.</param>
/// <param name="To">Die Zielposition.</param>
public sealed record TransitionMove(Guid FromQuestionId, int From, int To);

/// <summary>Ein neuer Schleifen-Marker, abgeleitet aus einem Rücksprung.</summary>
/// <param name="CollectionKey">Der Schlüssel, unter dem die Iterationen gesammelt werden.</param>
/// <param name="EntryQuestionId">Die Einstiegsfrage (das Ziel des Rücksprungs).</param>
/// <param name="BreakingQuestionId">Die Breaking Question (die Ausgangsfrage des Rücksprungs).</param>
public sealed record LoopDraft(string CollectionKey, Guid EntryQuestionId, Guid BreakingQuestionId);

/// <summary>Die neue Position eines Knotens, nachdem der Zug im Browser beendet wurde (#104).</summary>
/// <remarks>
/// Auf der Editor-Seite (#102) meldet das JS-Modul den Zug direkt an die Seite; die Laufansicht bindet
/// das Modul dagegen in der Canvas-Komponente, und die reicht ihn als ein Stück weiter.
/// </remarks>
/// <param name="QuestionId">Die verschobene Frage.</param>
/// <param name="X">Die neue waagerechte Canvas-Koordinate in px.</param>
/// <param name="Y">Die neue senkrechte Canvas-Koordinate in px.</param>
public sealed record NodeMove(Guid QuestionId, int X, int Y);

/// <summary>Ein neuer Trigger.</summary>
/// <param name="Scope">Der Zeitpunkt.</param>
/// <param name="QuestionId">Die Bezugsfrage bei <see cref="TriggerScope.AfterQuestion"/>, sonst <see langword="null"/>.</param>
/// <param name="Kind">Der Kanal.</param>
/// <param name="Config">
/// Die Konfiguration als JSON – im Panel über <c>TriggerFormModel.TryBuildConfig</c> gebaut, damit die
/// Querfeld-Regeln aus #42 (Webhook braucht eine absolute URL) vor dem Command greifen.
/// </param>
public sealed record TriggerDraft(TriggerScope Scope, Guid? QuestionId, TriggerKind Kind, string Config);
