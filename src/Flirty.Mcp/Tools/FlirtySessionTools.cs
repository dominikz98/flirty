using System.ComponentModel;
using Flirty.Runtime;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The session tools: start, play, read and correct a dialog run. The MCP counterpart of
/// <c>MapFlirtyEndpoints</c> – the runtime half of the surface, where the other seven classes are the
/// configuration half.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here. Two of them this class genuinely
/// departs from, and both are below.
/// </para>
/// <para>
/// <b><c>flirty_session_start_version</c> is the one tool with no HTTP twin, and that is the point.</b> It
/// starts a concrete dialog version <i>regardless of publication status</i>, which is exactly why
/// <c>MapFlirtyEndpoints</c> does not offer it: over HTTP the publish status stays the production barrier.
/// It exists so a draft is testable at all – without it the only way to try a dialog out would be to
/// publish it briefly, which arms it for real users in the meantime. The designer's test runner (#43) uses
/// the same facade operation for the same reason, and an MCP client authoring a dialog needs it just as
/// much.
/// </para>
/// <para>
/// <b>A test run is a real run.</b> It writes real sessions to the configured database and the engine's
/// notifications really are delivered, so a trigger of kind <c>Webhook</c> really posts to its configured
/// url. So that those sessions stay identifiable afterwards, <c>flirty_session_start_version</c> marks
/// what it stores with <see cref="TestUserKeyPrefix"/>, alongside the designer's own
/// <c>designer-test-</c>. <c>flirty_session_start</c> deliberately does <b>not</b>: it is the ordinary
/// production path, the twin of <c>POST /flirty/sessions</c>, and prefixing there would hand an MCP client
/// and an HTTP client two different sessions for the same user.
/// </para>
/// <para>
/// <b>The four writing tools set <c>OpenWorld = true</c>, and they are the only ones in the package that
/// do.</b> <see cref="FlirtyDialogTools"/> records <c>openWorld = false</c> as a fact about the admin
/// surface – it touches only its own database. That stops holding here: starting, submitting and editing
/// publish engine notifications, and the core's <c>WebhookNotificationHandler</c> turns those into outbound
/// HTTP calls to whatever absolute url a trigger was configured with. Declaring <c>false</c> while the
/// description says "delivers real webhooks" would be a contradiction on the wire.
/// <c>flirty_session_get</c> publishes nothing and stays <c>false</c>.
/// </para>
/// <para>
/// The results are the <b>runtime</b> core records (<see cref="StartDialogResult"/> and its siblings), not
/// the admin ones. <see cref="QuestionView"/> is deliberately leaner than the admin
/// <c>QuestionDetail</c> – it carries what a client needs to render a question, not what it needs to edit
/// one – and the two are kept apart here for the same reason the engine keeps them apart.
/// </para>
/// <para>
/// The per-type shape of the <c>value</c> argument is carried in the parameter descriptions of the two
/// tools that take it, and stated once in <see cref="FlirtyMcpInstructions"/>. It has to be: the schema of
/// that argument is <c>"string"</c>, which tells a model nothing, and getting it wrong is not always an
/// error. <c>AnswerValidator</c> accepts a <c>Boolean</c> as the bare literal <i>and</i> as the quoted
/// <c>"true"</c>, but only the bare form binds as a <see cref="bool"/> in a branching expression – the
/// quoted one arrives as a <see cref="string"/> and a condition comparing it to a boolean stops matching,
/// with nothing rejected along the way. <c>SingleChoice</c> at least fails loudly when handed an option's
/// label instead of its value, which is the mistake the sample chat UI shipped with (#47).
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtySessionTools
{
    /// <summary>
    /// The marker <c>flirty_session_start_version</c> prepends to the external user key it stores, so a
    /// draft test run is distinguishable from a production session afterwards.
    /// </summary>
    internal const string TestUserKeyPrefix = "mcp-test-";

    // Idempotent, which is not obvious: StartDialogCommand resumes the caller's still-running session on
    // the same dialog instead of opening a second one, and says so with isResumed. A repeat is a no-op.
    [McpServerTool(
        Name = FlirtyToolNames.SessionStart,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true)]
    [Description("Starts the published dialog with the given key for a user and returns the first "
        + "question, or resumes that user's session if one is already running (then isResumed is true). "
        + "Only published dialogs can be started this way - use flirty_session_start_version for a draft. "
        + "Beware that this really runs the dialog: configured webhook triggers are delivered.")]
    internal static async Task<StartDialogResult> StartSessionAsync(
        ISender sender,
        [Description("The business key of the dialog to start. It must be published.")]
        string dialogKey,
        [Description("Your identifier for the user this session belongs to. Passing the same one again "
            + "resumes the running session rather than starting a second.")]
        string externalUserKey,
        CancellationToken cancellationToken)
        => await sender.Send(new StartDialogCommand(dialogKey, externalUserKey), cancellationToken);

    // Idempotent for the same reason as flirty_session_start: the second call resumes the first session.
    [McpServerTool(
        Name = FlirtyToolNames.SessionStartVersion,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true)]
    [Description("Starts one concrete dialog version by id, published or not - the way to test a draft "
        + "without publishing it first. This is deliberately the only tool with no HTTP counterpart: over "
        + "HTTP the publish status stays the production barrier. The run is real all the same - it writes "
        + "a real session and delivers configured webhook triggers - so the session is stored under the "
        + "external user key prefixed with 'mcp-test-' to keep test runs identifiable.")]
    internal static async Task<StartDialogResult> StartSessionVersionAsync(
        ISender sender,
        [Description("The id of the dialog version to start. Take it from flirty_dialog_list or from what "
            + "flirty_dialog_create_version returned; publication status is not checked.")]
        Guid dialogId,
        [Description("Your identifier for the user this test session belongs to. It is stored prefixed "
            + "with 'mcp-test-'; passing the same one again resumes the running test session.")]
        string externalUserKey,
        CancellationToken cancellationToken)
        => await sender.Send(
            new StartDialogVersionCommand(dialogId, Mark(externalUserKey)), cancellationToken);

    [McpServerTool(
        Name = FlirtyToolNames.SessionGet,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Reads the state of a session: its status, the question currently open (absent once the "
        + "session is completed or abandoned) and every answer given so far, in order, with the loop "
        + "iteration each belongs to. Read-only.")]
    internal static async Task<ResumeDialogResult> GetSessionAsync(
        ISender sender,
        [Description("The id of the session to read.")]
        Guid sessionId,
        CancellationToken cancellationToken)
        => await sender.Send(new ResumeDialogQuery(sessionId), cancellationToken);

    // Not idempotent: the repeat answers a question that is no longer the open one and is refused with a
    // conflict, so a blind retry after a timeout is not safe - read the state with flirty_session_get.
    [McpServerTool(
        Name = FlirtyToolNames.SessionSubmitAnswer,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true)]
    [Description("Answers the question currently open in a session and advances to the next one, or "
        + "completes the session when the branch ends (then isCompleted is true and nextQuestion is "
        + "absent). Answering anything other than the open question is a conflict. The answer is validated "
        + "against the question's type and validation rules; a rejected answer reports the field errors "
        + "under errors.value. Delivers configured webhook triggers.")]
    internal static async Task<SubmitAnswerResult> SubmitAnswerAsync(
        ISender sender,
        [Description("The id of the session to answer in.")]
        Guid sessionId,
        [Description("The id of the question being answered. It must be the one the session currently has "
            + "open - flirty_session_get reports it as currentQuestion.")]
        Guid questionId,
        [Description(ValueContract)]
        string value,
        CancellationToken cancellationToken)
        => await sender.Send(new SubmitAnswerCommand(sessionId, questionId, value), cancellationToken);

    // Destructive: every answer given after the edited one is discarded, which is not recoverable.
    // Idempotent all the same - the repeat writes the same value and finds nothing left downstream.
    [McpServerTool(
        Name = FlirtyToolNames.SessionEditAnswer,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true)]
    [Description("Corrects an answer already given and recomputes the path from there. Every answer after "
        + "the edited one is discarded - invalidatedAnswers reports how many - because a different answer "
        + "can lead down a different branch. A completed session reopens if the new path has a follow-up "
        + "question. Editing an unanswered question is a conflict. Delivers configured webhook triggers.")]
    internal static async Task<EditAnswerResult> EditAnswerAsync(
        ISender sender,
        [Description("The id of the session whose answer is corrected.")]
        Guid sessionId,
        [Description("The id of the question whose answer is corrected. It must already be answered.")]
        Guid questionId,
        [Description(ValueContract)]
        string value,
        CancellationToken cancellationToken,
        [Description("Which loop iteration to correct, zero-based, for a question inside a loop. Omit it "
            + "to correct the earliest answer to this question, which is what you want outside a loop.")]
        int? iterationIndex = null)
        => await sender.Send(
            new EditAnswerCommand(sessionId, questionId, value, iterationIndex), cancellationToken);

    /// <summary>
    /// The per-question-type shape of the <c>value</c> argument, shared by the two tools that take it.
    /// </summary>
    /// <remarks>
    /// A const rather than two copies because it is a contract, and two copies drift. It is stated a second
    /// time in <see cref="FlirtyMcpInstructions"/> on purpose: a client that speaks protocol revision
    /// <c>2026-07-28</c> never receives the instructions, and this text is the half it cannot guess.
    /// </remarks>
    private const string ValueContract =
        "The answer, as raw JSON text. Its shape depends on the question's type, and the schema here says "
        + "only 'string', so: FreeText and Date take a JSON string - \"hello\", \"2026-07-31\" (ISO-8601). "
        + "SingleChoice takes a JSON string holding the option's VALUE, not its label - read the options "
        + "with flirty_dialog_get. MultiChoice takes a JSON array of strings - [\"a\",\"b\"]. Number takes "
        + "a bare JSON number with a dot as the decimal separator - 42, 3.14. Boolean takes bare true or "
        + "false; the quoted form \"true\" also passes validation but is stored as a string, so a "
        + "branching condition comparing that answer to a boolean stops matching - send the bare literal.";

    /// <summary>Marks an external user key as belonging to a test run.</summary>
    /// <remarks>
    /// A blank key stays blank on purpose. Prefixing it would turn <c>""</c> into a non-empty string and so
    /// silently satisfy <c>[Required]</c> on <c>StartDialogVersionCommand.ExternalUserKey</c> – the 400 the
    /// engine owes the caller would never arrive, and the session would be stored under the bare prefix.
    /// Same instinct as <see cref="FlirtyLayoutTools"/> guarding none of its input: the rule has one home,
    /// and this must not stand in front of it. Otherwise the prefix is applied unconditionally, so the
    /// stored key is a pure function of the argument.
    /// </remarks>
    private static string Mark(string externalUserKey)
        => string.IsNullOrWhiteSpace(externalUserKey)
            ? externalUserKey
            : TestUserKeyPrefix + externalUserKey;
}
