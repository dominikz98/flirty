using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Validation;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="QuestionTypeLabels"/> – the designer's single source of what a question type is
/// called, whether it evaluates answer options, and which types a question can be authored as.
/// </summary>
/// <remarks>
/// Since #137 every member takes an optional <see cref="FlirtyQuestionTypeRegistry"/>. Each behaviour is
/// therefore pinned <b>twice</b>: once with a registry (the new capability) and once without (the #136
/// behaviour, which must not regress). The second half is the interesting one – it is the acceptance
/// criterion "with no descriptors available at all, the designer behaves exactly as before".
/// </remarks>
public sealed class QuestionTypeLabelsTests
{
    /// <summary>A registry declaring one type, as <c>o.AddQuestionType</c> would build it.</summary>
    private static FlirtyQuestionTypeRegistry Registry(string key = "color", string name = "Colour picker")
        => new(new Dictionary<string, FlirtyQuestionType>(StringComparer.Ordinal)
        {
            [key] = new(key, name, ValidatorType: null, "\"#ff0000\""),
        });

    /// <summary>
    /// <b>The cheap structural guard for the premise of EPIC 14.</b> <c>Describe</c> ends in a
    /// <c>_ =&gt; type.ToString()</c> fallback, so a new enum member does not break anything – it simply
    /// renders as its bare technical name, in the question list, the type dropdowns, the graph node card
    /// and the screen-reader description, and nothing fails. That is precisely the kind of silent cost
    /// the issue measured across fifteen files. This test makes the next one loud.
    /// </summary>
    [Fact]
    public void Every_question_type_has_a_label_of_its_own()
    {
        foreach (var type in Enum.GetValues<QuestionType>())
        {
            Assert.NotEqual(type.ToString(), QuestionTypeLabels.Describe(type));
        }
    }

    [Fact]
    public void Describe_names_the_custom_type_when_the_question_carries_one()
        => Assert.Equal(
            "Custom type \"color\" (Json)", QuestionTypeLabels.Describe(QuestionType.Json, "color"));

    /// <summary>
    /// With a descriptor the type is named the way the host named it – the point of #137's part 2. The
    /// key stays in the parentheses where the technical name of a built-in type stands, so the line still
    /// tells an author what to type into <c>CustomTypeKey</c>.
    /// </summary>
    [Fact]
    public void Describe_names_a_declared_type_by_its_display_name()
        => Assert.Equal(
            "Colour picker (color)",
            QuestionTypeLabels.Describe(QuestionType.Json, "color", Registry()));

    /// <summary>
    /// <b>The EPIC 14 invariant.</b> A key the designer has no descriptor for still renders as the key –
    /// not as an error, not as a blank, and identically to how it rendered before there were descriptors
    /// at all.
    /// </summary>
    [Fact]
    public void Describe_falls_back_to_the_raw_key_for_an_undeclared_type()
        => Assert.Equal(
            QuestionTypeLabels.Describe(QuestionType.Json, "postcode"),
            QuestionTypeLabels.Describe(QuestionType.Json, "postcode", Registry()));

    [Fact]
    public void Describe_falls_back_to_the_plain_json_label_without_a_key()
    {
        Assert.Equal("JSON or custom type (Json)", QuestionTypeLabels.Describe(QuestionType.Json));
        Assert.Equal("JSON or custom type (Json)", QuestionTypeLabels.Describe(QuestionType.Json, "  "));
        Assert.Equal(
            "JSON or custom type (Json)",
            QuestionTypeLabels.Describe(QuestionType.Json, null, Registry()));
    }

    /// <summary>The key is only meaningful with <see cref="QuestionType.Json"/> and ignored elsewhere.</summary>
    [Fact]
    public void Describe_ignores_a_custom_type_key_on_another_type()
    {
        Assert.Equal(
            QuestionTypeLabels.Describe(QuestionType.FreeText),
            QuestionTypeLabels.Describe(QuestionType.FreeText, "color"));

        Assert.Equal(
            QuestionTypeLabels.Describe(QuestionType.FreeText),
            QuestionTypeLabels.Describe(QuestionType.FreeText, "color", Registry()));
    }

    /// <summary>
    /// A statement about the <b>engine</b>, which does not evaluate options for a JSON question. It says
    /// nothing about a host's validator, which receives them – hence the deliberately separate wording in
    /// the question editor's warning.
    /// </summary>
    [Theory]
    [InlineData(QuestionType.SingleChoice, true)]
    [InlineData(QuestionType.MultiChoice, true)]
    [InlineData(QuestionType.Json, false)]
    [InlineData(QuestionType.FreeText, false)]
    public void UsesOptions_reports_the_choice_types(QuestionType type, bool expected)
        => Assert.Equal(expected, QuestionTypeLabels.UsesOptions(type));

    /// <summary>
    /// <b>The other half of the EPIC 14 invariant</b>, and the one an implementation is likelier to break:
    /// without descriptors the authoring surfaces offer exactly the enum, in enum order. Compared against
    /// <c>Enum.GetValues</c> rather than a literal list, so a future <see cref="QuestionType"/> is covered
    /// too.
    /// </summary>
    [Fact]
    public void Choices_are_the_built_in_types_without_a_registry()
    {
        Assert.Equal(
            Enum.GetValues<QuestionType>().Select(type => type.ToString()),
            QuestionTypeLabels.Choices().Select(choice => choice.Value));

        Assert.All(QuestionTypeLabels.Choices(), choice => Assert.Null(choice.CustomTypeKey));
    }

    /// <summary>
    /// A declared type is offered as its own entry, <b>after</b> the built-ins, and authors as
    /// <c>Json</c> plus the key. That is what makes it pickable from the palette and both dropdowns
    /// without ever widening the enum (ADR 0011).
    /// </summary>
    [Fact]
    public void Choices_append_one_entry_per_declared_type()
    {
        var choices = QuestionTypeLabels.Choices(Registry());

        Assert.Equal(Enum.GetValues<QuestionType>().Length + 1, choices.Count);

        var custom = choices[^1];
        Assert.Equal("Colour picker (color)", custom.Label);
        Assert.Equal(QuestionType.Json, custom.Type);
        Assert.Equal("color", custom.CustomTypeKey);
    }

    /// <summary>
    /// The round trip the dropdowns rely on: whatever <c>Choices</c> offers, <c>TryResolveChoice</c>
    /// reads back into the same pair. Without it a pick could silently author a different type.
    /// </summary>
    [Fact]
    public void Every_choice_resolves_back_to_itself()
    {
        foreach (var choice in QuestionTypeLabels.Choices(Registry()))
        {
            Assert.True(QuestionTypeLabels.TryResolveChoice(choice.Value, out var type, out var key));
            Assert.Equal(choice.Type, type);
            Assert.Equal(choice.CustomTypeKey, key);
        }
    }

    /// <summary>
    /// The selection is derived from the form state, so an <b>undeclared</b> key lands on the plain
    /// <c>Json</c> entry instead of on nothing. Without that the dropdown would show an arbitrary option
    /// while the key field held something else – the two would drift apart on screen.
    /// </summary>
    [Fact]
    public void ChoiceValue_derives_the_selection_from_type_and_key()
    {
        Assert.Equal("FreeText", QuestionTypeLabels.ChoiceValue(QuestionType.FreeText));
        Assert.Equal("Json", QuestionTypeLabels.ChoiceValue(QuestionType.Json, "postcode", Registry()));
        Assert.Equal("Json", QuestionTypeLabels.ChoiceValue(QuestionType.Json, "color"));
        Assert.Equal(
            "custom:color", QuestionTypeLabels.ChoiceValue(QuestionType.Json, "color", Registry()));
    }

    /// <summary>Junk from a postback is discarded, never guessed into a type.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("custom:")]
    [InlineData("NotAType")]
    [InlineData("freetext")]
    public void TryResolveChoice_refuses_an_unreadable_value(string? value)
        => Assert.False(QuestionTypeLabels.TryResolveChoice(value, out _, out _));

    /// <summary>
    /// Deliberately registry-free: a value is read by its shape, so a pick still resolves if the
    /// descriptor file changed between render and postback. The authoring guard the core owns
    /// (<c>CustomTypeKey</c> ⇒ <c>Json</c>) holds either way.
    /// </summary>
    [Fact]
    public void TryResolveChoice_reads_an_undeclared_custom_value()
    {
        Assert.True(QuestionTypeLabels.TryResolveChoice("custom:postcode", out var type, out var key));
        Assert.Equal(QuestionType.Json, type);
        Assert.Equal("postcode", key);
    }
}
