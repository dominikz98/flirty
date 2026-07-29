namespace Flirty.Designer.Models;

/// <summary>
/// Kind of value that an expression variable delivers in the expression context. Controls, in the
/// snippet inserter of the branching editor (#40), the offered operators and the quoting of the
/// entered comparison value.
/// </summary>
internal enum ExpressionValueKind
{
    /// <summary>String – comparison values are quoted (<c>role == "dev"</c>).</summary>
    Text = 0,

    /// <summary>Number – comparison values are taken raw (<c>alter &gt; 18</c>).</summary>
    Number = 1,

    /// <summary>Boolean – <c>true</c>/<c>false</c> or the identifier alone.</summary>
    Boolean = 2,

    /// <summary>List (multi-choice or loop collection) – <c>skills.Count &gt; 0</c>.</summary>
    List = 3,

    /// <summary>
    /// Reserved context variable (<c>now</c>, <c>session</c>). Shown only in the reference table,
    /// not in the snippet inserter – meaningful expressions here access members (<c>now.Year</c>).
    /// </summary>
    Context = 4,
}

/// <summary>
/// An identifier available in the expression context together with type and example – the data basis for the
/// reference table and the snippet inserter of the branching editor (#40).
/// </summary>
/// <param name="Name">The identifier as it appears in the expression (question key, <c>CollectionKey</c> or reserved name).</param>
/// <param name="Kind">The kind of value (controls operators and quoting).</param>
/// <param name="TypeLabel">The German type name for the display (e.g. "Zahl").</param>
/// <param name="Example">A valid example expression with this identifier.</param>
/// <param name="IsUsable">
/// Indicates whether the identifier can be referenced in the expression. <see langword="false"/> e.g. for
/// keys that are not valid identifiers, or that are shadowed by a reserved variable.
/// </param>
/// <param name="Note">
/// Explanation for the reference table – for non-usable identifiers the reason, otherwise a
/// hint about the meaning (or <see langword="null"/>).
/// </param>
internal sealed record ExpressionVariable(
    string Name,
    ExpressionValueKind Kind,
    string TypeLabel,
    string Example,
    bool IsUsable,
    string? Note);
