namespace Flirty.Samples.Web;

/// <summary>
/// Constant keys of the web sample's demo dialog. The dialog demonstrates <b>branching</b>
/// (role-dependent branching) and a <b>loop over a list</b> (repeated collection of
/// skills). Kept central so that provisioner, tests and docs use the same keys.
/// </summary>
public static class DemoDialog
{
    /// <summary>Business key under which the demo dialog is started.</summary>
    public const string DialogKey = "web-onboarding";

    /// <summary>Display name of the demo dialog.</summary>
    public const string DialogName = "Web-Onboarding";

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

    /// <summary>Completion question after the loop (Boolean, terminal).</summary>
    public const string SummaryKey = "summary";

    /// <summary>Key under which the skills collected per iteration are held in the expression context.</summary>
    public const string CollectionKey = "skills";
}
