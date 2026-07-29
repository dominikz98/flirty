namespace Flirty.Domain;

/// <summary>
/// The author-chosen position of an element on the designer's graph canvas. Pure display data:
/// without a row the auto-layout arranges the element, and the runtime never reads the table.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately its own entity instead of two columns on <see cref="Question"/> (ADR 0007). This keeps
/// the graph entities free of display concerns and later also carries positions for elements that are
/// not a question (see <see cref="LayoutElementKind"/>).
/// </para>
/// <para>
/// The payoff is more than extensibility: because the table does not belong to the graph, writing
/// coordinates does not fall under the publish lock (<c>DialogEditGuard</c>) - a published dialog can
/// therefore be arranged clearly without the lock softening. Sessions pin
/// <c>DialogId</c>/<c>DialogVersion</c> and follow GUIDs, not pixels.
/// </para>
/// </remarks>
public sealed class DialogLayout
{
    /// <summary>Unique primary key of the layout row.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>The kind of element whose position is recorded here.</summary>
    public LayoutElementKind ElementKind { get; set; }

    /// <summary>
    /// Reference to the element - for <see cref="LayoutElementKind.Question"/> a
    /// <see cref="Question.Id"/>. Deliberately without a foreign key, like the question references in
    /// <see cref="LoopDefinition"/>; orphaned rows are cleaned up by <c>DeleteQuestionCommand</c>.
    /// </summary>
    public Guid ElementId { get; set; }

    /// <summary>The horizontal canvas coordinate of the top-left corner in px (never negative).</summary>
    public int X { get; set; }

    /// <summary>The vertical canvas coordinate of the top-left corner in px (never negative).</summary>
    public int Y { get; set; }

    /// <summary>The dialog this layout row belongs to.</summary>
    public Dialog Dialog { get; set; } = null!;
}
