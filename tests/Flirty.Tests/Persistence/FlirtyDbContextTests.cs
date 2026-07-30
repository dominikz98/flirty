using Flirty.Domain;
using Flirty.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifies the EF Core configuration from issue #18 against a real SQLite database (in-memory):
/// schema creation, aggregate round trips incl. navigations, enum-as-int storage, JSON-carrying text
/// columns, the unique index over <c>(Key, Version)</c>, the cascading delete as well as the absence
/// of unwanted shadow foreign keys.
/// </summary>
public sealed class FlirtyDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
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

    /// <summary>Closes the connection and thereby discards the in-memory database.</summary>
    public void Dispose() => _connection.Dispose();

    private FlirtyDbContext CreateContext() => new(_options);

    [Fact]
    public void Dialog_aggregate_with_all_children_is_persisted_and_loaded()
    {
        var dialogId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
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
            // The navigation-free Guid reference is preserved as a plain value (no FK constraint).
            Assert.Equal(question.Id, loaded.StartQuestionId);
        }
    }

    [Fact]
    public void Session_holds_several_answers_per_question_one_per_iteration()
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
                StartedAt = TestDialogFactory.SampleTime,
                Answers =
                {
                    new SessionAnswer
                    {
                        Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = questionId,
                        Value = "\"A\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
                        LoopInstanceId = loopInstanceId, IterationIndex = 0,
                    },
                    new SessionAnswer
                    {
                        Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = questionId,
                        Value = "\"B\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 1,
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

            // Two answers to the same question – told apart only by the iteration index.
            Assert.Equal(2, session.Answers.Count);
            Assert.All(session.Answers, answer => Assert.Equal(questionId, answer.QuestionId));
            Assert.Equal([0, 1], session.Answers.Select(answer => answer.IterationIndex).Order());
        }
    }

    [Fact]
    public void Enums_are_mapped_as_int()
    {
        using var context = CreateContext();

        Assert.Equal(typeof(int), ProviderTypeOf<Question>(context, nameof(Question.Type)));
        Assert.Equal(typeof(int), ProviderTypeOf<TriggerDefinition>(context, nameof(TriggerDefinition.Scope)));
        Assert.Equal(typeof(int), ProviderTypeOf<TriggerDefinition>(context, nameof(TriggerDefinition.Kind)));
        Assert.Equal(typeof(int), ProviderTypeOf<DialogSession>(context, nameof(DialogSession.Status)));
    }

    [Fact]
    public void Enum_is_stored_as_int_in_the_database()
    {
        var dialogId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
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
    public void JSON_columns_preserve_the_raw_text_unchanged()
    {
        var dialogId = Guid.NewGuid();
        const string validationJson = "{\"minLength\":1,\"pattern\":\"^[a-z]+$\"}";
        const string triggerJson = "{\"url\":\"https://example.test/hook\",\"retries\":3}";

        using (var context = CreateContext())
        {
            var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
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
    public void A_duplicate_key_and_version_violates_the_unique_index()
    {
        using var context = CreateContext();

        context.Dialogs.Add(TestDialogFactory.NewDialog("duplicate", version: 1, name: "First"));
        context.SaveChanges();

        // A different version of the same key is allowed.
        context.Dialogs.Add(TestDialogFactory.NewDialog("duplicate", version: 2, name: "Second"));
        context.SaveChanges();

        // The same key AND the same version violates the unique index.
        context.Dialogs.Add(TestDialogFactory.NewDialog("duplicate", version: 1, name: "Collision"));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void Deleting_the_dialog_removes_all_child_entities_by_cascade()
    {
        var dialogId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var dialog = context.Dialogs
                .Include(entry => entry.Questions).ThenInclude(question => question.Options)
                .Include(entry => entry.Transitions)
                .Include(entry => entry.Loops)
                .Include(entry => entry.Triggers)
                .Include(entry => entry.Layout)
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
            Assert.Empty(context.Set<DialogLayout>());
        }
    }

    [Fact]
    public void Scalar_Guid_references_create_no_foreign_keys()
    {
        using var context = CreateContext();

        // Transition has exactly ONE foreign key (to the dialog); FromQuestionId/TargetQuestionId are scalar.
        Assert.Single(context.Model.FindEntityType(typeof(Transition))!.GetForeignKeys());
        // LoopDefinition likewise: only the dialog FK, EntryQuestionId/BreakingQuestionId stay scalar.
        Assert.Single(context.Model.FindEntityType(typeof(LoopDefinition))!.GetForeignKeys());
        // DialogLayout likewise: only the dialog FK, ElementId stays scalar (#102).
        Assert.Single(context.Model.FindEntityType(typeof(DialogLayout))!.GetForeignKeys());
    }

    /// <summary>
    /// At most one position per element: the unique index over
    /// (<c>DialogId</c>, <c>ElementKind</c>, <c>ElementId</c>) prevents two contradicting rows – which
    /// one would apply could otherwise not be decided.
    /// </summary>
    [Fact]
    public void A_second_position_for_the_same_element_violates_the_unique_index()
    {
        var dialogId = Guid.NewGuid();

        using var context = CreateContext();
        context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out var questionId));
        context.SaveChanges();

        context.Set<DialogLayout>().Add(new DialogLayout
        {
            Id = Guid.NewGuid(),
            DialogId = dialogId,
            ElementKind = LayoutElementKind.Question,
            ElementId = questionId,
            X = 10,
            Y = 20,
        });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    private static Type? ProviderTypeOf<TEntity>(FlirtyDbContext context, string propertyName)
        => context.Model.FindEntityType(typeof(TEntity))!.FindProperty(propertyName)!.GetProviderClrType();
}
