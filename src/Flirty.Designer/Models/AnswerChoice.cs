using Flirty.Runtime;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// A selectable answer option for the input in the test runner (#43) – reduced to value and
/// label.
/// </summary>
/// <remarks>
/// <para>
/// Needed because the same input serves two sources: the currently open question provides its options as
/// <see cref="AnswerOptionView"/> (runtime view), while editing an earlier answer falls back to
/// <see cref="AnswerOptionDetail"/> from the dialog graph.
/// </para>
/// <para>
/// Deviating from the other designer models, this is <c>public</c>: the type is passed as a
/// <c>[Parameter]</c> of the <c>AnswerInput</c> component, and Razor generates components as
/// <c>public</c> classes – an <c>internal</c> parameter type would not be accessible (CS0053). The designer
/// is <c>IsPackable=false</c>, so no package API arises from it.
/// </para>
/// </remarks>
/// <param name="Value">The value to submit when selected.</param>
/// <param name="Label">The label to display.</param>
public sealed record AnswerChoice(string Value, string Label)
{
    /// <summary>Maps the options of the runtime view.</summary>
    /// <param name="options">The options of the currently open question.</param>
    /// <returns>The choices in display order.</returns>
    public static IReadOnlyList<AnswerChoice> From(IReadOnlyList<AnswerOptionView> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return [.. options.Select(option => new AnswerChoice(option.Value, option.Label))];
    }

    /// <summary>Maps the options from the dialog graph.</summary>
    /// <param name="options">The options of the question from the admin CRUD.</param>
    /// <returns>The choices in display order.</returns>
    public static IReadOnlyList<AnswerChoice> From(IReadOnlyList<AnswerOptionDetail> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return [.. options.Select(option => new AnswerChoice(option.Value, option.Label))];
    }
}
