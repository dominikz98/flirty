namespace Flirty.Mcp.Tools;

/// <summary>
/// The wire names of every tool this package registers – the single parity checklist of the MCP surface.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>[McpServerTool]</c> takes its <c>Name</c> from here and never lets the SDK derive one. The
/// SDK's <c>DeriveName</c> strips an <c>Async</c> suffix and snake_cases the method name, so a C# rename –
/// a refactoring that touches no contract – would silently rename a tool for every client. The names are
/// <c>flirty_&lt;area&gt;_&lt;action&gt;</c>, lower snake_case, and they are the <b>only</b> stable
/// identifier this package exposes.
/// </para>
/// <para>
/// This list is the checklist itself, not a copy of one: the golden tool-list test reflects over the
/// literal fields of this class and compares that set with what <c>tools/list</c> returns, in both
/// directions. A const without a tool fails as loudly as a tool without a const, which is why a name is
/// declared here in the stage that registers its tool and never ahead of it. Thirty-two today: the
/// twenty-seven admin tools of stages #126 and #127, and the five <c>flirty_session_*</c> of the runtime
/// stage #128. For the same reason the class holds nothing but string literals – the test filters on
/// exactly that, so any other member would silently join the checklist.
/// </para>
/// <para>
/// Note <c>flirty_option_*</c>: the wire name is short where the class mirroring
/// <c>MapAnswerOptionEndpoints</c> is <see cref="FlirtyAnswerOptionTools"/>, and it follows the HTTP route
/// segment <c>.../options</c>. Deliberate – a tool name is typed by a model, a class name is read by a
/// maintainer.
/// </para>
/// </remarks>
internal static class FlirtyToolNames
{
    // ---- Dialogs (10) – FlirtyDialogTools, mirrors MapDialogEndpoints ------------------------------

    /// <summary>Creates an unpublished dialog with version 1.</summary>
    internal const string DialogCreate = "flirty_dialog_create";

    /// <summary>Lists all dialogs as metadata, without their graphs.</summary>
    internal const string DialogList = "flirty_dialog_list";

    /// <summary>Reads one dialog along with its whole configuration graph.</summary>
    internal const string DialogGet = "flirty_dialog_get";

    /// <summary>Updates the dialog metadata and optionally the entry question.</summary>
    internal const string DialogUpdate = "flirty_dialog_update";

    /// <summary>Deletes a dialog together with its graph.</summary>
    internal const string DialogDelete = "flirty_dialog_delete";

    /// <summary>Publishes a dialog and retires the previously published version of the same key.</summary>
    internal const string DialogPublish = "flirty_dialog_publish";

    /// <summary>Withdraws a dialog from production.</summary>
    internal const string DialogUnpublish = "flirty_dialog_unpublish";

    /// <summary>Clones a dialog as an unpublished draft with the version raised by one.</summary>
    internal const string DialogCreateVersion = "flirty_dialog_create_version";

    /// <summary>Ends all sessions still running on a dialog version.</summary>
    internal const string DialogAbandonSessions = "flirty_dialog_abandon_sessions";

    /// <summary>Counts the sessions still in progress on a dialog version.</summary>
    internal const string DialogCountActiveSessions = "flirty_dialog_count_active_sessions";

    // ---- Questions (3) – FlirtyQuestionTools, mirrors MapQuestionEndpoints -------------------------

    /// <summary>Creates a question in a dialog.</summary>
    internal const string QuestionCreate = "flirty_question_create";

    /// <summary>Updates a question in place.</summary>
    internal const string QuestionUpdate = "flirty_question_update";

    /// <summary>Deletes a question and everything that referenced it.</summary>
    internal const string QuestionDelete = "flirty_question_delete";

    // ---- Answer options (3) – FlirtyAnswerOptionTools, mirrors MapAnswerOptionEndpoints -----------

    /// <summary>Creates an answer option on a question.</summary>
    internal const string OptionCreate = "flirty_option_create";

    /// <summary>Updates an answer option in place.</summary>
    internal const string OptionUpdate = "flirty_option_update";

    /// <summary>Deletes an answer option.</summary>
    internal const string OptionDelete = "flirty_option_delete";

    // ---- Transitions (3) – FlirtyTransitionTools, mirrors MapTransitionEndpoints -------------------

    /// <summary>Creates a transition between two questions.</summary>
    internal const string TransitionCreate = "flirty_transition_create";

    /// <summary>Updates a transition in place.</summary>
    internal const string TransitionUpdate = "flirty_transition_update";

    /// <summary>Deletes a transition.</summary>
    internal const string TransitionDelete = "flirty_transition_delete";

    // ---- Loops (3) – FlirtyLoopTools, mirrors MapLoopEndpoints -------------------------------------

    /// <summary>Creates a loop marker over a cycle in the graph.</summary>
    internal const string LoopCreate = "flirty_loop_create";

    /// <summary>Updates a loop marker in place.</summary>
    internal const string LoopUpdate = "flirty_loop_update";

    /// <summary>Deletes a loop marker.</summary>
    internal const string LoopDelete = "flirty_loop_delete";

    // ---- Triggers (3) – FlirtyTriggerTools, mirrors MapTriggerEndpoints ----------------------------

    /// <summary>Creates a trigger (back channel into the host application).</summary>
    internal const string TriggerCreate = "flirty_trigger_create";

    /// <summary>Updates a trigger in place.</summary>
    internal const string TriggerUpdate = "flirty_trigger_update";

    /// <summary>Deletes a trigger.</summary>
    internal const string TriggerDelete = "flirty_trigger_delete";

    // ---- Layout (2) – FlirtyLayoutTools, mirrors MapLayoutEndpoints. The two tools the publish
    //      lock does not reach (ADR 0007). ----------------------------------------------------------

    /// <summary>Sets canvas positions as a batch upsert; works on a published dialog.</summary>
    internal const string LayoutSet = "flirty_layout_set";

    /// <summary>Discards all stored canvas positions; works on a published dialog.</summary>
    internal const string LayoutReset = "flirty_layout_reset";

    // ---- Sessions (5) – FlirtySessionTools, mirrors MapFlirtyEndpoints. The runtime half; the only
    //      area whose tools reach outside the database, because a run delivers webhooks. -------------

    /// <summary>Starts the published dialog with a key, or resumes the user's running session.</summary>
    internal const string SessionStart = "flirty_session_start";

    /// <summary>Starts one dialog version by id regardless of publication status.</summary>
    internal const string SessionStartVersion = "flirty_session_start_version";

    /// <summary>Reads the state of a session: status, open question and the answers so far.</summary>
    internal const string SessionGet = "flirty_session_get";

    /// <summary>Answers the open question and advances the session.</summary>
    internal const string SessionSubmitAnswer = "flirty_session_submit_answer";

    /// <summary>Corrects an answer already given and discards the downstream answers.</summary>
    internal const string SessionEditAnswer = "flirty_session_edit_answer";
}
