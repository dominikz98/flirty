using System.Text;
using System.Text.Json;
using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Baut den <b>Musterkontext</b> für die Ausdrucks-Validierung im Designer (#40) – das Gegenstück zum
/// Core-internen <c>SessionExpressionContextBuilder</c>, nur ohne laufende Session: Statt echter
/// Antworten werden je Frage <b>typrichtige Beispielwerte</b> gebunden, damit
/// <see cref="IExpressionEvaluator.Validate(string, ExpressionContext)"/> denselben Compile-Check
/// durchführt, den die Laufzeit später gegen echte Antworten macht.
/// </summary>
/// <remarks>
/// <para>
/// Maßgeblich sind die <b>Typen</b>, nicht die Werte: Der Evaluator deserialisiert den rohen
/// JSON-Text einer Antwort typisiert (JSON-Zahl → <c>long</c>/<c>double</c>, JSON-String →
/// <c>string</c>, <c>true</c>/<c>false</c> → <c>bool</c>, Array → Liste). Die Beispielwerte hier
/// spiegeln diese Bindung exakt – sonst würde der Designer Ausdrücke durchwinken, die zur Laufzeit
/// scheitern (oder umgekehrt). Insbesondere ist eine Datumsantwort auch zur Laufzeit eine
/// <b>Zeichenkette</b>, kein <c>DateTimeOffset</c>.
/// </para>
/// <para>
/// Loop-Collections werden – wie vom <c>LoopResolver</c> zur Laufzeit – <b>stets</b> gebunden, vor der
/// ersten Iteration eben als leere Liste. Nur so ist <c>skills.Count &gt; 0</c> überhaupt prüfbar.
/// </para>
/// </remarks>
internal static class DesignerExpressionContext
{
    /// <summary>Baustein-Operator: Mengenprüfung auf einer Liste (<c>skills.Count &gt; 0</c>).</summary>
    public const string CountGreaterOperator = "Anzahl >";

    /// <summary>Baustein-Operator: exakte Anzahl einer Liste (<c>skills.Count == 3</c>).</summary>
    public const string CountEqualsOperator = "Anzahl ==";

    /// <summary>Baustein-Operator: Enthaltensein (<c>skills.Contains("csharp")</c>).</summary>
    public const string ContainsOperator = "enthält";

    /// <summary>
    /// Die reservierten Kontext-Variablen. Der Evaluator setzt sie <b>zuletzt</b>; gleichnamige
    /// Frage-/Collection-Schlüssel werden dadurch verdeckt und sind im Ausdruck nicht erreichbar.
    /// </summary>
    private static readonly string[] ReservedNames = ["now", "iterationIndex", "session"];

    /// <summary>
    /// Baut den Musterkontext des Dialogs: je Frage ein typrichtiger Beispielwert, je Loop-Collection
    /// eine (leere) Liste, dazu eine Attrappen-Session.
    /// </summary>
    /// <param name="detail">Der Dialog samt Graph (aus <c>GetDialogQuery</c>).</param>
    /// <returns>Der Kontext, gegen den Bedingungsausdrücke validiert werden.</returns>
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

        // iterationIndex wird vom Evaluator immer als int? gebunden – für den Compile-Check zählt nur
        // dieser Typ, nicht der Wert.
        return new ExpressionContext(session, DateTimeOffset.UtcNow, answers, collections, iterationIndex: 0);
    }

    /// <summary>
    /// Beschreibt alle im Musterkontext verfügbaren Bezeichner – Grundlage für die Referenztabelle und
    /// den Baustein-Einfüger.
    /// </summary>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <returns>Fragen, Loop-Collections und reservierte Kontext-Variablen in Anzeigereihenfolge.</returns>
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
    /// Führt den Compile-Check aus und fängt dabei auch unerwartete Ausnahmen ab – ein Tippfehler im
    /// Editor darf niemals den Blazor-Circuit reißen.
    /// </summary>
    /// <param name="evaluator">Die Ausdrucks-Engine (Singleton aus <c>AddFlirty()</c>).</param>
    /// <param name="expression">Der zu prüfende Ausdruck; <see langword="null"/>/leer gilt als gültig.</param>
    /// <param name="context">Der Musterkontext aus <see cref="Build"/>.</param>
    /// <returns>Das Prüfergebnis.</returns>
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

    /// <summary>Liefert die im Baustein-Einfüger angebotenen Operatoren zur Wertart.</summary>
    /// <param name="kind">Die Wertart der gewählten Variable.</param>
    /// <returns>Die passenden Operatoren.</returns>
    public static IReadOnlyList<string> OperatorsFor(ExpressionValueKind kind)
        => kind switch
        {
            ExpressionValueKind.Number => ["==", "!=", ">", ">=", "<", "<="],
            ExpressionValueKind.Boolean => ["==", "!="],
            ExpressionValueKind.List => [CountGreaterOperator, CountEqualsOperator, ContainsOperator],
            _ => ["==", "!=", ContainsOperator],
        };

    /// <summary>
    /// Setzt aus Variable, Operator und Wert einen Bedingungs-Baustein zusammen. Zeichenketten werden
    /// quotiert und escaped, Zahlen/Wahrheitswerte roh übernommen.
    /// </summary>
    /// <param name="variable">Die gewählte Variable.</param>
    /// <param name="operatorToken">Der gewählte Operator (siehe <see cref="OperatorsFor"/>).</param>
    /// <param name="value">Der eingegebene Vergleichswert.</param>
    /// <returns>Der einfügefertige Ausdrucks-Baustein.</returns>
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
    /// Verknüpft einen bestehenden Ausdruck mit einem neuen Baustein. Ist der Ausdruck leer, steht der
    /// Baustein allein; sonst wird er per <c>&amp;&amp;</c>/<c>||</c> angehängt (bestehende ODER-Teile
    /// bleiben dabei unangetastet – die Klammerung liegt beim Nutzer).
    /// </summary>
    /// <param name="expression">Der bisherige Ausdruck.</param>
    /// <param name="condition">Der anzuhängende Baustein.</param>
    /// <param name="conjunction">Die Verknüpfung (<c>&amp;&amp;</c> oder <c>||</c>).</param>
    /// <returns>Der zusammengesetzte Ausdruck.</returns>
    public static string Append(string? expression, string condition, string conjunction)
        => string.IsNullOrWhiteSpace(expression)
            ? condition
            : $"{expression.Trim()} {conjunction} {condition}";

    // ---- Beispielwerte ----------------------------------------------------------------------------

    /// <summary>
    /// Liefert den Beispielwert einer Frage als rohen JSON-Text – im selben Format, in dem
    /// <c>SessionAnswer.Value</c> zur Laufzeit gespeichert wird.
    /// </summary>
    /// <param name="question">Die Frage, für die ein Beispielwert gebraucht wird.</param>
    /// <returns>Der Beispielwert als JSON.</returns>
    private static string SampleJson(QuestionDetail question)
        => question.Type switch
        {
            QuestionType.Number => "0",
            QuestionType.Boolean => "true",
            QuestionType.MultiChoice => JsonSerializer.Serialize(new[] { SampleText(question) }),
            _ => JsonSerializer.Serialize(SampleText(question)),
        };

    /// <summary>Der Beispielwert einer zeichenkettenwertigen Frage – unescaped, für Beispiel-Ausdrücke.</summary>
    private static string SampleText(QuestionDetail question)
        => question.Type switch
        {
            QuestionType.Date => "2026-01-01",
            QuestionType.SingleChoice or QuestionType.MultiChoice => FirstOptionValue(question) ?? "option",
            _ => "Text",
        };

    private static string? FirstOptionValue(QuestionDetail question)
        => question.Options.Count == 0 ? null : question.Options[0].Value;

    // ---- Bezeichner -------------------------------------------------------------------------------

    /// <summary>
    /// Gibt an, ob ein Schlüssel als Ausdrucks-Variable gebunden werden kann: gültiger Bezeichner und
    /// nicht von einer reservierten Kontext-Variable belegt.
    /// </summary>
    /// <param name="key">Der zu prüfende Schlüssel.</param>
    /// <returns><see langword="true"/>, wenn der Schlüssel referenzierbar ist.</returns>
    private static bool IsBindable(string key)
        => IsIdentifier(key) && !ReservedNames.Contains(key, StringComparer.Ordinal);

    /// <summary>Prüft, ob der Schlüssel die Form eines Bezeichners hat (<c>[A-Za-z_][A-Za-z0-9_]*</c>).</summary>
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

    private static string IdentifierNote(string key)
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

    // ---- Literale ---------------------------------------------------------------------------------

    private static string Literal(ExpressionValueKind kind, string? value)
        => kind switch
        {
            ExpressionValueKind.Number => NumberLiteral(value),
            ExpressionValueKind.Boolean => BooleanLiteral(value),
            _ => TextLiteral(value),
        };

    /// <summary>
    /// Quotiert einen Text als Ausdrucks-Literal. Bewusst <b>nicht</b> über
    /// <see cref="JsonSerializer"/>: dessen <c>\u00XX</c>-Escapes versteht der Parser der Ausdrucks-Engine
    /// nicht („Invalid character escape sequence"). Erlaubt sind nur die C#-Escapes, die DynamicExpresso
    /// kennt; sonstige Steuerzeichen fallen weg, statt einen unparsbaren Ausdruck zu erzeugen.
    /// </summary>
    /// <param name="value">Der zu quotierende Text.</param>
    /// <returns>Das einfügefertige Zeichenketten-Literal inklusive Anführungszeichen.</returns>
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
