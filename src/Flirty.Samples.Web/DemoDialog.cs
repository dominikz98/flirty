namespace Flirty.Samples.Web;

/// <summary>
/// Konstante Schlüssel des Demo-Dialogs der Web-Sample. Der Dialog demonstriert <b>Branching</b>
/// (rollenabhängige Verzweigung) und einen <b>Loop über eine Liste</b> (wiederholtes Sammeln von
/// Fähigkeiten). Zentral gehalten, damit Provisioner, Tests und Doku dieselben Schlüssel verwenden.
/// </summary>
public static class DemoDialog
{
    /// <summary>Fachlicher Schlüssel, unter dem der Demo-Dialog gestartet wird.</summary>
    public const string DialogKey = "web-onboarding";

    /// <summary>Anzeigename des Demo-Dialogs.</summary>
    public const string DialogName = "Web-Onboarding";

    /// <summary>Startfrage (SingleChoice dev/pm) – Ausgangspunkt des Branchings.</summary>
    public const string RoleKey = "role";

    /// <summary>Detailfrage im dev-Zweig (FreeText).</summary>
    public const string LanguageKey = "language";

    /// <summary>Detailfrage im Default-Zweig (FreeText).</summary>
    public const string ProductKey = "product";

    /// <summary>Einstiegsfrage der Schleife (FreeText) – wird je Iteration gesammelt.</summary>
    public const string SkillKey = "skill";

    /// <summary>Breaking Question der Schleife (SingleChoice yes/no).</summary>
    public const string MoreKey = "more";

    /// <summary>Abschlussfrage nach der Schleife (Boolean, terminal).</summary>
    public const string SummaryKey = "summary";

    /// <summary>Schlüssel, unter dem die je Iteration gesammelten Fähigkeiten im Ausdruckskontext liegen.</summary>
    public const string CollectionKey = "skills";
}
