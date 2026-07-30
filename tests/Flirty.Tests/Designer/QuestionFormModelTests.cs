using System.Text.Json;
using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Validation;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für das <see cref="QuestionFormModel"/> (#39): die Abbildung zwischen den Eingabefeldern des
/// Frage-Editors und dem als JSON gespeicherten <see cref="Question.ValidationRules"/>. Kern ist, dass
/// das erzeugte JSON exakt das ist, was der <see cref="AnswerValidator"/> der Engine liest – und dass
/// unbekannte Regeln nicht stillschweigend verloren gehen.
/// </summary>
public sealed class QuestionFormModelTests
{
    [Fact]
    public void From_liest_bekannte_Regeln_in_die_strukturierten_Felder()
    {
        var model = QuestionFormModel.From(
            Frage(QuestionType.FreeText, """{"minLength":2,"maxLength":50,"pattern":"^[a-z]+$"}"""));

        Assert.False(model.UseRawJson);
        Assert.Equal(2, model.MinLength);
        Assert.Equal(50, model.MaxLength);
        Assert.Equal("^[a-z]+$", model.Pattern);
        Assert.Null(model.Min);
        Assert.Null(model.Max);
    }

    [Fact]
    public void From_liest_Regeln_case_insensitiv_wie_der_AnswerValidator()
    {
        var model = QuestionFormModel.From(Frage(QuestionType.Number, """{"Min":1,"MAX":9}"""));

        Assert.False(model.UseRawJson);
        Assert.Equal(1m, model.Min);
        Assert.Equal(9m, model.Max);
    }

    [Fact]
    public void From_ohne_Regeln_laesst_alle_Felder_leer()
    {
        var model = QuestionFormModel.From(Frage(QuestionType.FreeText, validationRules: null));

        Assert.False(model.UseRawJson);
        Assert.Null(model.RawJson);
        Assert.Null(model.MinLength);
        Assert.Null(model.Pattern);
    }

    [Fact]
    public void From_faellt_bei_unbekannten_Feldern_auf_Roh_JSON_zurueck()
    {
        const string rules = """{"minLength":2,"eigeneRegel":true}""";

        var model = QuestionFormModel.From(Frage(QuestionType.FreeText, rules));

        Assert.True(model.UseRawJson);
        Assert.Equal(rules, model.RawJson);
        // Die strukturierten Felder bleiben leer: sonst würde ein Speichern "eigeneRegel" verwerfen.
        Assert.Null(model.MinLength);
    }

    [Fact]
    public void From_faellt_bei_ungueltigem_JSON_auf_Roh_JSON_zurueck()
    {
        const string rules = "{ kein JSON";

        var model = QuestionFormModel.From(Frage(QuestionType.FreeText, rules));

        Assert.True(model.UseRawJson);
        Assert.Equal(rules, model.RawJson);
    }

    [Fact]
    public void TryBuildValidationRules_liefert_null_ohne_gesetzte_Regel()
    {
        var model = new QuestionFormModel { Key = "vorname", Text = "Name?", Type = QuestionType.FreeText };

        Assert.True(model.TryBuildValidationRules(out var json, out var error));
        Assert.Null(json);
        Assert.Null(error);
    }

    [Fact]
    public void TryBuildValidationRules_serialisiert_camelCase_ohne_Nullwerte()
    {
        var model = new QuestionFormModel
        {
            Key = "vorname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            MaxLength = 50,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Equal("""{"maxLength":50}""", json);
    }

    [Fact]
    public void TryBuildValidationRules_uebernimmt_nur_typrelevante_Regeln()
    {
        // Der Typ wurde von FreeText auf Number umgestellt: Längen und Muster wertet die Engine dann
        // nicht mehr aus und dürfen nicht als wirkungsloser Ballast im JSON stehen bleiben.
        var model = new QuestionFormModel
        {
            Key = "alter",
            Text = "Wie alt?",
            Type = QuestionType.Number,
            MinLength = 2,
            MaxLength = 50,
            Pattern = "^[a-z]+$",
            Min = 18m,
            Max = 99m,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Equal("""{"min":18,"max":99}""", json);
    }

    [Fact]
    public void TryBuildValidationRules_ignoriert_Regeln_bei_typen_ohne_Regelunterstuetzung()
    {
        var model = new QuestionFormModel
        {
            Key = "farbe",
            Text = "Welche Farbe?",
            Type = QuestionType.SingleChoice,
            MaxLength = 50,
            Max = 99m,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Null(json);
    }

    [Fact]
    public void TryBuildValidationRules_meldet_ungueltiges_Muster()
    {
        var model = new QuestionFormModel
        {
            Key = "vorname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            Pattern = "[",
        };

        Assert.False(model.TryBuildValidationRules(out var json, out var error));
        Assert.Null(json);
        Assert.Contains("regular expression", error);
    }

    [Fact]
    public void TryBuildValidationRules_meldet_vertauschte_Laengen()
    {
        var model = new QuestionFormModel
        {
            Key = "vorname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            MinLength = 10,
            MaxLength = 5,
        };

        Assert.False(model.TryBuildValidationRules(out _, out var error));
        Assert.Contains("minimum length", error);
    }

    [Fact]
    public void TryBuildValidationRules_meldet_vertauschte_Grenzen()
    {
        var model = new QuestionFormModel
        {
            Key = "alter",
            Text = "Wie alt?",
            Type = QuestionType.Number,
            Min = 99m,
            Max = 18m,
        };

        Assert.False(model.TryBuildValidationRules(out _, out var error));
        Assert.Contains("minimum", error);
    }

    [Fact]
    public void TryBuildValidationRules_reicht_unbekannte_Felder_des_Roh_JSON_unveraendert_durch()
    {
        const string rules = """{"minLength":2,"eigeneRegel":true}""";
        var model = QuestionFormModel.From(Frage(QuestionType.FreeText, rules));

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Equal(rules, json);
    }

    [Fact]
    public void TryBuildValidationRules_meldet_unlesbares_Roh_JSON()
    {
        var model = QuestionFormModel.From(Frage(QuestionType.FreeText, "{ kein JSON"));

        Assert.False(model.TryBuildValidationRules(out _, out var error));
        Assert.Contains("valid JSON", error);
    }

    [Fact]
    public void TryBuildValidationRules_entfernt_die_Regeln_bei_geleertem_Roh_JSON()
    {
        var model = QuestionFormModel.From(Frage(QuestionType.FreeText, """{"eigeneRegel":true}"""));
        model.RawJson = "   ";

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Null(json);
    }

    /// <summary>
    /// Kernprobe: Das im Designer erzeugte JSON muss der <see cref="AnswerValidator"/> der Engine
    /// tatsächlich anwenden. Bindet Serialisierung (Designer) und Deserialisierung (Core) aneinander –
    /// ein Auseinanderlaufen der Feldnamen fiele sonst erst zur Laufzeit auf.
    /// </summary>
    [Fact]
    public void TryBuildValidationRules_erzeugt_JSON_das_der_AnswerValidator_anwendet()
    {
        var model = new QuestionFormModel
        {
            Key = "vorname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            MinLength = 2,
            MaxLength = 4,
            Pattern = "^[a-z]+$",
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));

        var question = new Question { Key = "vorname", Text = "Name?", Type = QuestionType.FreeText, ValidationRules = json };
        var validator = new AnswerValidator();

        Assert.True(validator.Validate(question, "\"abc\"").IsValid);
        Assert.False(validator.Validate(question, "\"a\"").IsValid);        // zu kurz
        Assert.False(validator.Validate(question, "\"abcde\"").IsValid);    // zu lang
        Assert.False(validator.Validate(question, "\"ABC\"").IsValid);      // Muster verletzt
    }

    /// <summary>
    /// Gegenprobe zur Kernprobe für den numerischen Zweig.
    /// </summary>
    [Fact]
    public void TryBuildValidationRules_erzeugt_Zahlgrenzen_die_der_AnswerValidator_anwendet()
    {
        var model = new QuestionFormModel
        {
            Key = "alter",
            Text = "Wie alt?",
            Type = QuestionType.Number,
            Min = 18m,
            Max = 99m,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));

        var question = new Question { Key = "alter", Text = "Wie alt?", Type = QuestionType.Number, ValidationRules = json };
        var validator = new AnswerValidator();

        Assert.True(validator.Validate(question, "42").IsValid);
        Assert.False(validator.Validate(question, "17").IsValid);
        Assert.False(validator.Validate(question, "100").IsValid);
    }

    /// <summary>
    /// Roundtrip: gespeicherte Regeln einlesen, unverändert wieder erzeugen und dabei bei den vom
    /// Core-Typ <see cref="ValidationRules"/> vorgegebenen Feldnamen bleiben.
    /// </summary>
    [Fact]
    public void From_und_TryBuildValidationRules_sind_verlustfrei()
    {
        const string rules = """{"minLength":2,"maxLength":50,"pattern":"^[a-z]+$"}""";
        var model = QuestionFormModel.From(Frage(QuestionType.FreeText, rules));

        Assert.True(model.TryBuildValidationRules(out var json, out _));

        var original = JsonSerializer.Deserialize<ValidationRules>(rules);
        var wiederhergestellt = JsonSerializer.Deserialize<ValidationRules>(json!);

        Assert.Equal(original, wiederhergestellt);
    }

    // ---- Schlüssel-Vorschlag für Gesten auf dem Canvas (#103) ---------------------------------------

    [Theory]
    [InlineData(QuestionType.FreeText, "text")]
    [InlineData(QuestionType.Number, "number")]
    [InlineData(QuestionType.Date, "date")]
    [InlineData(QuestionType.Boolean, "yesno")]
    [InlineData(QuestionType.SingleChoice, "choice")]
    [InlineData(QuestionType.MultiChoice, "multi")]
    public void SuggestKey_nutzt_einen_Stamm_je_Fragetyp(QuestionType type, string erwartet)
        => Assert.Equal(erwartet, QuestionFormModel.SuggestKey(type, Dialog()));

    [Fact]
    public void SuggestKey_haengt_bei_Belegung_eine_Zahl_an()
    {
        var detail = Dialog(Frage(QuestionType.FreeText, null) with { Key = "text" });

        Assert.Equal("text2", QuestionFormModel.SuggestKey(QuestionType.FreeText, detail));
    }

    [Fact]
    public void SuggestKey_weicht_auch_einem_Collection_Schluessel_aus()
    {
        // Ein Frage-Schlüssel, der einen CollectionKey doppelt, wird im Ausdruckskontext von ihm
        // verdeckt – die Geste erzeugte sonst auf der Stelle eine Warnung.
        var detail = Dialog() with
        {
            Loops = [new LoopDetail(Guid.NewGuid(), Guid.NewGuid(), "text", Guid.NewGuid(), Guid.NewGuid())],
        };

        Assert.Equal("text2", QuestionFormModel.SuggestKey(QuestionType.FreeText, detail));
    }

    /// <summary>
    /// Anders als <c>LoopFormModel.SuggestCollectionKey</c> darf hier <b>nie</b> leer herauskommen: Der
    /// Vorschlag trägt eine Geste, die sofort schreibt – ein leerer Schlüssel ließe sie am
    /// <c>CreateQuestionCommand</c> scheitern.
    /// </summary>
    [Fact]
    public void SuggestKey_liefert_auch_bei_vielen_Kollisionen_einen_freien_Schluessel()
    {
        var belegt = Enumerable.Range(1, 5)
            .Select(index => Frage(QuestionType.Number, null) with { Key = index == 1 ? "number" : $"number{index}" })
            .ToArray();

        var vorschlag = QuestionFormModel.SuggestKey(QuestionType.Number, Dialog(belegt));

        Assert.Equal("number6", vorschlag);
        Assert.DoesNotContain(vorschlag, belegt.Select(frage => frage.Key));
    }

    /// <summary>
    /// Der Vorschlag muss als Ausdrucks-Variable bindbar sein – Frage-Schlüssel werden im
    /// Musterkontext des Branching-Editors gebunden.
    /// </summary>
    [Theory]
    [InlineData(QuestionType.FreeText)]
    [InlineData(QuestionType.Boolean)]
    [InlineData(QuestionType.MultiChoice)]
    public void SuggestKey_ist_immer_ein_bindbarer_Bezeichner(QuestionType type)
    {
        var vorschlag = QuestionFormModel.SuggestKey(type, Dialog());

        Assert.NotEqual(string.Empty, vorschlag);
        Assert.True(DesignerExpressionContext.IsBindable(vorschlag));
    }

    /// <summary>Baut eine Frage-Sicht, wie sie der <c>GetDialogQuery</c> liefert.</summary>
    /// <param name="type">Der Fragetyp.</param>
    /// <param name="validationRules">Die gespeicherten Regeln als JSON.</param>
    /// <returns>Die Frage-Sicht.</returns>
    private static QuestionDetail Frage(QuestionType type, string? validationRules)
        => new(Guid.NewGuid(), Guid.NewGuid(), "vorname", "Wie heißt du?", type, 0, true, validationRules, []);

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
