using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Tests.Expressions;

/// <summary>
/// Verifiziert die gesandboxte Default-Engine aus Issue #23
/// (<see cref="DynamicExpressoExpressionEvaluator"/>): typisierte Auswertung von Antworten und
/// Loop-Collections, UND/ODER-Verknüpfungen, Zugriff auf die Kontext-Variablen sowie die
/// Sandbox-/Injection-Abwehr (keine Reflection, keine nicht gewhitelisteten Typen, keine Zuweisung).
/// </summary>
public sealed class DynamicExpressoExpressionEvaluatorTests
{
    private static readonly DynamicExpressoExpressionEvaluator Evaluator = new();

    private static DialogSession NewSession() => new()
    {
        Id = Guid.NewGuid(),
        DialogId = Guid.NewGuid(),
        DialogVersion = 1,
        ExternalUserKey = "user-1",
        Status = SessionStatus.InProgress,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    private static ExpressionContext Context(
        IReadOnlyDictionary<string, string?>? answers = null,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? collections = null,
        DateTimeOffset? now = null,
        int? iterationIndex = null)
        => new(NewSession(), now ?? DateTimeOffset.UnixEpoch, answers, collections, iterationIndex);

    [Theory]
    [InlineData("42", true)]
    [InlineData("18", false)]
    [InlineData("10", false)]
    public void Numerischer_Vergleich_wertet_Antwort_typisiert_aus(string age, bool expected)
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = age });

        Assert.Equal(expected, Evaluator.Evaluate("age > 18", context));
    }

    [Fact]
    public void Und_Verknuepfung_kombiniert_Zahl_und_Bool()
    {
        var context = Context(new Dictionary<string, string?>
        {
            ["age"] = "42",
            ["verified"] = "true",
        });

        Assert.True(Evaluator.Evaluate("age > 18 && verified == true", context));
        Assert.False(Evaluator.Evaluate("age > 18 && verified == false", context));
    }

    [Fact]
    public void Oder_Verknuepfung_trifft_zu_wenn_ein_Zweig_wahr_ist()
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = "42" });

        Assert.True(Evaluator.Evaluate("age > 100 || age > 18", context));
    }

    [Fact]
    public void Bool_Antwort_kann_direkt_als_Bedingung_genutzt_werden()
    {
        var context = Context(new Dictionary<string, string?> { ["verified"] = "true" });

        Assert.True(Evaluator.Evaluate("verified", context));
    }

    [Fact]
    public void String_Antwort_wird_aus_JSON_deserialisiert()
    {
        var context = Context(new Dictionary<string, string?> { ["name"] = "\"Ada\"" });

        Assert.True(Evaluator.Evaluate("name == \"Ada\"", context));
        Assert.False(Evaluator.Evaluate("name == \"Bob\"", context));
    }

    [Fact]
    public void Nicht_JSON_Wert_wird_als_roher_String_behandelt()
    {
        // Ein unquotierter Auswahl-Schlüssel ist kein gültiges JSON -> Fallback auf den Rohstring.
        var context = Context(new Dictionary<string, string?> { ["status"] = "active" });

        Assert.True(Evaluator.Evaluate("status == \"active\"", context));
    }

    [Fact]
    public void Loop_Collection_Count_ist_auswertbar()
    {
        var context = Context(collections: new Dictionary<string, IReadOnlyList<string?>>
        {
            ["positions"] = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"],
        });

        Assert.True(Evaluator.Evaluate("positions.Count > 0", context));
        Assert.True(Evaluator.Evaluate("positions.Count == 2", context));
    }

    [Fact]
    public void Leere_Loop_Collection_hat_Count_null()
    {
        var context = Context(collections: new Dictionary<string, IReadOnlyList<string?>>
        {
            ["positions"] = [],
        });

        Assert.False(Evaluator.Evaluate("positions.Count > 0", context));
    }

    [Fact]
    public void Kontext_Variable_now_ist_verfuegbar()
    {
        var context = Context(now: new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.True(Evaluator.Evaluate("now.Year == 2026", context));
    }

    [Theory]
    [InlineData("System.IO.File.ReadAllText(\"secret.txt\") != null")] // nicht gewhitelisteter Typ
    [InlineData("\"x\".GetType().Assembly != null")]                    // Reflection ist blockiert
    [InlineData("typeof(System.Environment) != null")]                  // Typ-/Reflection-Zugriff
    [InlineData("unknownVariable > 1")]                                 // unbekannter Bezeichner
    public void Nicht_gewhitelistete_oder_ungueltige_Ausdruecke_werfen(string expression)
    {
        Assert.Throws<ExpressionEvaluationException>(() => Evaluator.Evaluate(expression, Context()));
    }

    [Fact]
    public void Zuweisung_ist_deaktiviert_und_wirft()
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = "42" });

        Assert.Throws<ExpressionEvaluationException>(() => Evaluator.Evaluate("age = 99", context));
    }

    [Fact]
    public void Nicht_boolescher_Ausdruck_wirft()
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = "42" });

        Assert.Throws<ExpressionEvaluationException>(() => Evaluator.Evaluate("age", context));
    }

    [Fact]
    public void Geworfene_Exception_traegt_den_Ausdruck()
    {
        var exception = Assert.Throws<ExpressionEvaluationException>(
            () => Evaluator.Evaluate("unknownVariable > 1", Context()));

        Assert.Equal("unknownVariable > 1", exception.Expression);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Leerer_Ausdruck_wirft_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Evaluator.Evaluate("   ", Context()));
    }

    [Fact]
    public void Null_Ausdruck_wirft_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluator.Evaluate(null!, Context()));
    }

    [Fact]
    public void Null_Kontext_wirft_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluator.Evaluate("age > 18", null!));
    }

    // ---- Validierung / Compile-Check (Issue #24) ----

    private static ExpressionContext ValidationContext() => Context(
        answers: new Dictionary<string, string?>
        {
            ["age"] = "42",
            ["verified"] = "true",
            ["name"] = "\"Ada\"",
        },
        collections: new Dictionary<string, IReadOnlyList<string?>>
        {
            ["positions"] = ["{\"title\":\"Dev\"}"],
        },
        now: new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("age > 18")]                     // Vergleichsoperator
    [InlineData("age > 18 && verified == true")] // UND-Verknüpfung
    [InlineData("age > 100 || age > 18")]        // ODER-Verknüpfung
    [InlineData("verified")]                      // boolesche Antwort direkt
    [InlineData("positions.Count > 0")]          // Loop-Collection
    [InlineData("name == \"Ada\"")]              // String-Vergleich
    [InlineData("now.Year == 2026")]             // Kontext-Variable
    public void Validate_gueltiger_Ausdruck_ist_valide(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorPosition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_leerer_Ausdruck_ist_valide(string? expression)
    {
        // Null/leer gilt als „bedingungslos zutreffend" (konsistent zur Runtime-Semantik).
        var result = Evaluator.Validate(expression!, ValidationContext());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("age > > 18")] // doppelter Operator
    [InlineData("(age > 18")]  // unbalancierte Klammer
    public void Validate_syntaxfehler_ist_ungueltig(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("System.IO.File.ReadAllText(\"secret.txt\") != null")] // nicht gewhitelisteter Typ
    [InlineData("\"x\".GetType().Assembly != null")]                    // Reflection ist blockiert
    [InlineData("typeof(System.Environment) != null")]                  // Typ-/Reflection-Zugriff
    public void Validate_injection_ist_ungueltig(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Validate_unbekannter_Bezeichner_ist_ungueltig_mit_Position()
    {
        var result = Evaluator.Validate("unknownVariable > 1", Context());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.ErrorPosition);
    }

    [Fact]
    public void Validate_Zuweisung_ist_ungueltig()
    {
        var result = Evaluator.Validate("age = 99", ValidationContext());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("age")] // long-Ergebnis
    [InlineData("42")]  // int-Ergebnis
    public void Validate_nicht_boolescher_Ausdruck_ist_ungueltig(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.False(result.IsValid);
        Assert.Contains("boolesch", result.Error!);
    }

    [Fact]
    public void Validate_wirft_nicht_bei_fehlerhaftem_Ausdruck()
    {
        var exception = Record.Exception(
            () => Evaluator.Validate("System.IO.File.ReadAllText(\"x\")", Context()));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_null_Kontext_wirft_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluator.Validate("age > 18", null!));
    }
}
