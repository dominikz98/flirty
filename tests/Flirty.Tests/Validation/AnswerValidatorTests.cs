using Flirty.Domain;
using Flirty.Validation;

namespace Flirty.Tests.Validation;

/// <summary>
/// Verifiziert den <see cref="AnswerValidator"/> (Issue #30) isoliert: die Typprüfung je
/// <see cref="QuestionType"/>, die typ-skopierten <see cref="ValidationRules"/>
/// (Länge/Bereich/Muster), die Options-Membership der Auswahl-Typen, den toleranten Fallback für
/// rohe (Nicht-JSON-)Werte sowie die Fehlkonfigurations-Fälle.
/// </summary>
public sealed class AnswerValidatorTests
{
    private readonly AnswerValidator _validator = new();

    private static Question NewQuestion(
        QuestionType type, string? validationRules = null, params string[] optionValues)
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            DialogId = Guid.NewGuid(),
            Key = "q",
            Text = "Frage?",
            Type = type,
            ValidationRules = validationRules,
        };

        var order = 0;
        foreach (var value in optionValues)
        {
            question.Options.Add(new AnswerOption
            {
                Id = Guid.NewGuid(),
                QuestionId = question.Id,
                Key = value,
                Label = value,
                Value = value,
                Order = order++,
            });
        }

        return question;
    }

    // ---- FreeText ----------------------------------------------------------------------------

    [Fact]
    public void FreeText_akzeptiert_beliebigen_Text()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.FreeText), "\"Hallo Welt\"").IsValid);

    [Fact]
    public void FreeText_akzeptiert_rohen_NichtJson_Text_tolerant()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.FreeText), "Hallo Welt").IsValid);

    [Fact]
    public void FreeText_zu_kurz_verletzt_MinLength()
    {
        var result = _validator.Validate(NewQuestion(QuestionType.FreeText, "{\"minLength\":5}"), "\"ab\"");
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void FreeText_zu_lang_verletzt_MaxLength()
    {
        var result = _validator.Validate(NewQuestion(QuestionType.FreeText, "{\"maxLength\":3}"), "\"abcd\"");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void FreeText_innerhalb_der_Laengengrenzen_ist_gueltig()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.FreeText, "{\"minLength\":2,\"maxLength\":5}"), "\"abc\"").IsValid);

    [Fact]
    public void FreeText_passendes_Muster_ist_gueltig()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.FreeText, "{\"pattern\":\"^[a-z]+$\"}"), "\"abc\"").IsValid);

    [Fact]
    public void FreeText_nicht_passendes_Muster_ist_ungueltig()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.FreeText, "{\"pattern\":\"^[a-z]+$\"}"), "\"ABC123\"").IsValid);

    // ---- Number ------------------------------------------------------------------------------

    [Fact]
    public void Number_akzeptiert_JsonZahl()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Number), "42").IsValid);

    [Fact]
    public void Number_akzeptiert_numerischen_String()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Number), "\"3.5\"").IsValid);

    [Fact]
    public void Number_lehnt_NichtZahl_ab()
        => Assert.False(_validator.Validate(NewQuestion(QuestionType.Number), "\"keine-zahl\"").IsValid);

    [Fact]
    public void Number_unter_Minimum_ist_ungueltig()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.Number, "{\"min\":1,\"max\":10}"), "0").IsValid);

    [Fact]
    public void Number_ueber_Maximum_ist_ungueltig()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.Number, "{\"min\":1,\"max\":10}"), "20").IsValid);

    [Fact]
    public void Number_im_Bereich_ist_gueltig()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.Number, "{\"min\":1,\"max\":10}"), "5").IsValid);

    // ---- Boolean -----------------------------------------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("\"true\"")]
    public void Boolean_akzeptiert_Wahrheitswerte(string value)
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Boolean), value).IsValid);

    [Fact]
    public void Boolean_lehnt_NichtBoolean_ab()
        => Assert.False(_validator.Validate(NewQuestion(QuestionType.Boolean), "\"vielleicht\"").IsValid);

    // ---- Date --------------------------------------------------------------------------------

    [Fact]
    public void Date_akzeptiert_IsoDatum()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Date), "\"2026-07-17\"").IsValid);

    [Fact]
    public void Date_lehnt_NichtDatum_ab()
        => Assert.False(_validator.Validate(NewQuestion(QuestionType.Date), "\"kein-datum\"").IsValid);

    // ---- SingleChoice ------------------------------------------------------------------------

    [Fact]
    public void SingleChoice_akzeptiert_bekannte_Option()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.SingleChoice, null, "dev", "pm"), "\"dev\"").IsValid);

    [Fact]
    public void SingleChoice_akzeptiert_bekannte_Option_als_rohen_String()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.SingleChoice, null, "dev", "pm"), "dev").IsValid);

    [Fact]
    public void SingleChoice_lehnt_unbekannte_Option_ab()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.SingleChoice, null, "dev", "pm"), "\"lead\"").IsValid);

    // ---- MultiChoice -------------------------------------------------------------------------

    [Fact]
    public void MultiChoice_akzeptiert_bekannte_Teilmenge()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.MultiChoice, null, "a", "b", "c"), "[\"a\",\"c\"]").IsValid);

    [Fact]
    public void MultiChoice_lehnt_unbekanntes_Element_ab()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.MultiChoice, null, "a", "b"), "[\"a\",\"x\"]").IsValid);

    [Fact]
    public void MultiChoice_lehnt_NichtArray_ab()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.MultiChoice, null, "a", "b"), "\"a\"").IsValid);

    // ---- Fehlkonfiguration / Argumente -------------------------------------------------------

    [Fact]
    public void Kaputtes_ValidationRules_Json_wirft_InvalidOperationException()
        => Assert.Throws<InvalidOperationException>(
            () => _validator.Validate(NewQuestion(QuestionType.FreeText, "{ kein json"), "\"x\""));

    [Fact]
    public void Ungueltiges_Regex_Muster_wirft_InvalidOperationException()
        => Assert.Throws<InvalidOperationException>(
            () => _validator.Validate(NewQuestion(QuestionType.FreeText, "{\"pattern\":\"[\"}"), "\"x\""));

    [Fact]
    public void Validate_wirft_bei_null_Frage()
        => Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, "\"x\""));

    [Fact]
    public void Validate_wirft_bei_null_Wert()
        => Assert.Throws<ArgumentNullException>(
            () => _validator.Validate(NewQuestion(QuestionType.FreeText), null!));
}
