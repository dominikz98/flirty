using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for <see cref="LoopFormModel.SuggestCollectionKey"/> (#116). The suggestion appends the English
/// suffix <c>_list</c> to the entry question's key and returns an empty string when that candidate is not
/// a referenceable identifier or already taken. It is the deliberate counterpart to
/// <see cref="QuestionFormModel.SuggestKey"/>, which must never return empty (covered in
/// <see cref="QuestionFormModelTests"/>).
/// </summary>
public sealed class LoopFormModelTests
{
    [Fact]
    public void SuggestCollectionKey_appends_the_list_suffix_to_the_entry_key()
        => Assert.Equal("topping_list", LoopFormModel.SuggestCollectionKey("topping", Dialog()));

    [Fact]
    public void SuggestCollectionKey_uses_the_english_suffix_not_a_plural_s()
    {
        // The English rule is a plain suffix, not an s-pluralization: "skill" -> "skill_list", never
        // "skills". A wrong plural would collide with a natural collection name and read oddly.
        var suggestion = LoopFormModel.SuggestCollectionKey("skill", Dialog());

        Assert.Equal("skill_list", suggestion);
        Assert.DoesNotContain("skills", suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public void SuggestCollectionKey_returns_empty_when_a_question_already_uses_the_candidate()
    {
        var detail = Dialog(Question("topping_list"));

        Assert.Equal(string.Empty, LoopFormModel.SuggestCollectionKey("topping", detail));
    }

    [Fact]
    public void SuggestCollectionKey_returns_empty_when_a_loop_already_uses_the_candidate()
    {
        var detail = Dialog() with { Loops = [Loop("topping_list")] };

        Assert.Equal(string.Empty, LoopFormModel.SuggestCollectionKey("topping", detail));
    }

    [Fact]
    public void SuggestCollectionKey_returns_empty_when_the_candidate_is_not_a_bindable_identifier()
    {
        // A hyphenated entry key yields "first-name_list", which is not a valid expression identifier –
        // suggesting it would immediately produce a "not referenceable" warning, so nothing is suggested.
        Assert.Equal(string.Empty, LoopFormModel.SuggestCollectionKey("first-name", Dialog()));
    }

    private static QuestionDetail Question(string key)
        => new(Guid.NewGuid(), Guid.NewGuid(), key, "Question", QuestionType.FreeText, 0, true, null, []);

    private static LoopDetail Loop(string collectionKey)
        => new(Guid.NewGuid(), Guid.NewGuid(), collectionKey, Guid.NewGuid(), Guid.NewGuid());

    private static DialogDetail Dialog(params QuestionDetail[] questions)
        => new(
            new DialogSummary(
                Guid.NewGuid(), "dialog", "Dialog", null, 1, false, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            questions,
            [],
            [],
            [],
            []);
}
