using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime;

namespace Flirty.Designer.Models;

/// <summary>
/// Input state of an answer in the test runner (#43) – for the currently open question as well as for
/// editing an already given answer. Deliberately mutable, so that the Blazor input fields can bind
/// directly to it.
/// </summary>
/// <remarks>
/// <para>
/// The model holds the value in the form in which it is <b>entered</b> (text, boolean value as
/// <c>"true"</c>/<c>"false"</c>, option values of a multiple choice). The translation into the raw
/// JSON answer value of the engine is done exclusively by the <c>AnswerValueCodec</c> – none of that is
/// deliberately rebuilt here.
/// </para>
/// <para>
/// Deviating from the other designer models, this is <c>public</c>, for the same reason as
/// <see cref="AnswerChoice"/>: the type is a <c>[Parameter]</c> of the <c>AnswerInput</c> component.
/// </para>
/// </remarks>
public sealed class AnswerInputModel
{
    /// <summary>Creates the input state for the given question.</summary>
    /// <param name="type">The answer type of the question.</param>
    private AnswerInputModel(QuestionType type) => Type = type;

    /// <summary>The answer type of the question to be answered.</summary>
    public QuestionType Type { get; }

    /// <summary>
    /// The entered single value: free text, date (ISO), number, chosen option value or
    /// <c>"true"</c>/<c>"false"</c>. Unused for <see cref="QuestionType.MultiChoice"/>.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The chosen option values of a <see cref="QuestionType.MultiChoice"/> question.</summary>
    public HashSet<string> Selected { get; } = new(StringComparer.Ordinal);

    /// <summary>Creates an empty input state for a newly to be answered question.</summary>
    /// <param name="question">The currently open question.</param>
    /// <param name="sample">
    /// The example answer of the question's host-declared custom type (#137), if the designer knows one.
    /// It prefills the raw-JSON field, which is the whole practical difference between "answerable" and
    /// "usable" for a composite type – and it is only ever a starting text the author overwrites.
    /// </param>
    /// <returns>The empty input state.</returns>
    public static AnswerInputModel For(QuestionView question, string? sample = null)
    {
        ArgumentNullException.ThrowIfNull(question);

        return new AnswerInputModel(question.Type)
        {
            // Only for Json: for every other type the control derives its own display from Text, and a
            // sample would be a value the author never typed.
            Text = question.Type == QuestionType.Json && !string.IsNullOrWhiteSpace(sample)
                ? sample
                : string.Empty,
        };
    }

    /// <summary>
    /// Creates the input state from an already given answer – the starting point for editing.
    /// </summary>
    /// <param name="type">The answer type of the question.</param>
    /// <param name="value">The stored raw JSON answer value.</param>
    /// <returns>The populated input state.</returns>
    public static AnswerInputModel From(QuestionType type, string value)
    {
        var (text, selected) = AnswerValueCodec.Decode(type, value);
        var model = new AnswerInputModel(type) { Text = text };

        foreach (var entry in selected)
        {
            _ = model.Selected.Add(entry);
        }

        return model;
    }

    /// <summary>Sets or removes an option of the multiple choice.</summary>
    /// <param name="value">The option value.</param>
    /// <param name="isSelected">Whether the option should be selected.</param>
    public void Toggle(string value, bool isSelected)
    {
        _ = isSelected ? Selected.Add(value) : Selected.Remove(value);
    }

    /// <summary>
    /// Indicates whether the input can be submitted. Prevents only the obviously empty; the
    /// domain-level check deliberately stays with the engine (<c>AnswerValidator</c>), so that the runner
    /// shows exactly the messages a host app would get too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <b>hard</b> guard: both submit paths and both edit paths of the test runner ask it, so
    /// the markup cannot be bypassed into sending something the runner refuses to show.
    /// </para>
    /// <para>
    /// <see cref="QuestionType.Json"/> deliberately needs text and nothing more – <b>not</b> well-formed
    /// JSON (#137). Gating on well-formedness would make the designer the author of that message, while
    /// the point of a test run is that the answer is refused by the same
    /// <c>AnswerValidator</c> a host would use. The editor shows well-formedness as advice beside the
    /// field; the verdict stays with the engine.
    /// </para>
    /// </remarks>
    public bool CanSubmit
        => Type switch
        {
            QuestionType.MultiChoice => Selected.Count > 0,
            _ => !string.IsNullOrWhiteSpace(Text),
        };

    /// <summary>Encodes the input as a raw JSON answer value for the engine.</summary>
    /// <returns>The raw JSON text.</returns>
    public string Encode() => AnswerValueCodec.Encode(Type, Text, [.. Selected]);
}
