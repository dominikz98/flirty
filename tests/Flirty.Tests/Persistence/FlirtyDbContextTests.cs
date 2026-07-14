using Flirty.Domain;
using Flirty.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifiziert die EF-Core-Konfiguration aus Issue #18 gegen eine echte SQLite-Datenbank
/// (in-memory): Schema-Erzeugung, Aggregat-Round-Trips inkl. Navigationen, Enum-als-int-Storage,
/// JSON-tragende Textspalten, den eindeutigen Index über <c>(Key, Version)</c>, das kaskadierende
/// Löschen sowie die Abwesenheit ungewollter Shadow-Fremdschlüssel.
/// </summary>
public sealed class FlirtyDbContextTests : IDisposable
{
    // Deterministischer, UTC-normalisierter Zeitstempel (Npgsql verlangt in #19 Offset == UTC).
    private static readonly DateTimeOffset SampleTime = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public FlirtyDbContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>Schließt die Verbindung und verwirft damit die in-memory-Datenbank.</summary>
    public void Dispose() => _connection.Dispose();

    private FlirtyDbContext CreateContext() => new(_options);

    [Fact]
    public void Dialog_Aggregat_mit_allen_Kindern_wird_persistiert_und_geladen()
    {
        var dialogId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            context.Dialogs.Add(BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var loaded = context.Dialogs
                .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
                .Include(dialog => dialog.Transitions)
                .Include(dialog => dialog.Loops)
                .Include(dialog => dialog.Triggers)
                .Single(dialog => dialog.Id == dialogId);

            Assert.Equal("onboarding", loaded.Key);
            var question = Assert.Single(loaded.Questions);
            Assert.Equal(2, question.Options.Count);
            Assert.Single(loaded.Transitions);
            Assert.Single(loaded.Loops);
            Assert.Single(loaded.Triggers);
            // Der navigationslose Guid-Verweis wird als einfacher Wert erhalten (kein FK-Zwang).
            Assert.Equal(question.Id, loaded.StartQuestionId);
        }
    }

    [Fact]
    public void Session_haelt_mehrere_Antworten_pro_Frage_je_Iteration()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var loopInstanceId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            context.DialogSessions.Add(new DialogSession
            {
                Id = sessionId,
                DialogId = Guid.NewGuid(),
                DialogVersion = 1,
                ExternalUserKey = "user-42",
                Status = SessionStatus.InProgress,
                CurrentQuestionId = questionId,
                StartedAt = SampleTime,
                Answers =
                {
                    new SessionAnswer
                    {
                        Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = questionId,
                        Value = "\"A\"", AnsweredAt = SampleTime, Sequence = 0,
                        LoopInstanceId = loopInstanceId, IterationIndex = 0,
                    },
                    new SessionAnswer
                    {
                        Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = questionId,
                        Value = "\"B\"", AnsweredAt = SampleTime, Sequence = 1,
                        LoopInstanceId = loopInstanceId, IterationIndex = 1,
                    },
                },
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var session = context.DialogSessions
                .Include(entry => entry.Answers)
                .Single(entry => entry.Id == sessionId);

            // Zwei Antworten auf dieselbe Frage – unterschieden nur über den Iterationsindex.
            Assert.Equal(2, session.Answers.Count);
            Assert.All(session.Answers, answer => Assert.Equal(questionId, answer.QuestionId));
            Assert.Equal([0, 1], session.Answers.Select(answer => answer.IterationIndex).Order());
        }
    }

    [Fact]
    public void Enums_werden_als_int_gemappt()
    {
        using var context = CreateContext();

        Assert.Equal(typeof(int), ProviderTypeOf<Question>(context, nameof(Question.Type)));
        Assert.Equal(typeof(int), ProviderTypeOf<TriggerDefinition>(context, nameof(TriggerDefinition.Scope)));
        Assert.Equal(typeof(int), ProviderTypeOf<TriggerDefinition>(context, nameof(TriggerDefinition.Kind)));
        Assert.Equal(typeof(int), ProviderTypeOf<DialogSession>(context, nameof(DialogSession.Status)));
    }

    [Fact]
    public void Enum_wird_als_int_in_der_Datenbank_gespeichert()
    {
        var dialogId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            var dialog = BuildFullDialog(dialogId, out var questionId);
            dialog.Questions.Single(question => question.Id == questionId).Type = QuestionType.Number;
            context.Dialogs.Add(dialog);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var entityType = readContext.Model.FindEntityType(typeof(Question))!;
        var table = entityType.GetTableName()!;
        var column = entityType.FindProperty(nameof(Question.Type))!.GetColumnName();

        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT \"{column}\" FROM \"{table}\"";
        var raw = Convert.ToInt64(command.ExecuteScalar());

        Assert.Equal((long)(int)QuestionType.Number, raw);
    }

    [Fact]
    public void JSON_Spalten_erhalten_den_Rohtext_unveraendert()
    {
        var dialogId = Guid.NewGuid();
        const string validationJson = "{\"minLength\":1,\"pattern\":\"^[a-z]+$\"}";
        const string triggerJson = "{\"url\":\"https://example.test/hook\",\"retries\":3}";

        using (var context = CreateContext())
        {
            var dialog = BuildFullDialog(dialogId, out var questionId);
            dialog.Questions.Single(question => question.Id == questionId).ValidationRules = validationJson;
            dialog.Triggers.Single().Config = triggerJson;
            context.Dialogs.Add(dialog);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var question = context.Set<Question>().Single();
            var trigger = context.Set<TriggerDefinition>().Single();

            Assert.Equal(validationJson, question.ValidationRules);
            Assert.Equal(triggerJson, trigger.Config);
        }
    }

    [Fact]
    public void Doppelter_Key_und_Version_verletzt_den_eindeutigen_Index()
    {
        using var context = CreateContext();

        context.Dialogs.Add(NewDialog("duplicate", version: 1, name: "Erster"));
        context.SaveChanges();

        // Andere Version desselben Key ist erlaubt.
        context.Dialogs.Add(NewDialog("duplicate", version: 2, name: "Zweiter"));
        context.SaveChanges();

        // Gleicher Key UND gleiche Version verletzt den Unique-Index.
        context.Dialogs.Add(NewDialog("duplicate", version: 1, name: "Kollision"));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void Loeschen_des_Dialogs_entfernt_alle_Kind_Entities_kaskadierend()
    {
        var dialogId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            context.Dialogs.Add(BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var dialog = context.Dialogs
                .Include(entry => entry.Questions).ThenInclude(question => question.Options)
                .Include(entry => entry.Transitions)
                .Include(entry => entry.Loops)
                .Include(entry => entry.Triggers)
                .Single(entry => entry.Id == dialogId);

            context.Dialogs.Remove(dialog);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Empty(context.Dialogs);
            Assert.Empty(context.Set<Question>());
            Assert.Empty(context.Set<AnswerOption>());
            Assert.Empty(context.Set<Transition>());
            Assert.Empty(context.Set<LoopDefinition>());
            Assert.Empty(context.Set<TriggerDefinition>());
        }
    }

    [Fact]
    public void Skalare_Guid_Verweise_erzeugen_keine_Fremdschluessel()
    {
        using var context = CreateContext();

        // Transition hat genau EINEN Fremdschlüssel (zum Dialog); FromQuestionId/TargetQuestionId sind skalar.
        Assert.Single(context.Model.FindEntityType(typeof(Transition))!.GetForeignKeys());
        // LoopDefinition ebenso: nur der Dialog-FK, EntryQuestionId/BreakingQuestionId bleiben skalar.
        Assert.Single(context.Model.FindEntityType(typeof(LoopDefinition))!.GetForeignKeys());
    }

    private static Type? ProviderTypeOf<TEntity>(FlirtyDbContext context, string propertyName)
        => context.Model.FindEntityType(typeof(TEntity))!.FindProperty(propertyName)!.GetProviderClrType();

    private static Dialog NewDialog(string key, int version, string name) => new()
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
    private static Dialog BuildFullDialog(Guid dialogId, out Guid questionId)
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
}
