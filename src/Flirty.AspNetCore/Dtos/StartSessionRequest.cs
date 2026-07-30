namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Request body for <c>POST /flirty/sessions</c>: starts the dialog with the given key
/// for the user or resumes a session of the same user that is already running.
/// Mapped onto the <see cref="Flirty.Runtime.StartDialogCommand"/>.
/// </summary>
/// <param name="DialogKey">The business, stable key of the dialog to start.</param>
/// <param name="ExternalUserKey">The host app's business user key (e.g. user id).</param>
public sealed record StartSessionRequest(string DialogKey, string ExternalUserKey);
