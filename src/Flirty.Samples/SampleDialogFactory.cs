using Flirty.Domain;

namespace Flirty.Samples;

/// <summary>
/// Builds the published sample dialog that the console sample seeds into the database programmatically
/// (without the designer). The dialog demonstrates branching: the start question <c>role</c> branches
/// depending on the choice to a role-specific detail question that each completes the dialog.
/// </summary>
public static class SampleDialogFactory
{
    /// <summary>The business key under which the sample dialog is started.</summary>
    public const string DialogKey = "onboarding";

    /// <summary>
    /// Creates the complete dialog aggregate (questions, options, transitions) for the sample dialog.
    /// </summary>
    /// <remarks>
    /// Flow: <c>role</c> (SingleChoice <c>dev</c>/<c>pm</c>) → on <c>role == "dev"</c> to the free-text
    /// question <c>language</c>, otherwise by default to <c>product</c>. Both detail questions are
    /// terminal (no outgoing transition) and complete the dialog. All timestamps are UTC-normalized
    /// (the PostgreSQL provider requires Offset == UTC).
    /// </remarks>
    /// <returns>The dialog aggregate storable via <see cref="Flirty.Persistence.FlirtyDbContext"/>.</returns>
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
            Description = "Short onboarding with role-dependent branching.",
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
                    Text = "What is your role?",
                    Type = QuestionType.SingleChoice,
                    Order = 0,
                    IsRequired = true,
                    Options =
                    {
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "dev", Label = "Developer", Value = "dev", Order = 0 },
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "pm", Label = "Product Manager", Value = "pm", Order = 1 },
                    },
                },
                new Question
                {
                    Id = languageQuestionId,
                    DialogId = dialogId,
                    Key = "language",
                    Text = "Which programming language do you prefer?",
                    Type = QuestionType.FreeText,
                    Order = 1,
                    IsRequired = true,
                },
                new Question
                {
                    Id = productQuestionId,
                    DialogId = dialogId,
                    Key = "product",
                    Text = "Which product do you look after?",
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
