using Flirty.Domain;
using Flirty.Validation;

namespace Flirty.Designer.Models;

/// <summary>
/// English display texts for the <see cref="QuestionType"/> values, and the list of types a question can
/// actually be authored as. Centralized, so that the question list (<c>DialogEditor</c>), the question
/// editor (<c>QuestionEditor</c>), the canvas palette and the graph inspector use the same labels.
/// </summary>
/// <remarks>
/// Every member takes the <see cref="FlirtyQuestionTypeRegistry"/> as an <b>optional</b> parameter
/// (#137). Passing it resolves a host-declared type to its display name; omitting it reproduces the
/// behaviour from #136 exactly, and that is deliberate rather than convenient – the no-registry arm is
/// the same code that shipped, so "a key with no descriptor still renders as the key" cannot regress
/// through a new branch.
/// </remarks>
internal static class QuestionTypeLabels
{
    /// <summary>
    /// Prefix of a <see cref="QuestionTypeChoice.Value"/> that stands for a host-declared type rather
    /// than a bare <see cref="QuestionType"/>. Not a valid enum member name, so the two never collide.
    /// </summary>
    private const string CustomChoicePrefix = "custom:";

    /// <summary>Returns the display text of the given question type.</summary>
    /// <param name="type">The question type.</param>
    /// <param name="customTypeKey">
    /// The question's <see cref="Question.CustomTypeKey"/>, if any. Passing it makes the host-declared
    /// type visible wherever a question is described.
    /// </param>
    /// <param name="registry">
    /// The declared custom question types, if the caller has them. With a matching declaration the type
    /// is named by its display name; without one the raw key is still the only honest thing to show.
    /// </param>
    /// <returns>The English display text (including the technical name for recognition).</returns>
    public static string Describe(
        QuestionType type,
        string? customTypeKey = null,
        FlirtyQuestionTypeRegistry? registry = null) => type switch
    {
        QuestionType.SingleChoice => "Single choice (SingleChoice)",
        QuestionType.MultiChoice => "Multiple choice (MultiChoice)",
        QuestionType.FreeText => "Free text (FreeText)",
        QuestionType.Number => "Number (Number)",
        QuestionType.Date => "Date (Date)",
        QuestionType.Boolean => "Yes/No (Boolean)",
        QuestionType.Json when Declared(customTypeKey, registry) is { } declared
            => $"{declared.DisplayName} ({declared.Key})",
        QuestionType.Json when !string.IsNullOrWhiteSpace(customTypeKey)
            => $"Custom type \"{customTypeKey}\" (Json)",
        QuestionType.Json => "JSON or custom type (Json)",
        _ => type.ToString(),
    };

    /// <summary>Indicates whether the question type evaluates answer options (choice types).</summary>
    /// <param name="type">The question type.</param>
    /// <returns><see langword="true"/> for <see cref="QuestionType.SingleChoice"/> and <see cref="QuestionType.MultiChoice"/>.</returns>
    /// <remarks>
    /// Deliberately unchanged for <see cref="QuestionType.Json"/>: this is a positive predicate about the
    /// <b>engine</b>, and the engine does not evaluate options for a JSON question. It says nothing about
    /// a host's custom validator, which receives <c>question.Options</c> and may well read them – so a
    /// caller must not turn a <see langword="false"/> here into "these options are useless".
    /// </remarks>
    public static bool UsesOptions(QuestionType type)
        => type is QuestionType.SingleChoice or QuestionType.MultiChoice;

    /// <summary>
    /// The types a question can be authored as: the built-in <see cref="QuestionType"/> values followed
    /// by one entry per host-declared custom type.
    /// </summary>
    /// <param name="registry">The declared custom question types, if the caller has them.</param>
    /// <returns>
    /// The choices in display order. With no declaration (or no registry) this is exactly the built-in
    /// list, which is what makes "the designer behaves as before without descriptors" checkable.
    /// </returns>
    /// <remarks>
    /// Lives here rather than in a component's <c>@code</c> block for the reason #103 recorded: rules in
    /// a Razor code block are not unit-testable, and this one is consumed by four surfaces (the
    /// new-question dropdown, the question editor, the graph inspector and the canvas palette).
    /// </remarks>
    public static IReadOnlyList<QuestionTypeChoice> Choices(FlirtyQuestionTypeRegistry? registry = null)
    {
        var choices = Enum.GetValues<QuestionType>()
            .Select(type => new QuestionTypeChoice(type.ToString(), Describe(type), type, null))
            .ToList();

        if (registry is null)
        {
            return choices;
        }

        choices.AddRange(registry.Types.Select(declared => new QuestionTypeChoice(
            CustomChoicePrefix + declared.Key,
            $"{declared.DisplayName} ({declared.Key})",
            QuestionType.Json,
            declared.Key)));

        return choices;
    }

    /// <summary>
    /// The <see cref="QuestionTypeChoice.Value"/> that represents the given question, for a
    /// <c>&lt;select&gt;</c>.
    /// </summary>
    /// <param name="type">The question's type.</param>
    /// <param name="customTypeKey">The question's <see cref="Question.CustomTypeKey"/>, if any.</param>
    /// <param name="registry">The declared custom question types, if the caller has them.</param>
    /// <returns>The value of the matching choice.</returns>
    /// <remarks>
    /// Derived from the form state rather than stored beside it, so the dropdown and the custom-type-key
    /// field cannot drift apart: typing an <b>undeclared</b> key falls back to the plain
    /// <see cref="QuestionType.Json"/> entry, which is exactly what such a question is.
    /// </remarks>
    public static string ChoiceValue(
        QuestionType type,
        string? customTypeKey = null,
        FlirtyQuestionTypeRegistry? registry = null)
        => Declared(customTypeKey, registry) is { } declared && type == QuestionType.Json
            ? CustomChoicePrefix + declared.Key
            : type.ToString();

    /// <summary>Reads back what a <c>&lt;select&gt;</c> or a palette entry produced.</summary>
    /// <param name="value">The <see cref="QuestionTypeChoice.Value"/> that was selected.</param>
    /// <param name="type">The question type to author.</param>
    /// <param name="customTypeKey">The custom type key to author, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the value was understood.</returns>
    /// <remarks>
    /// Deliberately registry-free: a value is parsed by its shape, so a choice does not stop being
    /// readable because a descriptor was removed from the file between render and postback.
    /// </remarks>
    public static bool TryResolveChoice(string? value, out QuestionType type, out string? customTypeKey)
    {
        type = default;
        customTypeKey = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith(CustomChoicePrefix, StringComparison.Ordinal))
        {
            var key = value[CustomChoicePrefix.Length..];
            if (key.Length == 0)
            {
                return false;
            }

            type = QuestionType.Json;
            customTypeKey = key;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: false, out type);
    }

    /// <summary>Looks a key up in the registry, tolerating a missing key or a missing registry.</summary>
    private static FlirtyQuestionType? Declared(string? customTypeKey, FlirtyQuestionTypeRegistry? registry)
        => !string.IsNullOrWhiteSpace(customTypeKey)
            && registry is not null
            && registry.TryGet(customTypeKey, out var declared)
                ? declared
                : null;
}
