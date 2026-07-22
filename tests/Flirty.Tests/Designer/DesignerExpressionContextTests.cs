using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für den Musterkontext des Branching-Editors (#40). Kern ist, dass die <b>echte</b> Engine
/// (<see cref="DynamicExpressoExpressionEvaluator"/>) gegen diesen Kontext genau die Ausdrücke annimmt,
/// die auch zur Laufzeit funktionieren – und Tippfehler ablehnt, statt sie bis in eine laufende Session
/// durchzureichen.
/// </summary>
public sealed class DesignerExpressionContextTests
{
    private static readonly IExpressionEvaluator Evaluator = new DynamicExpressoExpressionEvaluator();

    [Theory]
    [InlineData("alter > 18")]                          // Zahl
    [InlineData("alter >= 18.5")]                       // Zahl gegen Dezimalliteral
    [InlineData("role == \"dev\"")]                     // Einfachauswahl (Zeichenkette)
    [InlineData("zustimmung")]                          // Wahrheitswert direkt
    [InlineData("zustimmung == true")]                  // Wahrheitswert im Vergleich
    [InlineData("bemerkung.Length > 3")]                // Freitext als Zeichenkette
    [InlineData("sprachen.Count > 0")]                  // Mehrfachauswahl als Liste
    [InlineData("skills.Count > 0")]                    // Loop-Collection
    [InlineData("geburtstag == \"2026-01-01\"")]        // Datum liegt als Zeichenkette vor
    [InlineData("now.Year >= 2026")]                    // reservierte Kontext-Variable
    [InlineData("iterationIndex == 0")]                 // Iterationsindex (int?)
    [InlineData("session.ExternalUserKey == \"kunde\"")] // Session
    [InlineData("role == \"dev\" && alter > 18")]       // Verknüpfung
    public void Validate_gueltiger_Ausdruck_gegen_den_Musterkontext(string expression)
    {
        var result = DesignerExpressionContext.Validate(Evaluator, expression, DesignerExpressionContext.Build(Dialog()));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Validate_meldet_Tippfehler_im_Frage_Schluessel_mit_Position()
    {
        var result = DesignerExpressionContext.Validate(
            Evaluator, "rolle == \"dev\"", DesignerExpressionContext.Build(Dialog()));

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.ErrorPosition);
    }

    [Fact]
    public void Validate_leerer_Ausdruck_ist_gueltig()
    {
        var result = DesignerExpressionContext.Validate(Evaluator, null, DesignerExpressionContext.Build(Dialog()));

        Assert.True(result.IsValid);
    }

    /// <summary>
    /// Die Laufzeit bindet Datumsantworten als Zeichenkette (der Wert ist roher JSON-Text). Der
    /// Musterkontext muss dasselbe tun – sonst würde der Designer einen Vergleich durchwinken, der in
    /// einer laufenden Session scheitert.
    /// </summary>
    [Fact]
    public void Validate_lehnt_Datumsvergleich_mit_now_ab_wie_die_Laufzeit()
    {
        var result = DesignerExpressionContext.Validate(
            Evaluator, "geburtstag < now", DesignerExpressionContext.Build(Dialog()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Build_bindet_Loop_Collections_auch_ohne_Iteration()
    {
        var context = DesignerExpressionContext.Build(Dialog());

        // Wie der LoopResolver zur Laufzeit: der Schlüssel ist stets gebunden, vor der ersten Iteration leer.
        Assert.True(context.Collections.ContainsKey("skills"));
        Assert.Empty(context.Collections["skills"]);
    }

    [Fact]
    public void Describe_liefert_Frage_Collection_und_Kontext_Bezeichner()
    {
        var variables = DesignerExpressionContext.Describe(Dialog());

        Assert.Equal(ExpressionValueKind.Number, variables.Single(variable => variable.Name == "alter").Kind);
        Assert.Equal(ExpressionValueKind.List, variables.Single(variable => variable.Name == "skills").Kind);
        Assert.Contains(variables, variable => variable.Name == "now");
        Assert.Contains(variables, variable => variable.Name == "session");
        Assert.All(
            variables.Where(variable => variable.IsUsable),
            variable => Assert.True(
                DesignerExpressionContext.Validate(
                    Evaluator, variable.Example, DesignerExpressionContext.Build(Dialog())).IsValid,
                $"Das Beispiel „{variable.Example}“ ist nicht gültig."));
    }

    /// <summary>
    /// Ein Frage-Schlüssel wie <c>now</c> wird zur Laufzeit von der reservierten Kontext-Variable
    /// verdeckt (der Evaluator setzt sie zuletzt) – die Referenztabelle muss das sagen, statt ihn als
    /// nutzbar anzubieten.
    /// </summary>
    [Fact]
    public void Describe_markiert_von_reservierten_Namen_verdeckte_Schluessel()
    {
        var detail = Dialog(Question("now", QuestionType.FreeText));

        var variable = DesignerExpressionContext.Describe(detail).First(entry => entry.Name == "now");
        var context = DesignerExpressionContext.Build(detail);

        Assert.False(variable.IsUsable);
        Assert.Contains("verdeckt", variable.Note);
        Assert.DoesNotContain("now", context.Answers.Keys);
    }

    [Fact]
    public void Describe_markiert_Schluessel_die_keine_Bezeichner_sind()
    {
        var detail = Dialog(Question("vor-name", QuestionType.FreeText));

        var variable = DesignerExpressionContext.Describe(detail).First(entry => entry.Name == "vor-name");

        Assert.False(variable.IsUsable);
        Assert.Contains("Bezeichner", variable.Note);
        Assert.DoesNotContain("vor-name", DesignerExpressionContext.Build(detail).Answers.Keys);
    }

    // Die Wertart kommt als Name statt als Enum: ExpressionValueKind ist internal, taugt also nicht als
    // Parametertyp einer public Testmethode (CS0051).
    [Theory]
    [InlineData("role", "Text", "==", "dev", "role == \"dev\"")]
    [InlineData("role", "Text", "==", "\"quoted\"", "role == \"\\\"quoted\\\"\"")]
    [InlineData("alter", "Number", ">", "18", "alter > 18")]
    [InlineData("alter", "Number", ">", "", "alter > 0")]
    [InlineData("zustimmung", "Boolean", "==", "false", "zustimmung == false")]
    [InlineData("skills", "List", "Anzahl >", "0", "skills.Count > 0")]
    [InlineData("skills", "List", "enthält", "csharp", "skills.Contains(\"csharp\")")]
    public void BuildCondition_setzt_den_Baustein_typgerecht_zusammen(
        string name, string kind, string operatorToken, string value, string expected)
    {
        var variable = new ExpressionVariable(
            name, Enum.Parse<ExpressionValueKind>(kind), kind, name, true, null);

        Assert.Equal(expected, DesignerExpressionContext.BuildCondition(variable, operatorToken, value));
    }

    /// <summary>Auch ein aus Nutzereingaben zusammengesetzter Baustein muss kompilierbar sein.</summary>
    [Fact]
    public void BuildCondition_erzeugt_einen_kompilierbaren_Ausdruck_auch_mit_Anfuehrungszeichen()
    {
        var variable = DesignerExpressionContext.Describe(Dialog()).First(entry => entry.Name == "role");
        var condition = DesignerExpressionContext.BuildCondition(variable, "==", "de\"v");

        var result = DesignerExpressionContext.Validate(Evaluator, condition, DesignerExpressionContext.Build(Dialog()));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Append_verknuepft_nur_einen_vorhandenen_Ausdruck()
    {
        Assert.Equal("alter > 18", DesignerExpressionContext.Append(null, "alter > 18", "&&"));
        Assert.Equal(
            "role == \"dev\" && alter > 18",
            DesignerExpressionContext.Append("role == \"dev\"", "alter > 18", "&&"));
    }

    [Fact]
    public void Validate_faengt_Ausnahmen_einer_fremden_Engine_ab()
    {
        var result = DesignerExpressionContext.Validate(
            new ThrowingEvaluator(), "alter > 18", DesignerExpressionContext.Build(Dialog()));

        Assert.False(result.IsValid);
        Assert.Contains("nicht geprüft", result.Error);
    }

    // ---- Testdaten --------------------------------------------------------------------------------

    /// <summary>
    /// Baut einen Dialog-Graphen mit je einer Frage pro Typ und einer Schleife (Collection
    /// <c>skills</c>) – optional um weitere Fragen ergänzt.
    /// </summary>
    private static DialogDetail Dialog(params QuestionDetail[] additional)
    {
        var dialogId = Guid.NewGuid();

        var questions = new List<QuestionDetail>
        {
            Question("role", QuestionType.SingleChoice, dialogId, "dev"),
            Question("alter", QuestionType.Number, dialogId),
            Question("zustimmung", QuestionType.Boolean, dialogId),
            Question("bemerkung", QuestionType.FreeText, dialogId),
            Question("geburtstag", QuestionType.Date, dialogId),
            Question("sprachen", QuestionType.MultiChoice, dialogId, "de"),
        };
        questions.AddRange(additional);

        return new DialogDetail(
            new DialogSummary(
                dialogId, "onboarding", "Onboarding", null, 1, false, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            questions,
            [],
            [new LoopDetail(Guid.NewGuid(), dialogId, "skills", Guid.NewGuid(), Guid.NewGuid())]);
    }

    private static QuestionDetail Question(
        string key, QuestionType type, Guid? dialogId = null, string? optionValue = null)
    {
        var questionId = Guid.NewGuid();
        IReadOnlyList<AnswerOptionDetail> options = optionValue is null
            ? []
            : [new AnswerOptionDetail(Guid.NewGuid(), questionId, optionValue, optionValue, optionValue, 0)];

        return new QuestionDetail(
            questionId, dialogId ?? Guid.NewGuid(), key, $"Frage {key}?", type, 0, false, null, options);
    }

    /// <summary>Handgeschriebenes TestDouble: eine Engine, die beim Prüfen wirft (kein Mocking-Framework).</summary>
    private sealed class ThrowingEvaluator : IExpressionEvaluator
    {
        public bool Evaluate(string expression, ExpressionContext context)
            => throw new InvalidOperationException("kaputt");

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
            => throw new InvalidOperationException("kaputt");
    }
}
