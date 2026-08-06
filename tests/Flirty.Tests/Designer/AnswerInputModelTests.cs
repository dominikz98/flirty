using Flirty.Designer.Models;
using Flirty.Domain;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="AnswerInputModel.CanSubmit"/> – the designer's hard guard on what the test runner
/// may send. Both submit paths and both edit paths of the runner ask it, so a type that is false here has
/// no reachable way out of the designer even if the markup were bypassed.
/// </summary>
public sealed class AnswerInputModelTests
{
    /// <summary>
    /// Well-formed, non-empty, and still refused: the designer does not know what shape the host's custom
    /// type expects, and a test run writes a <b>real</b> session and delivers real webhooks.
    /// </summary>
    [Fact]
    public void A_json_answer_can_never_be_submitted_from_the_designer()
        => Assert.False(
            AnswerInputModel.From(QuestionType.Json, "{\"city\":\"Berlin\"}").CanSubmit);

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
}
