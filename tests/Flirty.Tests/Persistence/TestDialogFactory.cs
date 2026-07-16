using Flirty.Domain;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Gemeinsame Testdaten-Fabrik für die Persistenz-Tests: erzeugt Dialog-Aggregate, die von den
/// SQLite-Konfigurationstests (#18) und den provider-übergreifenden Migrationstests (#19) genutzt
/// werden. Alle Zeitstempel sind UTC-normalisiert, weil der PostgreSQL-Provider (Npgsql)
/// <see cref="DateTimeOffset"/> auf <c>timestamptz</c> mappt und Offset == UTC verlangt.
/// </summary>
internal static class TestDialogFactory
{
    /// <summary>Deterministischer, UTC-normalisierter Zeitstempel für reproduzierbare Round-Trips.</summary>
    public static readonly DateTimeOffset SampleTime = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Erzeugt einen minimalen Dialog mit dem angegebenen <paramref name="key"/>, der
    /// <paramref name="version"/> und dem Anzeigenamen <paramref name="name"/>.</summary>
    public static Dialog NewDialog(string key, int version, string name) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = name,
        Version = version,
        CreatedAt = SampleTime,
        UpdatedAt = SampleTime,
    };

    /// <summary>
    /// Baut ein vollständiges Dialog-Aggregat mit je einem Kind pro Navigation (Frage mit zwei
    /// Optionen, Übergang, Schleife, Trigger). Liefert die Id der einzigen Frage über
    /// <paramref name="questionId"/> zurück.
    /// </summary>
    public static Dialog BuildFullDialog(Guid dialogId, out Guid questionId)
    {
        questionId = Guid.NewGuid();
        var qId = questionId;

        return new Dialog
        {
            Id = dialogId,
            Key = "onboarding",
            Name = "Onboarding",
            Version = 1,
            IsPublished = true,
            StartQuestionId = qId,
            CreatedAt = SampleTime,
            UpdatedAt = SampleTime,
            Questions =
            {
                new Question
                {
                    Id = qId,
                    DialogId = dialogId,
                    Key = "role",
                    Text = "Welche Rolle?",
                    Type = QuestionType.SingleChoice,
                    Order = 0,
                    IsRequired = true,
                    ValidationRules = "{\"maxLength\":50}",
                    Options =
                    {
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = qId, Key = "dev", Label = "Entwickler", Value = "dev", Order = 0 },
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = qId, Key = "pm", Label = "Product Manager", Value = "pm", Order = 1 },
                    },
                },
            },
            Transitions =
            {
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = qId,
                    TargetQuestionId = Guid.NewGuid(), Priority = 0, IsDefault = true,
                },
            },
            Loops =
            {
                new LoopDefinition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, CollectionKey = "positions",
                    EntryQuestionId = qId, BreakingQuestionId = Guid.NewGuid(),
                },
            },
            Triggers =
            {
                new TriggerDefinition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, Scope = TriggerScope.OnDialogCompleted,
                    Kind = TriggerKind.Webhook, Config = "{\"url\":\"https://example.test/hook\"}",
                },
            },
        };
    }

    /// <summary>
    /// Baut einen veröffentlichten Dialog mit Branching für die Submit-Runtime-Tests (#26): eine
    /// Start-Frage <c>role</c> (SingleChoice, Optionen <c>dev</c>/<c>pm</c>) mit einem bedingten
    /// Übergang (<c>role == "dev"</c>) auf die Frage <c>devDetail</c> und einem Default-Übergang auf
    /// <c>pmDetail</c>. Beide Ziel-Fragen sind terminal (keine ausgehenden Übergänge) und lösen daher
    /// den Abschluss aus. Liefert die Frage-Ids über <paramref name="ids"/> zurück.
    /// </summary>
    public static Dialog BuildBranchingDialog(Guid dialogId, out BranchingDialogIds ids)
    {
        var roleQuestionId = Guid.NewGuid();
        var devQuestionId = Guid.NewGuid();
        var pmQuestionId = Guid.NewGuid();
        ids = new BranchingDialogIds(roleQuestionId, devQuestionId, pmQuestionId);

        return new Dialog
        {
            Id = dialogId,
            Key = "branching",
            Name = "Branching",
            Version = 1,
            IsPublished = true,
            StartQuestionId = roleQuestionId,
            CreatedAt = SampleTime,
            UpdatedAt = SampleTime,
            Questions =
            {
                new Question
                {
                    Id = roleQuestionId, DialogId = dialogId, Key = "role", Text = "Welche Rolle?",
                    Type = QuestionType.SingleChoice, Order = 0, IsRequired = true,
                    Options =
                    {
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "dev", Label = "Entwickler", Value = "dev", Order = 0 },
                        new AnswerOption { Id = Guid.NewGuid(), QuestionId = roleQuestionId, Key = "pm", Label = "Product Manager", Value = "pm", Order = 1 },
                    },
                },
                new Question
                {
                    Id = devQuestionId, DialogId = dialogId, Key = "devDetail",
                    Text = "Welche Programmiersprache?", Type = QuestionType.FreeText, Order = 1,
                },
                new Question
                {
                    Id = pmQuestionId, DialogId = dialogId, Key = "pmDetail",
                    Text = "Welches Produkt?", Type = QuestionType.FreeText, Order = 2,
                },
            },
            Transitions =
            {
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = roleQuestionId,
                    Expression = "role == \"dev\"", TargetQuestionId = devQuestionId, Priority = 0, IsDefault = false,
                },
                new Transition
                {
                    Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = roleQuestionId,
                    TargetQuestionId = pmQuestionId, Priority = 1, IsDefault = true,
                },
            },
        };
    }
}

/// <summary>
/// Die Frage-Ids des von <see cref="TestDialogFactory.BuildBranchingDialog"/> erzeugten Dialogs.
/// </summary>
/// <param name="RoleQuestionId">Die Start-/Auswahlfrage <c>role</c>.</param>
/// <param name="DevQuestionId">Die Ziel-Frage <c>devDetail</c> des bedingten Übergangs.</param>
/// <param name="PmQuestionId">Die Ziel-Frage <c>pmDetail</c> des Default-Übergangs.</param>
internal sealed record BranchingDialogIds(Guid RoleQuestionId, Guid DevQuestionId, Guid PmQuestionId);
