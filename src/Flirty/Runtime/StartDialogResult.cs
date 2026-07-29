using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Result of <see cref="StartDialogCommand"/> or <see cref="IFlirtyEngine.StartDialogAsync"/>:
/// the (newly created or resumed) session along with the question currently to be answered.
/// </summary>
/// <param name="SessionId">The primary key of the running <see cref="DialogSession"/>.</param>
/// <param name="IsResumed">
/// <see langword="true"/> if an already running session was resumed; <see langword="false"/>
/// if the dialog was started anew.
/// </param>
/// <param name="CurrentQuestion">The currently open question to be presented to the user.</param>
public sealed record StartDialogResult(Guid SessionId, bool IsResumed, QuestionView CurrentQuestion);

/// <summary>
/// Lean, immutable view of a <see cref="Question"/> for the runtime API – without
/// EF Core navigations, so that host apps can display the question without knowing the
/// configuration graph.
/// </summary>
/// <param name="Id">The primary key of the question.</param>
/// <param name="Key">The business, stable key of the question.</param>
/// <param name="Text">The question text to display.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="Options">
/// The answer options of the question in display order (empty for free-text/value types).
/// </param>
public sealed record QuestionView(
    Guid Id,
    string Key,
    string Text,
    QuestionType Type,
    IReadOnlyList<AnswerOptionView> Options);

/// <summary>
/// Lean, immutable view of an <see cref="AnswerOption"/> for the runtime API.
/// </summary>
/// <param name="Id">The primary key of the answer option.</param>
/// <param name="Key">The business, stable key of the option.</param>
/// <param name="Label">The label of the option to display.</param>
/// <param name="Value">The value of the option to store.</param>
public sealed record AnswerOptionView(Guid Id, string Key, string Label, string Value);
