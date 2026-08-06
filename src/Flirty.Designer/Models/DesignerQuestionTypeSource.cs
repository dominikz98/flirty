namespace Flirty.Designer.Models;

/// <summary>
/// What the designer made of its question-type descriptor file at startup: where it looked, whether the
/// file was there, and everything it had to skip.
/// </summary>
/// <remarks>
/// Registered as a singleton in <see cref="DesignerApp"/> and rendered read-only by the
/// <c>QuestionTypes</c> page. It exists because the alternative is silence: a typo in a key, a malformed
/// sample or a duplicate entry is skipped rather than thrown (a startup crash over a display name would
/// be absurd), and a skipped entry that nobody reports is indistinguishable from one that was never
/// written. Same argument as #118's publish confirmation – a limit has to be on screen, not only in a
/// guide.
/// </remarks>
/// <param name="FilePath">Full path the designer read (or would have read) the descriptors from.</param>
/// <param name="FileExists">
/// Whether that file was present. Its absence is the normal case, not a problem: without descriptors the
/// designer behaves exactly as it did after #136.
/// </param>
/// <param name="Problems">
/// Human-readable messages for entries that were skipped, in file order. Empty when everything loaded.
/// </param>
internal sealed record DesignerQuestionTypeSource(
    string FilePath,
    bool FileExists,
    IReadOnlyList<string> Problems);
