using System.Text.Json;
using DynamicExpresso;

namespace Flirty.Expressions;

/// <summary>
/// Gesandboxte Default-Implementierung von <see cref="IExpressionEvaluator"/> auf Basis von
/// DynamicExpresso (Issue #23). Wertet boolesche Bedingungsausdrücke wie <c>age &gt; 18</c> oder
/// <c>positions.Count &gt; 0</c> aus, ohne beliebige Code-Ausführung zuzulassen: Es steht nur eine
/// Member-Whitelist zur Verfügung (kein roher C#-<c>eval</c>).
/// </summary>
/// <remarks>
/// <para>
/// Die Sandbox nutzt ausschließlich <see cref="InterpreterOptions.PrimitiveTypes"/> und
/// <see cref="InterpreterOptions.SystemKeywords"/> (Literale, Vergleichs-/Arithmetik- sowie
/// UND/ODER-Operatoren). <see cref="InterpreterOptions.CommonTypes"/> (z. B. <c>System.Math</c>,
/// <c>System.Convert</c>, <c>System.Linq.Enumerable</c>) ist bewusst <b>nicht</b> aktiviert;
/// Reflection bleibt blockiert (kein Aufruf von <c>EnableReflection</c>) und Zuweisungen sind
/// deaktiviert. Zugreifbar sind damit nur die injizierten Kontext-Variablen und deren Instanz-Member.
/// </para>
/// <para>
/// Als Ausdrucks-Variablen stehen zur Verfügung: jede Antwort (per <c>Question.Key</c>) und jede
/// Loop-Collection (per <c>CollectionKey</c>) als Top-Level-Bezeichner sowie <c>now</c>,
/// <c>iterationIndex</c> und <c>session</c>. Die roh als JSON-Text vorliegenden Werte werden dabei
/// typisiert deserialisiert (siehe <see cref="ExpressionContext"/>).
/// </para>
/// <para>
/// Die Klasse ist zustandslos (je Auswertung wird ein frischer <see cref="Interpreter"/> erzeugt)
/// und damit als Singleton nutzbar (DI-Verdrahtung folgt in Issue #34).
/// </para>
/// </remarks>
public sealed class DynamicExpressoExpressionEvaluator : IExpressionEvaluator
{
    private const InterpreterOptions SandboxOptions =
        InterpreterOptions.PrimitiveTypes | InterpreterOptions.SystemKeywords;

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="expression"/> ist <see langword="null"/>, leer oder nur Leerraum.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> ist <see langword="null"/>.</exception>
    /// <exception cref="ExpressionEvaluationException">
    /// Der Ausdruck konnte nicht zu einem booleschen Ergebnis ausgewertet werden – z. B. bei
    /// Syntaxfehlern, unbekannten Bezeichnern, nicht gewhitelisteten Typen/Membern (Sandbox-Verletzung)
    /// oder einem nicht-booleschen Ergebnis.
    /// </exception>
    public bool Evaluate(string expression, ExpressionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(context);

        var interpreter = new Interpreter(SandboxOptions);
        interpreter.EnableAssignment(AssignmentOperators.None);

        foreach (var (key, rawJson) in context.Answers)
        {
            SetVariable(interpreter, key, ParseJsonValue(rawJson));
        }

        foreach (var (key, entries) in context.Collections)
        {
            var items = new List<object?>(entries.Count);
            foreach (var entry in entries)
            {
                items.Add(ParseJsonValue(entry));
            }

            interpreter.SetVariable(key, items);
        }

        // Reservierte Kontext-Variablen zuletzt: sie sollen nicht von gleichnamigen Answer-/
        // Collection-Keys verdeckt werden.
        interpreter.SetVariable("now", context.Now);
        interpreter.SetVariable("iterationIndex", context.IterationIndex, typeof(int?));
        interpreter.SetVariable("session", context.Session);

        try
        {
            return interpreter.Eval<bool>(expression);
        }
        catch (Exception ex) when (ex is not ExpressionEvaluationException)
        {
            throw new ExpressionEvaluationException(
                expression,
                $"Der Bedingungsausdruck '{expression}' konnte nicht ausgewertet werden: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Setzt eine Ausdrucks-Variable und wählt für <see langword="null"/>-Werte bewusst den Typ
    /// <see cref="object"/>, damit DynamicExpresso einen deklarierten Typ erhält.
    /// </summary>
    private static void SetVariable(Interpreter interpreter, string name, object? value)
    {
        if (value is null)
        {
            interpreter.SetVariable(name, null, typeof(object));
        }
        else
        {
            interpreter.SetVariable(name, value);
        }
    }

    /// <summary>
    /// Deserialisiert den rohen JSON-Text einer Antwort typisiert. Ist der Text kein gültiges JSON
    /// (z. B. ein unquotierter Auswahl-Schlüssel), wird er unverändert als Zeichenkette verwendet.
    /// </summary>
    private static object? ParseJsonValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return ConvertElement(document.RootElement);
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    /// <summary>Bildet ein <see cref="JsonElement"/> auf einen passenden CLR-Wert ab.</summary>
    private static object? ConvertElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.TryGetInt64(out var integer) ? integer : element.GetDouble();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ConvertElement(item));
                }

                return list;

            case JsonValueKind.Object:
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    map[property.Name] = ConvertElement(property.Value);
                }

                return map;

            default:
                return null;
        }
    }
}
