namespace Flirty.Mcp;

/// <summary>
/// The error payload of a failed Flirty tool call: the <c>structuredContent</c> that
/// <see cref="FlirtyMcpExceptionFilter"/> writes.
/// </summary>
/// <remarks>
/// <para>
/// The member names are deliberately those of <c>ProblemDetails</c> (RFC 9457) that the HTTP surface
/// emits – <c>status</c>, <c>title</c>, <c>detail</c> and (for an invalid answer) <c>errors</c>. That is
/// what makes the parity test between the two surfaces three field comparisons instead of a translation
/// table, and a translation table is precisely where a parity bug hides.
/// </para>
/// <para>
/// Two deliberate differences from the HTTP payload. First, <c>type</c> is <b>not</b> carried across:
/// <c>TypedResults.Problem</c> fills it with a pointer into HTTP <i>response</i> semantics
/// (<c>https://tools.ietf.org/html/rfc9110#section-15.5.4</c>), and over MCP there is no HTTP response,
/// so copying it would be a falsehood in a payload whose whole purpose is honesty. Second,
/// <see cref="Status"/> is <b>advisory</b>: it has no meaning in the MCP protocol. It exists because it
/// is the most compact signal of the error class, and because it is the comparison key against the HTTP
/// surface.
/// </para>
/// <para>
/// It is a record and not a bare string on purpose: a non-object <c>structuredContent</c> is
/// protocol-version dependent (wrapped as <c>{"result": …}</c> for clients before SEP-2106, bare
/// afterwards), so every payload this package emits is an object.
/// </para>
/// </remarks>
/// <param name="Status">The HTTP status code the same failure would produce over HTTP; advisory here.</param>
/// <param name="Title">The short, stable classification of the failure (e.g. <c>"Conflict"</c>).</param>
/// <param name="Detail">The engine's own message for this failure.</param>
/// <param name="Errors">
/// The individual validation errors, keyed as over HTTP (<c>"value"</c> for an invalid answer);
/// <see langword="null"/> – and therefore omitted from the JSON – for every other failure.
/// </param>
internal sealed record FlirtyProblem(
    int Status,
    string Title,
    string Detail,
    IReadOnlyDictionary<string, string[]>? Errors);
