using Flirty.Domain;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Navigation-free view of a <see cref="Dialog"/> (only the metadata, without the
/// configuration graph). Result of the dialog CRUD commands and the dialog list.
/// </summary>
/// <param name="Id">The primary key of the dialog.</param>
/// <param name="Key">The business, stable key of the dialog.</param>
/// <param name="Name">The display name of the dialog.</param>
/// <param name="Description">The optional description of the dialog.</param>
/// <param name="Version">The version number of the dialog.</param>
/// <param name="IsPublished">Indicates whether the dialog is published (startable in production).</param>
/// <param name="StartQuestionId">Reference to the entry question, or <see langword="null"/>.</param>
/// <param name="CreatedAt">Point in time of creation.</param>
/// <param name="UpdatedAt">Point in time of the last change.</param>
public sealed record DialogSummary(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    int Version,
    bool IsPublished,
    Guid? StartQuestionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Navigation-free view of a <see cref="Dialog"/> along with its graph managed as part of the admin CRUD
/// (questions incl. options, transitions, loop markers and triggers). Result of
/// <c>GetDialogQuery</c>.
/// </summary>
/// <param name="Dialog">The dialog metadata.</param>
/// <param name="Questions">The questions of the dialog (including their answer options), sorted by <c>Order</c>.</param>
/// <param name="Transitions">The conditional transitions of the dialog, sorted by <c>Priority</c>.</param>
/// <param name="Loops">The loop markers of the dialog, sorted by <c>CollectionKey</c>.</param>
/// <param name="Triggers">
/// The trigger definitions of the dialog, sorted by <c>Scope</c>, <c>Kind</c> and configuration
/// (the entity has no order of its own).
/// </param>
/// <param name="Layout">
/// The stored canvas positions of the dialog, sorted by <c>ElementKind</c> and <c>ElementId</c>.
/// Pure display data of the designer; if no row exists for an element, the auto-layout arranges it there.
/// </param>
public sealed record DialogDetail(
    DialogSummary Dialog,
    IReadOnlyList<QuestionDetail> Questions,
    IReadOnlyList<TransitionDetail> Transitions,
    IReadOnlyList<LoopDetail> Loops,
    IReadOnlyList<TriggerDetail> Triggers,
    IReadOnlyList<DialogLayoutDetail> Layout);

/// <summary>
/// Navigation-free view of a <see cref="Question"/> for the admin CRUD (with all
/// configurable fields, unlike the lean runtime view <see cref="QuestionView"/>).
/// </summary>
/// <param name="Id">The primary key of the question.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="Key">The business, stable key of the question.</param>
/// <param name="Text">The displayed question text.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="CustomTypeKey">
/// The key of the host-declared custom question type, or <see langword="null"/>. Only ever set
/// together with <see cref="QuestionType.Json"/>.
/// </param>
/// <param name="Order">The order index of the question within the dialog.</param>
/// <param name="IsRequired">Indicates whether an answer is required.</param>
/// <param name="ValidationRules">Optional validation rules as JSON.</param>
/// <param name="Options">The answer options of the question, sorted by <c>Order</c>.</param>
public sealed record QuestionDetail(
    Guid Id,
    Guid DialogId,
    string Key,
    string Text,
    QuestionType Type,
    string? CustomTypeKey,
    int Order,
    bool IsRequired,
    string? ValidationRules,
    IReadOnlyList<AnswerOptionDetail> Options);

/// <summary>
/// Navigation-free view of an <see cref="AnswerOption"/> for the admin CRUD.
/// </summary>
/// <param name="Id">The primary key of the answer option.</param>
/// <param name="QuestionId">The foreign key to the associated question.</param>
/// <param name="Key">The business, stable key of the option.</param>
/// <param name="Label">The displayed label text of the option.</param>
/// <param name="Value">The value of the option stored on selection.</param>
/// <param name="Order">The order index of the option within the question.</param>
public sealed record AnswerOptionDetail(
    Guid Id,
    Guid QuestionId,
    string Key,
    string Label,
    string Value,
    int Order);

/// <summary>
/// Navigation-free view of a <see cref="Transition"/> (branching transition) for the admin CRUD.
/// </summary>
/// <param name="Id">The primary key of the transition.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="FromQuestionId">Reference to the source question.</param>
/// <param name="TargetQuestionId">Reference to the target question.</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
/// <param name="Priority">Priority for the evaluation order (smaller value = earlier).</param>
/// <param name="IsDefault">Indicates whether this transition is the default.</param>
public sealed record TransitionDetail(
    Guid Id,
    Guid DialogId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);

/// <summary>
/// Navigation-free view of a <see cref="LoopDefinition"/> (loop marker) for the admin CRUD.
/// </summary>
/// <param name="Id">The primary key of the loop definition.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="CollectionKey">Key under which the answers gathered per iteration lie in the expression context.</param>
/// <param name="EntryQuestionId">Reference to the entry question of the loop.</param>
/// <param name="BreakingQuestionId">Reference to the breaking question (whose exit transition leaves the cycle).</param>
public sealed record LoopDetail(
    Guid Id,
    Guid DialogId,
    string CollectionKey,
    Guid EntryQuestionId,
    Guid BreakingQuestionId);

/// <summary>
/// Navigation-free view of a <see cref="TriggerDefinition"/> (back channel into the host application)
/// for the admin CRUD.
/// </summary>
/// <param name="Id">The primary key of the trigger definition.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="Scope">The point in the dialog flow at which the trigger fires.</param>
/// <param name="QuestionId">
/// The question the trigger listens to for <see cref="TriggerScope.AfterQuestion"/>; otherwise
/// <see langword="null"/>.
/// </param>
/// <param name="Kind">The channel (<see cref="TriggerKind.Webhook"/> or <see cref="TriggerKind.InProcess"/>).</param>
/// <param name="Config">The channel-specific configuration as JSON (schema: <see cref="TriggerConfig"/>).</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
public sealed record TriggerDetail(
    Guid Id,
    Guid DialogId,
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    string Config,
    string? Expression);

/// <summary>
/// Navigation-free view of a <see cref="DialogLayout"/> row (stored canvas position) for
/// the admin CRUD.
/// </summary>
/// <param name="Id">The primary key of the layout row.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="ElementKind">The kind of the positioned element.</param>
/// <param name="ElementId">Reference to the element (today always a <see cref="Question.Id"/>).</param>
/// <param name="X">The horizontal canvas coordinate in px.</param>
/// <param name="Y">The vertical canvas coordinate in px.</param>
public sealed record DialogLayoutDetail(
    Guid Id,
    Guid DialogId,
    LayoutElementKind ElementKind,
    Guid ElementId,
    int X,
    int Y);

/// <summary>
/// A canvas position to set – the input form of an entry in the batch of
/// <c>SetDialogLayoutCommand</c>.
/// </summary>
/// <remarks>
/// Without an <c>Id</c>: identified via (<see cref="ElementKind"/>, <see cref="ElementId"/>), because
/// the caller sets a position and does not need to know whether a row already exists for it.
/// </remarks>
/// <param name="ElementKind">The kind of the positioned element.</param>
/// <param name="ElementId">Reference to the element (today always a <see cref="Question.Id"/>).</param>
/// <param name="X">The horizontal canvas coordinate in px; must not be negative.</param>
/// <param name="Y">The vertical canvas coordinate in px; must not be negative.</param>
public sealed record DialogLayoutEntry(
    LayoutElementKind ElementKind,
    Guid ElementId,
    int X,
    int Y);
