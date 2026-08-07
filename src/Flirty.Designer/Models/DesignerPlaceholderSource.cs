namespace Flirty.Designer.Models;

/// <summary>
/// What the designer made of its placeholder descriptor file at startup: where it looked, whether the
/// file was there, and everything it had to skip.
/// </summary>
/// <remarks>
/// Registered as a singleton in <see cref="DesignerApp"/> and rendered read-only by the
/// <c>Placeholders</c> page. It exists for the same reason as <see cref="DesignerQuestionTypeSource"/>: a
/// typo in a key or a duplicate entry is skipped rather than thrown (a startup crash over a display name
/// would be absurd), and a skipped entry that nobody reports is indistinguishable from one that was never
/// written – so the limit has to be on screen, not only in a guide.
/// </remarks>
/// <param name="FilePath">Full path the designer read (or would have read) the descriptors from.</param>
/// <param name="FileExists">
/// Whether that file was present. Its absence is the normal case, not a problem: without descriptors the
/// designer shows a marker's raw key and everything else works unchanged.
/// </param>
/// <param name="Problems">
/// Human-readable messages for entries that were skipped, in file order. Empty when everything loaded.
/// </param>
internal sealed record DesignerPlaceholderSource(
    string FilePath,
    bool FileExists,
    IReadOnlyList<string> Problems);
