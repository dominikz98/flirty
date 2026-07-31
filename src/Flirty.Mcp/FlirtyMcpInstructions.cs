namespace Flirty.Mcp;

/// <summary>
/// The server instructions this package reports to a connecting client – the one place that explains the
/// <i>shape</i> of a Flirty dialog rather than a single tool.
/// </summary>
/// <remarks>
/// <para>
/// The division of labour is deliberate: a tool <c>[Description]</c> says what one tool does, the
/// instructions say what a client needs to know before it picks a tool at all – the order in which a graph
/// is built, that ids are the currency, that a published version is locked, and above all the <b>two
/// arguments that are JSON inside a string</b>. Those two are why this text exists. Their schema is
/// <c>"type": "string"</c>, which tells a model nothing, so the shape has to be written out in prose; it is
/// stated here once and repeated in the parameter descriptions of the tools that take them.
/// </para>
/// <para>
/// That repetition is load-bearing, not tidiness. Instructions are delivered in
/// <c>InitializeResult.Instructions</c>, which reaches a client only if it performs the <c>initialize</c>
/// handshake – the SDK's own client does, negotiating <c>2025-06-18</c> even against this stateless server,
/// since stateless removed the session header rather than the handshake. A client that instead speaks
/// <c>2026-07-28</c> with per-request metadata lists and calls tools perfectly well and receives
/// <b>nothing</b> of this text (the SDK can carry instructions in <c>DiscoverResult.Instructions</c>, but
/// this server does not expose <c>discover</c>). So no fact may live here alone: everything below is also
/// carried by a tool or parameter <see cref="System.ComponentModel.DescriptionAttribute"/>, and those travel
/// with <c>tools/list</c>, which every client can read.
/// </para>
/// <para>
/// The text deliberately does not restate individual tool descriptions – the protocol's own guidance is
/// that instructions should not duplicate what <c>tools/list</c> already carries.
/// </para>
/// <para>
/// It is set unconditionally and is not configurable through <see cref="FlirtyMcpOptions"/>. A host that
/// wants to add its own guidance can append to it after <c>AddFlirtyMcp</c>, because the SDK's server
/// options are plain <c>IOptions</c>:
/// <c>services.Configure&lt;McpServerOptions&gt;(o =&gt; o.ServerInstructions += "…");</c>. A
/// <i>replace</i> knob is deliberately not offered: the content is a fact about Flirty's contract, not a
/// host preference, and dropping it would silently strand every write tool's description that assumes the
/// two JSON shapes were stated once.
/// </para>
/// </remarks>
internal static class FlirtyMcpInstructions
{
    /// <summary>The instruction text sent to the client.</summary>
    internal const string Text = """
        Flirty is a dialog (chatbot) engine for .NET. This server exposes its dialog configuration: every
        tool is a thin wrapper over one engine command, so the rules below are the engine's, not this
        layer's. A host may expose only part of the surface - trust tools/list.

        The shape of a dialog: a dialog owns questions, a question owns answer options, transitions connect
        two questions, loop markers mark a cycle in the graph, triggers call back into the host
        application, and layout rows hold canvas positions. Ids are the currency - create an element, keep
        the id that comes back. A typical build order is flirty_dialog_create, then flirty_question_create
        (plus flirty_option_create for SingleChoice and MultiChoice), then flirty_transition_create, then
        flirty_dialog_update to set startQuestionId, then flirty_dialog_publish.

        A published version is locked: every graph change on it fails with status 409. Use
        flirty_dialog_create_version to clone it as a draft with the version raised by one - note that
        every cloned element gets a new id. The two layout tools are the deliberate exception and work on a
        published dialog.

        Every update overwrites all fields, so read first and pass the current value for whatever stays the
        same. Enum arguments are the C# member names in PascalCase: FreeText, AfterQuestion, Webhook.

        Running a dialog is the other half of the surface: flirty_session_start plays a published dialog by
        key, flirty_session_start_version plays one version by id whether published or not (the way to test
        a draft), then flirty_session_submit_answer answers whatever flirty_session_get reports as the open
        question, and flirty_session_edit_answer corrects an earlier answer and discards everything after
        it. Such a run is real: it writes a session and configured Webhook triggers really do fire. Sessions
        from flirty_session_start_version are stored with the external user key prefixed 'mcp-test-'.

        Three arguments are JSON inside a string. Their schema is "string", so it tells you nothing - here
        is what goes in, camelCase throughout:

        - validationRules on a question, type-scoped, every field optional. FreeText:
          {"minLength":3,"maxLength":50,"pattern":"^[a-z]+$"} - pattern is a .NET regular expression
          matched partially, so anchor it for a full match. Number: {"min":0,"max":10}. The other four
          types have no rules. Omitting the argument on an update clears whatever was stored.
        - config on a trigger: {"url":"https://host.example/hook","name":"order-created"}. url is required
          for kind Webhook and must be an absolute http or https address; name is optional and is
          delivered as the X-Flirty-Trigger header. For kind InProcess pass {} - an empty string is
          rejected. Only these two fields survive a write; unknown fields are dropped.
        - value on an answer, whose shape follows the question's type. FreeText and Date: a JSON string,
          "hello" or "2026-07-31" (ISO-8601). SingleChoice: a JSON string holding the option's value, not
          its label. MultiChoice: a JSON array of strings, ["a","b"]. Number: a bare number with a dot as
          the decimal separator, 42 or 3.14. Boolean: bare true or false - the quoted "true" also passes
          validation but is stored as a string, and a branching condition comparing it to a boolean then
          stops matching, so send the bare literal.

        Errors arrive as isError with {"status","title","detail"} in structuredContent: 404 for an unknown
        element, 400 for an invalid request or answer, 409 for a conflict (published dialog, duplicate key,
        running sessions).
        """;
}
