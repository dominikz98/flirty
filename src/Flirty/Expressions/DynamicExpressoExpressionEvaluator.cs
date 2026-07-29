using System.Text.Json;
using DynamicExpresso;
using DynamicExpresso.Exceptions;

namespace Flirty.Expressions;

/// <summary>
/// Sandboxed default implementation of <see cref="IExpressionEvaluator"/> based on DynamicExpresso
/// (issue #23). Evaluates boolean condition expressions such as <c>age &gt; 18</c> or
/// <c>positions.Count &gt; 0</c> without allowing arbitrary code execution: only a member whitelist is
/// available (no raw C# <c>eval</c>).
/// </summary>
/// <remarks>
/// <para>
/// The sandbox uses exclusively <see cref="InterpreterOptions.PrimitiveTypes"/> and
/// <see cref="InterpreterOptions.SystemKeywords"/> (literals, comparison/arithmetic and AND/OR
/// operators). <see cref="InterpreterOptions.CommonTypes"/> (e.g. <c>System.Math</c>,
/// <c>System.Convert</c>, <c>System.Linq.Enumerable</c>) is deliberately <b>not</b> enabled; reflection
/// stays blocked (no call to <c>EnableReflection</c>) and assignments are disabled. Only the injected
/// context variables and their instance members are therefore accessible.
/// </para>
/// <para>
/// The following are available as expression variables: every answer (via <c>Question.Key</c>) and
/// every loop collection (via <c>CollectionKey</c>) as a top-level identifier, plus <c>now</c>,
/// <c>iterationIndex</c> and <c>session</c>. The values, present raw as JSON text, are deserialized in a
/// typed way (see <see cref="ExpressionContext"/>).
/// </para>
/// <para>
/// The class is stateless (a fresh <see cref="Interpreter"/> is created per evaluation) and therefore
/// usable as a singleton (DI wiring follows in issue #34).
/// </para>
/// </remarks>
public sealed class DynamicExpressoExpressionEvaluator : IExpressionEvaluator
{
    private const InterpreterOptions SandboxOptions =
        InterpreterOptions.PrimitiveTypes | InterpreterOptions.SystemKeywords;

    /// <summary>
    /// Message for a reflection access (<c>x.GetType()</c>, <c>…Assembly</c>). Replaces the message from
    /// DynamicExpresso that advises enabling reflection – which is deliberately excluded here.
    /// </summary>
    private const string ReflectionNotAllowedMessage =
        "Access to types and reflection (e.g. GetType()) is not allowed in conditions. Only the "
        + "dialog's answers, the loop collections and now, iterationIndex and session are available.";

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is <see langword="null"/>, empty or whitespace only.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ExpressionEvaluationException">
    /// The expression could not be evaluated to a boolean result – e.g. on syntax errors, unknown
    /// identifiers, non-whitelisted types/members (sandbox violation) or a non-boolean result.
    /// </exception>
    public bool Evaluate(string expression, ExpressionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(context);

        var interpreter = BuildInterpreter(context);

        try
        {
            return interpreter.Eval<bool>(expression);
        }
        catch (ReflectionNotAllowedException ex)
        {
            // The same custom message as in Validate – the expression can also reach the database
            // without the designer (directly via the admin API) and then only surfaces here.
            throw new ExpressionEvaluationException(
                expression,
                $"The condition expression '{expression}' could not be evaluated: "
                + ReflectionNotAllowedMessage,
                ex);
        }
        catch (Exception ex) when (ex is not ExpressionEvaluationException)
        {
            throw new ExpressionEvaluationException(
                expression,
                $"The condition expression '{expression}' could not be evaluated: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public ExpressionValidationResult Validate(string expression, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Null/empty counts semantically as "unconditionally true" (consistent with the runtime) -> valid.
        if (string.IsNullOrWhiteSpace(expression))
        {
            return ExpressionValidationResult.Valid;
        }

        var interpreter = BuildInterpreter(context);

        try
        {
            // Parse compiles the expression into a lambda but does not execute it.
            var lambda = interpreter.Parse(expression);

            return lambda.ReturnType == typeof(bool)
                ? ExpressionValidationResult.Valid
                : ExpressionValidationResult.Invalid(
                    $"The expression does not yield a boolean result (type: {lambda.ReturnType.Name}).");
        }
        catch (ReflectionNotAllowedException ex)
        {
            // Custom message instead of the passed-through library message: DynamicExpresso advises
            // `Interpreter.EnableReflection()` there – a hint to the library embedder, not to the dialog
            // author who types the expression in the designer. That very enabling is deliberately
            // excluded (ADR 0004), so the advice would be misleading.
            return ExpressionValidationResult.Invalid(ReflectionNotAllowedMessage, ex.Position);
        }
        catch (ParseException ex)
        {
            // Syntax errors, unknown identifiers, non-whitelisted types, disabled assignment – all with a
            // reported position and with a message that tells the dialog author something.
            return ExpressionValidationResult.Invalid(ex.Message, ex.Position);
        }
        catch (Exception ex)
        {
            // Safety net: Validate must never throw for a faulty expression.
            return ExpressionValidationResult.Invalid(ex.Message);
        }
    }

    /// <summary>
    /// Builds a fresh, sandboxed <see cref="Interpreter"/> and binds the context variables (answers,
    /// loop collections, <c>now</c>, <c>iterationIndex</c>, <c>session</c>). Shared by
    /// <see cref="Evaluate"/> and <see cref="Validate"/> so that evaluation and compile check use exactly
    /// the same sandbox and variable binding.
    /// </summary>
    private static Interpreter BuildInterpreter(ExpressionContext context)
    {
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

        // Reserved context variables last: they must not be shadowed by answer/collection keys of the
        // same name.
        interpreter.SetVariable("now", context.Now);
        interpreter.SetVariable("iterationIndex", context.IterationIndex, typeof(int?));
        interpreter.SetVariable("session", context.Session);

        return interpreter;
    }

    /// <summary>
    /// Sets an expression variable and deliberately chooses the type <see cref="object"/> for
    /// <see langword="null"/> values so that DynamicExpresso receives a declared type.
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
    /// Deserializes the raw JSON text of an answer in a typed way. If the text is not valid JSON (e.g. an
    /// unquoted selection key), it is used unchanged as a string.
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

    /// <summary>Maps a <see cref="JsonElement"/> to a matching CLR value.</summary>
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
