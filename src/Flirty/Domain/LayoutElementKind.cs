namespace Flirty.Domain;

/// <summary>
/// Defines what kind of element a <see cref="DialogLayout"/> row refers to.
/// </summary>
/// <remarks>
/// For now there is only <see cref="Question"/> - but this very enumeration type is the reason why the
/// layout lives in its own table instead of as two columns on the question: edge waypoints, note nodes
/// or a viewport can be added later without a schema rebuild (ADR 0007).
/// </remarks>
public enum LayoutElementKind
{
    /// <summary>The position of a question (<see cref="Domain.Question.Id"/>) on the canvas.</summary>
    Question = 0,
}
