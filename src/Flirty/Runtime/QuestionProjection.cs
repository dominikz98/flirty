using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Gemeinsame Projektion einer <see cref="Question"/> aus einem geladenen <see cref="Dialog"/>-Graphen
/// in die schlanke, navigationsfreie <see cref="QuestionView"/>. Wird von den Runtime-Handlern
/// (<see cref="StartDialogCommandHandler"/>, <see cref="SubmitAnswerCommandHandler"/>) geteilt, damit
/// die Auflösung samt Options-Reihenfolge nur an einer Stelle definiert ist.
/// </summary>
internal static class QuestionProjection
{
    /// <summary>
    /// Löst die Frage mit <paramref name="questionId"/> aus dem geladenen <paramref name="dialog"/>-Graphen
    /// auf und projiziert sie samt Optionen (in <see cref="AnswerOption.Order"/>-Reihenfolge) in eine
    /// <see cref="QuestionView"/>.
    /// </summary>
    /// <param name="dialog">Der geladene Dialog-Graph (inkl. Fragen und Optionen).</param>
    /// <param name="questionId">Die Id der aufzulösenden Frage.</param>
    /// <returns>Die projizierte <see cref="QuestionView"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Die Frage mit <paramref name="questionId"/> gehört nicht zum <paramref name="dialog"/>-Graphen
    /// (Fehlkonfiguration).
    /// </exception>
    public static QuestionView ResolveQuestion(Dialog dialog, Guid? questionId)
    {
        var question = dialog.Questions.FirstOrDefault(candidate => candidate.Id == questionId)
            ?? throw new InvalidOperationException(
                $"Die Frage '{questionId}' gehört nicht zum Dialog '{dialog.Key}'.");

        var options = question.Options
            .OrderBy(option => option.Order)
            .Select(option => new AnswerOptionView(option.Id, option.Key, option.Label, option.Value))
            .ToList();

        return new QuestionView(question.Id, question.Key, question.Text, question.Type, options);
    }
}
