namespace Flirty.Samples.Web;

/// <summary>
/// Constant keys of the web sample's demo dialog. The dialog demonstrates <b>branching</b>
/// (role-dependent branching) and a <b>loop over a list</b> (repeatedly collecting
/// skills). Kept centrally so that provisioner, tests and docs use the same keys.
/// </summary>
public static class DemoDialog
{
    /// <summary>Business key under which the demo dialog is started.</summary>
    public const string DialogKey = "web-onboarding";

    /// <summary>Display name of the demo dialog.</summary>
    public const string DialogName = "Web onboarding";

    /// <summary>Start question (SingleChoice dev/pm) – starting point of the branching.</summary>
    public const string RoleKey = "role";

    /// <summary>Detail question in the dev branch (FreeText).</summary>
    public const string LanguageKey = "language";

    /// <summary>Detail question in the default branch (FreeText).</summary>
    public const string ProductKey = "product";

    /// <summary>Entry question of the loop (FreeText) – collected per iteration.</summary>
    public const string SkillKey = "skill";

    /// <summary>Breaking question of the loop (SingleChoice yes/no).</summary>
    public const string MoreKey = "more";

    /// <summary>Question of the host-declared custom type <c>color</c> (Json, scalar).</summary>
    public const string ColourKey = "colour";

    /// <summary>Question of the host-declared custom type <c>address</c> (Json, composite).</summary>
    public const string AddressKey = "address";

    /// <summary>Final question after the loop (Boolean, terminal).</summary>
    public const string SummaryKey = "summary";

    /// <summary>Key under which the skills collected per iteration live in the expression context.</summary>
    public const string CollectionKey = "skills";

    /// <summary>
    /// Registry key of the scalar custom question type – a colour as a JSON string, checked in code by
    /// <see cref="ColourAnswerValidator"/>.
    /// </summary>
    public const string ColourTypeKey = "color";

    /// <summary>
    /// Registry key of the composite custom question type – a JSON object of several fields, checked in
    /// code by <see cref="AddressAnswerValidator"/>. Deliberately equal to
    /// <see cref="AddressKey"/>: the question key and the registry key are independent, and having them
    /// coincide once shows that nothing couples them.
    /// </summary>
    public const string AddressTypeKey = "address";

    /// <summary>
    /// Registry key of the message placeholder that greets the user by name (#140), resolved in code by
    /// <see cref="UserNamePlaceholderFiller"/> and referenced by the entry question text.
    /// </summary>
    public const string UserNamePlaceholderKey = "user-name";

    /// <summary>
    /// Registry key of the message placeholder that fills the delivery date (#140), resolved in code by
    /// <see cref="TodayPlaceholderFiller"/> and referenced by the final question text.
    /// </summary>
    public const string TodayPlaceholderKey = "today";
}
