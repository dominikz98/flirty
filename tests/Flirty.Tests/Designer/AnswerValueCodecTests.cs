using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Validation;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für den <see cref="AnswerValueCodec"/> des Test-Runners (#43). Kernprobe – wie bei
/// <see cref="QuestionFormModelTests"/> – ist der Abgleich mit der Engine: Der im Designer erzeugte rohe
/// JSON-Text muss genau das sein, was der <see cref="AnswerValidator"/> je <see cref="QuestionType"/>
/// akzeptiert. Sonst scheiterte jeder Testlauf an der eigenen Kodierung statt am Dialog.
/// </summary>
public sealed class AnswerValueCodecTests
{
    private static readonly AnswerValidator Validator = new();

    // ---- Kodierung: von der Engine akzeptiert ------------------------------------------------

    /// <summary>Freitext, Datum und Einfachauswahl reisen als JSON-Zeichenkette.</summary>
    [Theory]
    [InlineData(QuestionType.FreeText, "Hallo Welt", "\"Hallo Welt\"")]
    [InlineData(QuestionType.Date, "2026-07-22", "\"2026-07-22\"")]
    [InlineData(QuestionType.SingleChoice, "dev", "\"dev\"")]
    public void Encode_erzeugt_JSON_Zeichenketten(QuestionType type, string input, string expected)
        => Assert.Equal(expected, AnswerValueCodec.Encode(type, input));

    /// <summary>Sonderzeichen werden escaped, damit gültiges JSON entsteht.</summary>
    [Fact]
    public void Encode_escaped_Anfuehrungszeichen_im_Freitext()
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.FreeText, "Er sagte \"Hallo\"");

        Assert.True(Validator.Validate(NewQuestion(QuestionType.FreeText), encoded).IsValid);
        Assert.Equal("Er sagte \"Hallo\"", AnswerValueCodec.Describe(null, encoded));
    }

    /// <summary>Zahlen reisen als rohes JSON-Zahlliteral – invariant, auch bei deutschem Dezimalkomma.</summary>
    [Theory]
    [InlineData("42", "42")]
    [InlineData("3.5", "3.5")]
    [InlineData("3,5", "3.5")]
    [InlineData(" 7 ", "7")]
    public void Encode_erzeugt_invariante_Zahlliterale(string input, string expected)
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.Number, input);

        Assert.Equal(expected, encoded);
        Assert.True(Validator.Validate(NewQuestion(QuestionType.Number), encoded).IsValid);
    }

    /// <summary>
    /// Eine unlesbare Zahleingabe wird bewusst <b>nicht</b> stillschweigend ersetzt, sondern als
    /// Zeichenkette weitergereicht – damit die Engine sie mit ihrer eigenen Meldung ablehnt.
    /// </summary>
    [Fact]
    public void Encode_reicht_unlesbare_Zahleingabe_an_die_Engine_weiter()
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.Number, "keine Zahl");

        var result = Validator.Validate(NewQuestion(QuestionType.Number), encoded);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Zahl", StringComparison.Ordinal));
    }

    /// <summary>Wahrheitswerte reisen als <c>true</c>/<c>false</c>; alles andere gilt als <c>false</c>.</summary>
    [Theory]
    [InlineData("true", "true")]
    [InlineData("True", "true")]
    [InlineData("false", "false")]
    [InlineData("", "false")]
    public void Encode_erzeugt_Wahrheitswerte(string input, string expected)
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.Boolean, input);

        Assert.Equal(expected, encoded);
        Assert.True(Validator.Validate(NewQuestion(QuestionType.Boolean), encoded).IsValid);
    }

    /// <summary>Die Mehrfachauswahl reist als JSON-Array von Zeichenketten.</summary>
    [Fact]
    public void Encode_erzeugt_Array_fuer_die_Mehrfachauswahl()
    {
        var question = NewQuestion(QuestionType.MultiChoice, ("csharp", "C#"), ("fsharp", "F#"));

        var encoded = AnswerValueCodec.Encode(QuestionType.MultiChoice, null, ["csharp", "fsharp"]);

        Assert.Equal("[\"csharp\",\"fsharp\"]", encoded);
        Assert.True(Validator.Validate(question, encoded).IsValid);
    }

    /// <summary>Auch die leere Mehrfachauswahl ist gültiges JSON (die Engine prüft die Pflicht separat).</summary>
    [Fact]
    public void Encode_erzeugt_leeres_Array_ohne_Auswahl()
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.MultiChoice, null, []);

        Assert.Equal("[]", encoded);
        Assert.True(Validator.Validate(NewQuestion(QuestionType.MultiChoice), encoded).IsValid);
    }

    /// <summary>Die Einfachauswahl muss gegen die konfigurierten Options-Werte laufen.</summary>
    [Fact]
    public void Encode_liefert_bei_unbekannter_Option_die_Ablehnung_der_Engine()
    {
        var question = NewQuestion(QuestionType.SingleChoice, ("dev", "Entwickler"));

        Assert.True(Validator.Validate(question, AnswerValueCodec.Encode(question.Type, "dev")).IsValid);
        Assert.False(Validator.Validate(question, AnswerValueCodec.Encode(question.Type, "pm")).IsValid);
    }

    // ---- Anzeige -----------------------------------------------------------------------------

    /// <summary>Auswahlen erscheinen mit ihrer Beschriftung, nicht mit dem gespeicherten Wert.</summary>
    [Fact]
    public void Describe_zeigt_die_Beschriftung_einer_Auswahl()
    {
        var question = NewDetail(QuestionType.SingleChoice, ("dev", "Entwickler"));

        Assert.Equal("Entwickler", AnswerValueCodec.Describe(question, "\"dev\""));
    }

    /// <summary>Eine Mehrfachauswahl erscheint kommagetrennt mit Beschriftungen.</summary>
    [Fact]
    public void Describe_zeigt_die_Mehrfachauswahl_kommagetrennt()
    {
        var question = NewDetail(QuestionType.MultiChoice, ("csharp", "C#"), ("fsharp", "F#"));

        Assert.Equal("C#, F#", AnswerValueCodec.Describe(question, "[\"csharp\",\"fsharp\"]"));
    }

    /// <summary>Wahrheitswerte erscheinen deutsch.</summary>
    [Theory]
    [InlineData("true", "Ja")]
    [InlineData("false", "Nein")]
    public void Describe_zeigt_Wahrheitswerte_deutsch(string value, string expected)
        => Assert.Equal(expected, AnswerValueCodec.Describe(NewDetail(QuestionType.Boolean), value));

    /// <summary>
    /// Gehört die Frage nicht mehr zum Dialog (gelöscht), wird der Rohwert bestmöglich gelesen statt
    /// verschwiegen.
    /// </summary>
    [Fact]
    public void Describe_liest_ohne_bekannte_Frage_den_Rohwert()
        => Assert.Equal("Hallo", AnswerValueCodec.Describe(null, "\"Hallo\""));

    /// <summary>Ein unbekannter Options-Wert wird roh gezeigt, nicht unterschlagen.</summary>
    [Fact]
    public void Describe_zeigt_unbekannte_Option_roh()
    {
        var question = NewDetail(QuestionType.SingleChoice, ("dev", "Entwickler"));

        Assert.Equal("pm", AnswerValueCodec.Describe(question, "\"pm\""));
    }

    // ---- Rundreise ---------------------------------------------------------------------------

    /// <summary>
    /// Der Editier-Modus liest gespeicherte Werte zurück in die Eingabefelder – kodiert man sie erneut,
    /// muss derselbe JSON-Text herauskommen.
    /// </summary>
    [Theory]
    [InlineData(QuestionType.FreeText, "\"Hallo Welt\"")]
    [InlineData(QuestionType.Date, "\"2026-07-22\"")]
    [InlineData(QuestionType.SingleChoice, "\"dev\"")]
    [InlineData(QuestionType.Number, "42")]
    [InlineData(QuestionType.Boolean, "true")]
    [InlineData(QuestionType.Boolean, "false")]
    [InlineData(QuestionType.MultiChoice, "[\"csharp\",\"fsharp\"]")]
    public void Decode_und_Encode_sind_zueinander_invers(QuestionType type, string value)
    {
        var (text, selected) = AnswerValueCodec.Decode(type, value);

        Assert.Equal(value, AnswerValueCodec.Encode(type, text, selected));
    }

    // ---- Testdaten ---------------------------------------------------------------------------

    private static Question NewQuestion(QuestionType type, params (string Value, string Label)[] options)
    {
        var questionId = Guid.NewGuid();
        var question = new Question
        {
            Id = questionId,
            DialogId = Guid.NewGuid(),
            Key = "frage",
            Text = "Frage?",
            Type = type,
            Order = 0,
        };

        foreach (var (value, label) in options)
        {
            question.Options.Add(new AnswerOption
            {
                Id = Guid.NewGuid(),
                QuestionId = questionId,
                Key = value,
                Label = label,
                Value = value,
                Order = question.Options.Count,
            });
        }

        return question;
    }

    private static QuestionDetail NewDetail(QuestionType type, params (string Value, string Label)[] options)
    {
        var question = NewQuestion(type, options);

        return new QuestionDetail(
            question.Id, question.DialogId, question.Key, question.Text, question.Type, question.Order,
            question.IsRequired, question.ValidationRules,
            [.. question.Options.Select(option => new AnswerOptionDetail(
                option.Id, option.QuestionId, option.Key, option.Label, option.Value, option.Order))]);
    }
}
