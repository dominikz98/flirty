using System.Text;
using System.Text.Json;
using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Builds the <b>sample context</b> for the expression validation in the designer (#40) – the counterpart to the
/// core-internal <c>SessionExpressionContextBuilder</c>, only without a running session: instead of real
/// answers, <b>type-correct sample values</b> are bound per question, so that
/// <see cref="IExpressionEvaluator.Validate(string, ExpressionContext)"/> performs the same compile check
/// that the runtime later makes against real answers.
/// </summary>
/// <remarks>
/// <para>
/// Authoritative are the <b>types</b>, not the values: the evaluator deserializes the raw
/// JSON text of an answer typed (JSON number → <c>long</c>/<c>double</c>, JSON string →
/// <c>string</c>, <c>true</c>/<c>false</c> → <c>bool</c>, array → list). The sample values here
/// mirror this binding exactly – otherwise the designer would wave through expressions that fail at
/// runtime (or vice versa). In particular, a date answer is also at runtime a
/// <b>string</b>, not a <c>DateTimeOffset</c>.
/// </para>
/// <para>
/// Loop collections are – as by the <c>LoopResolver</c> at runtime – <b>always</b> bound, before the
/// first iteration just as an empty list. Only so is <c>skills.Count &gt; 0</c> checkable at all.
/// </para>
/// </remarks>
internal static class DesignerExpressionContext
{
    /// <summary>Snippet operator: count check on a list (<c>skills.Count &gt; 0</c>).</summary>
    public const string CountGreaterOperator = "Anzahl >";

    /// <summary>Snippet operator: exact count of a list (<c>skills.Count == 3</c>).</summary>
    public const string CountEqualsOperator = "Anzahl ==";

    /// <summary>Snippet operator: containment (<c>skills.Contains("csharp")</c>).</summary>
    public const string ContainsOperator = "enthält";

    /// <summary>
    /// The reserved context variables. The evaluator sets them <b>last</b>; question/collection keys
    /// of the same name are thereby shadowed and are not reachable in the expression.
    /// </summary>
    private static readonly string[] ReservedNames = ["now", "iterationIndex", "session"];

    /// <summary>
    /// Builds the sample context of the dialog: per question a type-correct sample value, per loop collection
    /// a (empty) list, plus a dummy session.
    /// </summary>
    /// <param name="detail">The dialog together with the graph (from <c>GetDialogQuery</c>).</param>
    /// <returns>The context against which condition expressions are validated.</returns>
    public static ExpressionContext Build(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var collections = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal);
        foreach (var loop in detail.Loops.Where(loop => IsBindable(loop.CollectionKey)))
        {
            collections[loop.CollectionKey] = [];
        }

        var answers = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var question in detail.Questions.Where(question => IsBindable(question.Key)))
        {
            answers[question.Key] = SampleJson(question);
        }

        var session = new DialogSession
        {
            Id = Guid.Empty,
            DialogId = detail.Dialog.Id,
            DialogVersion = detail.Dialog.Version,
            ExternalUserKey = "designer",
            Status = SessionStatus.InProgress,
            StartedAt = DateTimeOffset.UtcNow,
        };

        // iterationIndex is always bound by the evaluator as int? – for the compile check only
        // this type counts, not the value.
        return new ExpressionContext(session, DateTimeOffset.UtcNow, answers, collections, iterationIndex: 0);
    }

    /// <summary>
    /// Describes all identifiers available in the sample context – basis for the reference table and
    /// the snippet inserter.
    /// </summary>
    /// <param name="detail">The dialog together with the graph.</param>
    /// <returns>Questions, loop collections and reserved context variables in display order.</returns>
    public static IReadOnlyList<ExpressionVariable> Describe(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var collectionKeys = detail.Loops
            .Select(loop => loop.CollectionKey)
            .ToHashSet(StringComparer.Ordinal);

        var variables = new List<ExpressionVariable>();

        foreach (var question in detail.Questions)
        {
            var kind = KindOf(question.Type);
            variables.Add(new ExpressionVariable(
                question.Key,
                kind,
                TypeLabelOf(question.Type),
                ExampleFor(question.Key, kind, question),
                IsBindable(question.Key) && !collectionKeys.Contains(question.Key),
                NoteFor(question, collectionKeys)));
        }

        foreach (var key in collectionKeys.OrderBy(key => key, StringComparer.Ordinal))
        {
            variables.Add(new ExpressionVariable(
                key,
                ExpressionValueKind.List,
                "Liste (je Iteration)",
                $"{key}.Count > 0",
                IsBindable(key),
                IsBindable(key)
                    ? "Sammelt die Antworten der Schleife – vor der ersten Iteration leer."
                    : IdentifierNote(key)));
        }

        variables.Add(new ExpressionVariable(
            "now", ExpressionValueKind.Context, "Zeitpunkt", "now.Year >= 2026", true,
            "Auswertungszeitpunkt (UTC)."));
        variables.Add(new ExpressionVariable(
            "iterationIndex", ExpressionValueKind.Number, "Zahl (optional)", "iterationIndex == 0", true,
            "Nullbasierter Schleifen-Index; außerhalb einer Schleife leer."));
        variables.Add(new ExpressionVariable(
            "session", ExpressionValueKind.Context, "Session", "session.ExternalUserKey == \"kunde-1\"", true,
            "Die laufende Session (z. B. ExternalUserKey, StartedAt)."));

        return variables;
    }

    /// <summary>
    /// Runs the compile check and catches unexpected exceptions along the way – a typo in the
    /// editor must never tear down the Blazor circuit.
    /// </summary>
    /// <param name="evaluator">The expression engine (singleton from <c>AddFlirty()</c>).</param>
    /// <param name="expression">The expression to check; <see langword="null"/>/empty counts as valid.</param>
    /// <param name="context">The sample context from <see cref="Build"/>.</param>
    /// <returns>The check result.</returns>
    public static ExpressionValidationResult Validate(
        IExpressionEvaluator evaluator, string? expression, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return evaluator.Validate(expression!, context);
        }
        catch (Exception exception)
        {
            return ExpressionValidationResult.Invalid(
                $"Der Ausdruck konnte nicht geprüft werden: {exception.Message}");
        }
    }

    /// <summary>Returns the operators offered in the snippet inserter for the value kind.</summary>
    /// <param name="kind">The value kind of the chosen variable.</param>
    /// <returns>The matching operators.</returns>
    public static IReadOnlyList<string> OperatorsFor(ExpressionValueKind kind)
        => kind switch
        {
            ExpressionValueKind.Number => ["==", "!=", ">", ">=", "<", "<="],
            ExpressionValueKind.Boolean => ["==", "!="],
            ExpressionValueKind.List => [CountGreaterOperator, CountEqualsOperator, ContainsOperator],
            _ => ["==", "!=", ContainsOperator],
        };

    /// <summary>
    /// Assembles a condition snippet from variable, operator and value. Strings are
    /// quoted and escaped, numbers/boolean values taken raw.
    /// </summary>
    /// <param name="variable">The chosen variable.</param>
    /// <param name="operatorToken">The chosen operator (see <see cref="OperatorsFor"/>).</param>
    /// <param name="value">The entered comparison value.</param>
    /// <returns>The ready-to-insert expression snippet.</returns>
    public static string BuildCondition(ExpressionVariable variable, string operatorToken, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorToken);

        return operatorToken switch
        {
            ContainsOperator => $"{variable.Name}.Contains({TextLiteral(value)})",
            CountGreaterOperator => $"{variable.Name}.Count > {NumberLiteral(value)}",
            CountEqualsOperator => $"{variable.Name}.Count == {NumberLiteral(value)}",
            _ => $"{variable.Name} {operatorToken} {Literal(variable.Kind, value)}",
        };
    }

    /// <summary>
    /// Links an existing expression with a new snippet. If the expression is empty, the
    /// snippet stands alone; otherwise it is appended via <c>&amp;&amp;</c>/<c>||</c> (existing OR parts
    /// stay untouched thereby – the parenthesization is up to the user).
    /// </summary>
    /// <param name="expression">The previous expression.</param>
    /// <param name="condition">The snippet to append.</param>
    /// <param name="conjunction">The linkage (<c>&amp;&amp;</c> or <c>||</c>).</param>
    /// <returns>The composed expression.</returns>
    public static string Append(string? expression, string condition, string conjunction)
        => string.IsNullOrWhiteSpace(expression)
            ? condition
            : $"{expression.Trim()} {conjunction} {condition}";

    // ---- Sample values ----------------------------------------------------------------------------

    /// <summary>
    /// Returns the sample value of a question as raw JSON text – in the same format in which
    /// <c>SessionAnswer.Value</c> is stored at runtime. The encoding deliberately stems from the
    /// <see cref="AnswerValueCodec"/> (single source of the contract), so that the sample context does not bind
    /// differently than the test runner actually submits.
    /// </summary>
    /// <param name="question">The question for which a sample value is needed.</param>
    /// <returns>The sample value as JSON.</returns>
    private static string SampleJson(QuestionDetail question)
        => question.Type switch
        {
            QuestionType.Number => AnswerValueCodec.Encode(question.Type, "0"),
            QuestionType.Boolean => AnswerValueCodec.Encode(question.Type, "true"),
            QuestionType.MultiChoice => AnswerValueCodec.Encode(question.Type, null, [SampleText(question)]),
            _ => AnswerValueCodec.Encode(question.Type, SampleText(question)),
        };

    /// <summary>The sample value of a string-valued question – unescaped, for example expressions.</summary>
    private static string SampleText(QuestionDetail question)
        => question.Type switch
        {
            QuestionType.Date => "2026-01-01",
            QuestionType.SingleChoice or QuestionType.MultiChoice => FirstOptionValue(question) ?? "option",
            _ => "Text",
        };

    private static string? FirstOptionValue(QuestionDetail question)
        => question.Options.Count == 0 ? null : question.Options[0].Value;

    // ---- Identifiers -------------------------------------------------------------------------------

    /// <summary>
    /// Indicates whether a key can be bound as an expression variable: valid identifier and
    /// not occupied by a reserved context variable.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns><see langword="true"/> if the key is referenceable.</returns>
    internal static bool IsBindable(string key)
        => IsIdentifier(key) && !ReservedNames.Contains(key, StringComparer.Ordinal);

    /// <summary>Checks whether the key has the form of an identifier (<c>[A-Za-z_][A-Za-z0-9_]*</c>).</summary>
    private static bool IsIdentifier(string key)
    {
        if (string.IsNullOrEmpty(key) || (!char.IsAsciiLetter(key[0]) && key[0] != '_'))
        {
            return false;
        }

        foreach (var character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string? NoteFor(QuestionDetail question, IReadOnlySet<string> collectionKeys)
    {
        if (!IsIdentifier(question.Key) || ReservedNames.Contains(question.Key, StringComparer.Ordinal))
        {
            return IdentifierNote(question.Key);
        }

        if (collectionKeys.Contains(question.Key))
        {
            return "Wird von der gleichnamigen Loop-Collection verdeckt – Schlüssel umbenennen.";
        }

        return question.Type == QuestionType.Date
            ? "Datumsantworten liegen als Zeichenkette vor (kein Vergleich mit now möglich)."
            : null;
    }

    /// <summary>
    /// Explains why a key does not qualify as an expression variable. Also used by the
    /// <see cref="LoopAnalyzer"/>, so that the loop editor reports the same wording as the
    /// identifier reference of the branching editor.
    /// </summary>
    /// <param name="key">The non-bindable key.</param>
    /// <returns>The German explanation.</returns>
    internal static string IdentifierNote(string key)
        => ReservedNames.Contains(key, StringComparer.Ordinal)
            ? $"Wird von der reservierten Kontext-Variable „{key}\" verdeckt – Schlüssel umbenennen."
            : "Kein gültiger Bezeichner (nur Buchstaben, Ziffern und Unterstrich, nicht mit Ziffer "
                + "beginnend) – im Ausdruck nicht referenzierbar.";

    private static ExpressionValueKind KindOf(QuestionType type)
        => type switch
        {
            QuestionType.Number => ExpressionValueKind.Number,
            QuestionType.Boolean => ExpressionValueKind.Boolean,
            QuestionType.MultiChoice => ExpressionValueKind.List,
            _ => ExpressionValueKind.Text,
        };

    private static string TypeLabelOf(QuestionType type)
        => type switch
        {
            QuestionType.Number => "Zahl",
            QuestionType.Boolean => "Ja/Nein",
            QuestionType.Date => "Datum (Text)",
            QuestionType.SingleChoice => "Auswahl (Text)",
            QuestionType.MultiChoice => "Mehrfachauswahl (Liste)",
            _ => "Text",
        };

    private static string ExampleFor(string name, ExpressionValueKind kind, QuestionDetail question)
        => kind switch
        {
            ExpressionValueKind.Number => $"{name} > 0",
            ExpressionValueKind.Boolean => $"{name} == true",
            ExpressionValueKind.List => $"{name}.Count > 0",
            _ => $"{name} == {TextLiteral(SampleText(question))}",
        };

    // ---- Literals ---------------------------------------------------------------------------------

    private static string Literal(ExpressionValueKind kind, string? value)
        => kind switch
        {
            ExpressionValueKind.Number => NumberLiteral(value),
            ExpressionValueKind.Boolean => BooleanLiteral(value),
            _ => TextLiteral(value),
        };

    /// <summary>
    /// Quotes a text as an expression literal. Deliberately <b>not</b> via
    /// <see cref="JsonSerializer"/>: its <c>\u00XX</c> escapes are not understood by the parser of the expression engine
    /// ("Invalid character escape sequence"). Allowed are only the C# escapes that DynamicExpresso
    /// knows; other control characters are dropped, instead of producing an unparsable expression.
    /// </summary>
    /// <param name="value">The text to quote.</param>
    /// <returns>The ready-to-insert string literal including quotation marks.</returns>
    private static string TextLiteral(string? value)
    {
        var literal = new StringBuilder("\"");

        foreach (var character in value ?? string.Empty)
        {
            _ = character switch
            {
                '\\' => literal.Append(@"\\"),
                '"' => literal.Append("\\\""),
                '\n' => literal.Append(@"\n"),
                '\r' => literal.Append(@"\r"),
                '\t' => literal.Append(@"\t"),
                _ => char.IsControl(character) ? literal : literal.Append(character),
            };
        }

        return literal.Append('"').ToString();
    }

    private static string NumberLiteral(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "0" : trimmed;
    }

    private static string BooleanLiteral(string? value)
        => string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
}
