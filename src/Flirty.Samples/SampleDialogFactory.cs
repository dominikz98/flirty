using Flirty.Domain;

namespace Flirty.Samples;

/// <summary>
/// Baut den veröffentlichten Beispiel-Dialog, den das Console-Sample programmatisch (ohne Designer)
/// in die Datenbank seedet. Der Dialog demonstriert Branching: die Startfrage <c>role</c> verzweigt je
/// nach Auswahl auf eine rollenspezifische Detailfrage, die den Dialog jeweils abschließt.
/// </summary>
public static class SampleDialogFactory
{
    /// <summary>Der fachliche Schlüssel, unter dem der Beispiel-Dialog gestartet wird.</summary>
    public const string DialogKey = "onboarding";

    /// <summary>
    /// Erzeugt das vollständige Dialog-Aggregat (Fragen, Optionen, Übergänge) für den Beispiel-Dialog.
    /// </summary>
    /// <remarks>
    /// Ablauf: <c>role</c> (SingleChoice <c>dev</c>/<c>pm</c>) → bei <c>role == "dev"</c> auf die
    /// Freitext-Frage <c>language</c>, sonst per Default auf <c>product</c>. Beide Detailfragen sind
    /// terminal (kein ausgehender Übergang) und schließen den Dialog ab. Alle Zeitstempel sind
    /// UTC-normalisiert (der PostgreSQL-Provider verlangt Offset == UTC).
    /// </remarks>
    /// <returns>Das für <see cref="Flirty.Persistence.FlirtyDbContext"/> speicherbare Dialog-Aggregat.</returns>
    public static Dialog BuildOnboardingDialog()
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

        var dialogId = Guid.NewGuid();
        var roleQuestionId = Guid.NewGuid();
        var languageQuestionId = Guid.NewGuid();
        var productQuestionId = Guid.NewGuid();

        return new Dialog
        {
            Id = dialogId,
            Key = DialogKey,
            Name = "Onboarding",
            Description = "Kurzes Onboarding mit rollenabhängiger Verzweigung.",
            Version = 1,
            IsPublished = true,
            StartQuestionId = roleQuestionId,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            Questions =
            {
                new Question
                {
                    Id = roleQuestionId,
                    DialogId = dialogId,
                    Key = "role",
                    Text = "Welche Rolle hast du?",
                    Type = QuestionType.SingleChoice,
                    Order = 0,
                    IsRequired = true,
                    Options =
                    {
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "dev", Label = "Entwickler", Value = "dev", Order = 0 },
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "pm", Label = "Product Manager", Value = "pm", Order = 1 },
                    },
                },
                new Question
                {
                    Id = languageQuestionId,
                    DialogId = dialogId,
                    Key = "language",
                    Text = "Welche Programmiersprache nutzt du am liebsten?",
                    Type = QuestionType.FreeText,
                    Order = 1,
                    IsRequired = true,
                },
                new Question
                {
                    Id = productQuestionId,
                    DialogId = dialogId,
                    Key = "product",
                    Text = "Welches Produkt betreust du?",
                    Type = QuestionType.FreeText,
                    Order = 2,
                    IsRequired = true,
                },
            },
            Transitions =
            {
                new Transition
                {
                    Id = Guid.NewGuid(),
                    DialogId = dialogId,
                    FromQuestionId = roleQuestionId,
                    Expression = "role == \"dev\"",
                    TargetQuestionId = languageQuestionId,
                    Priority = 0,
                    IsDefault = false,
                },
                new Transition
                {
                    Id = Guid.NewGuid(),
                    DialogId = dialogId,
                    FromQuestionId = roleQuestionId,
                    TargetQuestionId = productQuestionId,
                    Priority = 1,
                    IsDefault = true,
                },
            },
        };
    }
}
