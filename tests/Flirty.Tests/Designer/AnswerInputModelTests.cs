using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="AnswerInputModel"/> – the designer's hard guard on what the test runner may send,
/// plus the sample prefill of the raw-JSON field (#137). Both submit paths and both edit paths of the
/// runner ask <see cref="AnswerInputModel.CanSubmit"/>, so a type that is false here has no reachable way
/// out of the designer even if the markup were bypassed.
/// </summary>
public sealed class AnswerInputModelTests
{
    private static QuestionView Question(QuestionType type, string? customTypeKey = null)
        => new(Guid.NewGuid(), "colour", "Which colour?", type, customTypeKey, []);

    /// <summary>
    /// Since #137 a JSON answer is submittable – the runner has a raw-JSON field. It needs text like any
    /// other free input, and nothing more.
    /// </summary>
    [Fact]
    public void A_json_answer_needs_text()
    {
        Assert.True(AnswerInputModel.From(QuestionType.Json, "{\"city\":\"Berlin\"}").CanSubmit);
        Assert.False(AnswerInputModel.From(QuestionType.Json, "   ").CanSubmit);
    }

    /// <summary>
    /// <b>Deliberately not gated on well-formedness.</b> A malformed value has to reach the engine, so
    /// the author sees the <c>AnswerValidator</c>'s own refusal – the very message a host application
    /// would produce. Gating here would make the designer the author of that message and hide the engine
    /// behind a greyed-out button.
    /// </summary>
    [Fact]
    public void A_malformed_json_answer_still_reaches_the_engine()
        => Assert.True(AnswerInputModel.From(QuestionType.Json, "{oops").CanSubmit);

    [Fact]
    public void A_free_text_answer_needs_text()
    {
        Assert.False(AnswerInputModel.From(QuestionType.FreeText, "\"  \"").CanSubmit);
        Assert.True(AnswerInputModel.From(QuestionType.FreeText, "\"hello\"").CanSubmit);
    }

    [Fact]
    public void A_multi_choice_answer_needs_a_selection()
    {
        Assert.False(AnswerInputModel.From(QuestionType.MultiChoice, "[]").CanSubmit);
        Assert.True(AnswerInputModel.From(QuestionType.MultiChoice, "[\"a\"]").CanSubmit);
    }

    /// <summary>
    /// The sample of a host-declared type prefills the field – the practical difference between
    /// "answerable" and "usable" for a composite type, where an author would otherwise have to know the
    /// shape by heart.
    /// </summary>
    [Fact]
    public void For_prefills_a_json_question_with_the_sample()
        => Assert.Equal(
            "\"#ff0000\"",
            AnswerInputModel.For(Question(QuestionType.Json, "color"), "\"#ff0000\"").Text);

    /// <summary>
    /// Without a sample the field stays empty rather than showing an invented value – the same reason
    /// EPIC 14 gave for offering no control at all: a test run writes a real session.
    /// </summary>
    [Fact]
    public void For_leaves_the_field_empty_without_a_sample()
    {
        Assert.Equal(string.Empty, AnswerInputModel.For(Question(QuestionType.Json)).Text);
        Assert.Equal(string.Empty, AnswerInputModel.For(Question(QuestionType.Json), "  ").Text);
    }

    /// <summary>
    /// A sample belongs to <c>Json</c> alone. Every other control derives its display from
    /// <see cref="AnswerInputModel.Text"/>, so a stray prefill would look like an answer the author gave.
    /// </summary>
    [Fact]
    public void For_ignores_a_sample_on_another_type()
        => Assert.Equal(
            string.Empty, AnswerInputModel.For(Question(QuestionType.FreeText), "\"#ff0000\"").Text);
}
