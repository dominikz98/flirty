using System.Text;
using System.Text.Json;
using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Builds the <b>sample context</b> for expression validation in the designer (#40) – the counterpart to
/// the core-internal <c>SessionExpressionContextBuilder</c>, only without a running session: instead of
/// real answers, a <b>type-correct sample value</b> is bound per question, so that
/// <see cref="IExpressionEvaluator.Validate(string, ExpressionContext)"/> performs the same compile check
/// the runtime later makes against real answers.
/// </summary>
/// <remarks>
/// <para>
/// The <b>types</b> are what matters, not the values: the evaluator deserializes the raw JSON text of an
/// answer in a typed way (JSON number → <c>long</c>/<c>double</c>, JSON string → <c>string</c>,
/// <c>true</c>/<c>false</c> → <c>bool</c>, array → list). The sample values here mirror this binding
/// exactly – otherwise the designer would wave through expressions that fail at runtime (or vice versa).
/// In particular, a date answer is a <b>string</b> at runtime too, not a <c>DateTimeOffset</c>.
/// </para>
/// <para>
/// Loop collections are – as by the <c>LoopResolver</c> at runtime – <b>always</b> bound, before the
/// first iteration simply as an empty list. Only that way is <c>skills.Count &gt; 0</c> checkable at all.
/// </para>
/// </remarks>
internal static class DesignerExpressionContext
{
    /// <summary>Building-block operator: set check on a list (<c>skills.Count &gt; 0</c>).</summary>
    public const string CountGreaterOperator = "Count >";

    /// <summary>Building-block operator: exact count of a list (<c>skills.Count == 3</c>).</summary>
    public const string CountEqualsOperator = "Count ==";

    /// <summary>Building-block operator: containment (<c>skills.Contains("csharp")</c>).</summary>
    public const string ContainsOperator = "contains";

    /// <summary>
    /// The reserved context variables. The evaluator sets them <b>last</b>; question/collection keys of
    /// the same name are thereby shadowed and not reachable in the expression.
    /// </summary>
    private static readonly string[] ReservedNames = ["now", "iterationIndex", "session"];

    /// <summary>
    /// Builds the sample context of the dialog: a type-correct sample value per question, an (empty) list
    /// per loop collection, plus a dummy session.
    /// </summary>
    /// <param name="detail">The dialog including its graph (from <c>GetDialogQuery</c>).</param>
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

        // iterationIndex is always bound by the evaluator as int? – for the compile check only this type
        // matters, not the value.
        return new ExpressionContext(session, DateTimeOffset.UtcNow, answers, collections, iterationIndex: 0);
    }

    /// <summary>
    /// Describes all identifiers available in the sample context – the basis for the reference table and
    /// the building-block inserter.
    /// </summary>
    /// <param name="detail">The dialog including its graph.</param>
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
                "List (per iteration)",
                $"{key}.Count > 0",
                IsBindable(key),
                IsBindable(key)
                    ? "Collects the loop's answers – empty before the first iteration."
                    : IdentifierNote(key)));
        }

        variables.Add(new ExpressionVariable(
            "now", ExpressionValueKind.Context, "Timestamp", "now.Year >= 2026", true,
            "Evaluation timestamp (UTC)."));
        variables.Add(new ExpressionVariable(
            "iterationIndex", ExpressionValueKind.Number, "Number (optional)", "iterationIndex == 0", true,
            "Zero-based loop index; empty outside a loop."));
        variables.Add(new ExpressionVariable(
            "session", ExpressionValueKind.Context, "Session", "session.ExternalUserKey == \"customer-1\"", true,
            "The running session (e.g. ExternalUserKey, StartedAt)."));

        return variables;
    }

    /// <summary>
    /// Runs the compile check and also catches unexpected exceptions – a typo in the editor must never
    /// tear the Blazor circuit.
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
                $"The expression could not be checked: {exception.Message}");
        }
    }

    /// <summary>Returns the operators offered in the building-block inserter for the value kind.</summary>
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
    /// Assembles a condition building block from variable, operator and value. Strings are quoted and
    /// escaped, numbers/booleans are taken raw.
    /// </summary>
    /// <param name="variable">The chosen variable.</param>
    /// <param name="operatorToken">The chosen operator (see <see cref="OperatorsFor"/>).</param>
    /// <param name="value">The entered comparison value.</param>
    /// <returns>The ready-to-insert expression building block.</returns>
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
    /// Combines an existing expression with a new building block. If the expression is empty, the block
    /// stands alone; otherwise it is appended via <c>&amp;&amp;</c>/<c>||</c> (existing OR parts stay
    /// untouched – the bracketing is up to the user).
    /// </summary>
    /// <param name="expression">The existing expression.</param>
    /// <param name="condition">The building block to append.</param>
    /// <param name="conjunction">The conjunction (<c>&amp;&amp;</c> or <c>||</c>).</param>
    /// <returns>The assembled expression.</returns>
    public static string Append(string? expression, string condition, string conjunction)
        => string.IsNullOrWhiteSpace(expression)
            ? condition
            : $"{expression.Trim()} {conjunction} {condition}";

    // ---- Sample values ----------------------------------------------------------------------------

    /// <summary>
    /// Returns the sample value of a question as raw JSON text – in the same format in which
    /// <c>SessionAnswer.Value</c> is stored at runtime. The encoding deliberately comes from the
    /// <see cref="AnswerValueCodec"/> (the single source of the contract), so the sample context does not
    /// bind differently than the test runner actually submits.
    /// </summary>
    /// <param name="question">The question a sample value is needed for.</param>
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

    // ---- Identifiers ------------------------------------------------------------------------------

    /// <summary>
    /// Indicates whether a key can be bound as an expression variable: a valid identifier and not taken by
    /// a reserved context variable.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns><see langword="true"/> when the key is referenceable.</returns>
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
            return "Shadowed by the loop collection of the same name – rename the key.";
        }

        return question.Type == QuestionType.Date
            ? "Date answers are strings (no comparison with now possible)."
            : null;
    }

    /// <summary>
    /// Explains why a key is not usable as an expression variable. Also used by the
    /// <see cref="LoopAnalyzer"/>, so the loop editor reports the same wording as the identifier reference
    /// of the branching editor.
    /// </summary>
    /// <param name="key">The non-bindable key.</param>
    /// <returns>The explanation.</returns>
    internal static string IdentifierNote(string key)
        => ReservedNames.Contains(key, StringComparer.Ordinal)
            ? $"Shadowed by the reserved context variable \"{key}\" – rename the key."
            : "Not a valid identifier (only letters, digits and underscore, not starting with a digit) – "
                + "not referenceable in expressions.";

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
            QuestionType.Number => "Number",
            QuestionType.Boolean => "Yes/No",
            QuestionType.Date => "Date (text)",
            QuestionType.SingleChoice => "Choice (text)",
            QuestionType.MultiChoice => "Multiple choice (list)",
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
    /// Quotes a text as an expression literal. Deliberately <b>not</b> via <see cref="JsonSerializer"/>:
    /// its <c>\u00XX</c> escapes are not understood by the expression engine's parser ("Invalid character
    /// escape sequence"). Only the C# escapes that DynamicExpresso knows are allowed; other control
    /// characters are dropped instead of producing an unparsable expression.
    /// </summary>
    /// <param name="value">The text to quote.</param>
    /// <returns>The ready-to-insert string literal including quotes.</returns>
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
