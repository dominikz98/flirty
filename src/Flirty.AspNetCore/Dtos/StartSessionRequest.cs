namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Anfrage-Körper für <c>POST /flirty/sessions</c>: startet den Dialog mit dem angegebenen Schlüssel
/// für den Anwender oder setzt eine bereits laufende Session desselben Anwenders fort (Resume).
/// Wird auf das <see cref="Flirty.Runtime.StartDialogCommand"/> gemappt.
/// </summary>
/// <param name="DialogKey">Der fachliche, stabile Schlüssel des zu startenden Dialogs.</param>
/// <param name="ExternalUserKey">Der fachliche Anwenderschlüssel der Host-App (z. B. Benutzer-Id).</param>
public sealed record StartSessionRequest(string DialogKey, string ExternalUserKey);
