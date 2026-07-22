using Flirty.Domain;
using Flirty.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifiziert das <see cref="IDialogStore"/>-Repository (Issue #21) gegen eine echte SQLite-Datenbank
/// (in-memory): das Laden veröffentlichter bzw. gepinnter Dialog-Graphen (ungetrackt), das getrackte
/// Laden von Sessions samt Antworten, den Aktiv-Session-Filter, die Trigger-Abfrage je Session und
/// Zeitpunkt (#42), die Unit-of-Work-Naht (<see cref="IDialogStore.AddSession"/> +
/// <see cref="IDialogStore.SaveChangesAsync"/>) sowie die DI-Registrierung.
/// </summary>
public sealed class DialogStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public DialogStoreTests()
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

    // ---- GetPublishedDialogAsync ------------------------------------------------------------

    /// <summary>Bei mehreren veröffentlichten Versionen liefert der Store die höchste.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_liefert_hoechste_publizierte_Version()
    {
        using (var context = CreateContext())
        {
            context.Dialogs.Add(PublishedDialog("survey", version: 1));
            context.Dialogs.Add(PublishedDialog("survey", version: 2));
            context.Dialogs.Add(UnpublishedDialog("survey", version: 3));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("survey");

        Assert.NotNull(dialog);
        Assert.Equal(2, dialog.Version);
        Assert.True(dialog.IsPublished);
    }

    /// <summary>Existieren nur unveröffentlichte Versionen, liefert der Store <c>null</c>.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_ignoriert_unpublizierte_Dialoge()
    {
        using (var context = CreateContext())
        {
            context.Dialogs.Add(UnpublishedDialog("draft", version: 1));
            context.Dialogs.Add(UnpublishedDialog("draft", version: 2));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("draft");

        Assert.Null(dialog);
    }

    /// <summary>Ein unbekannter Schlüssel liefert <c>null</c> statt einer Ausnahme.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_unbekannter_Key_liefert_null()
    {
        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("does-not-exist");

        Assert.Null(dialog);
    }

    /// <summary>Der vollständige Konfigurationsgraph (Fragen/Optionen, Übergänge, Schleifen, Trigger) wird geladen.</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_laedt_vollstaendigen_Graphen()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetPublishedDialogAsync("onboarding");

        Assert.NotNull(dialog);
        var question = Assert.Single(dialog.Questions);
        Assert.Equal(2, question.Options.Count);
        Assert.Single(dialog.Transitions);
        Assert.Single(dialog.Loops);
        Assert.Single(dialog.Triggers);
    }

    /// <summary>Der Dialog-Graph wird ungetrackt geliefert (Change-Tracker bleibt leer).</summary>
    [Fact]
    public async Task GetPublishedDialogAsync_liefert_ungetrackten_Graphen()
    {
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(Guid.NewGuid(), out _));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        _ = await new DialogStore(readContext).GetPublishedDialogAsync("onboarding");

        Assert.Empty(readContext.ChangeTracker.Entries());
    }

    // ---- GetDialogAsync ---------------------------------------------------------------------

    /// <summary>Lädt genau die per Id gepinnte Version samt Graph, auch wenn weitere Versionen existieren.</summary>
    [Fact]
    public async Task GetDialogAsync_liefert_gepinnte_Version_mit_Graph()
    {
        var pinnedId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(pinnedId, out _)); // onboarding v1
            context.Dialogs.Add(PublishedDialog("onboarding", version: 2));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetDialogAsync(pinnedId);

        Assert.NotNull(dialog);
        Assert.Equal(pinnedId, dialog.Id);
        Assert.Equal(1, dialog.Version);
        Assert.Single(dialog.Questions);
        Assert.Single(dialog.Triggers);
    }

    /// <summary>Lädt per Id auch einen unveröffentlichten Dialog (kein <c>IsPublished</c>-Filter – Pinning-Vertrag).</summary>
    [Fact]
    public async Task GetDialogAsync_laedt_auch_unpublizierten_Dialog()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var dialog = UnpublishedDialog("draft", version: 1);
            dialog.Id = dialogId;
            context.Dialogs.Add(dialog);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var loaded = await new DialogStore(readContext).GetDialogAsync(dialogId);

        Assert.NotNull(loaded);
        Assert.False(loaded.IsPublished);
    }

    /// <summary>Eine unbekannte Id liefert <c>null</c>.</summary>
    [Fact]
    public async Task GetDialogAsync_unbekannte_Id_liefert_null()
    {
        using var readContext = CreateContext();
        var dialog = await new DialogStore(readContext).GetDialogAsync(Guid.NewGuid());

        Assert.Null(dialog);
    }

    /// <summary>Auch <see cref="IDialogStore.GetDialogAsync"/> liefert den Graphen ungetrackt.</summary>
    [Fact]
    public async Task GetDialogAsync_liefert_ungetrackten_Graphen()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        _ = await new DialogStore(readContext).GetDialogAsync(dialogId);

        Assert.Empty(readContext.ChangeTracker.Entries());
    }

    // ---- GetSessionAsync --------------------------------------------------------------------

    /// <summary>Lädt die Session samt ihrer Antworten.</summary>
    [Fact]
    public async Task GetSessionAsync_liefert_Session_mit_Antworten()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var session = NewSession(Guid.NewGuid(), "user-1", id: sessionId);
            session.Answers.Add(Answer(sessionId, questionId, "\"a\"", sequence: 0));
            session.Answers.Add(Answer(sessionId, questionId, "\"b\"", sequence: 1));
            context.DialogSessions.Add(session);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var loaded = await new DialogStore(readContext).GetSessionAsync(sessionId);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Answers.Count);
    }

    /// <summary>Die Session wird getrackt geliefert, damit spätere Mutationen persistiert werden können.</summary>
    [Fact]
    public async Task GetSessionAsync_liefert_getrackte_Session()
    {
        var sessionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(Guid.NewGuid(), "user-1", id: sessionId));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(EntityState.Unchanged, readContext.Entry(session).State);
    }

    /// <summary>Eine unbekannte Session-Id liefert <c>null</c>.</summary>
    [Fact]
    public async Task GetSessionAsync_unbekannte_Id_liefert_null()
    {
        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).GetSessionAsync(Guid.NewGuid());

        Assert.Null(session);
    }

    /// <summary>Mehrere Antworten auf dieselbe Frage (Loop-Iterationen) werden vollständig geladen.</summary>
    [Fact]
    public async Task GetSessionAsync_haelt_mehrere_Antworten_pro_Frage_je_Iteration()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var loopInstanceId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var seed = NewSession(Guid.NewGuid(), "user-1", id: sessionId);
            seed.Answers.Add(Answer(sessionId, questionId, "\"A\"", sequence: 0, loopInstanceId, iterationIndex: 0));
            seed.Answers.Add(Answer(sessionId, questionId, "\"B\"", sequence: 1, loopInstanceId, iterationIndex: 1));
            context.DialogSessions.Add(seed);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(2, session.Answers.Count);
        Assert.All(session.Answers, answer => Assert.Equal(questionId, answer.QuestionId));
        Assert.Equal([0, 1], session.Answers.Select(answer => answer.IterationIndex).Order());
    }

    // ---- FindActiveSessionAsync -------------------------------------------------------------

    /// <summary>Findet die laufende Session zu (DialogId, ExternalUserKey) samt Antworten.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_findet_laufende_Session_mit_Antworten()
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            var session = NewSession(dialogId, "user-1", id: sessionId);
            session.Answers.Add(Answer(sessionId, Guid.NewGuid(), "\"a\"", sequence: 0));
            context.DialogSessions.Add(session);
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.NotNull(found);
        Assert.Equal(sessionId, found.Id);
        Assert.Single(found.Answers);
    }

    /// <summary>Abgeschlossene bzw. abgebrochene Sessions gelten nicht als aktiv.</summary>
    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task FindActiveSessionAsync_ignoriert_nicht_laufende_Sessions(SessionStatus status)
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1", status));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.Null(found);
    }

    /// <summary>Der Filter unterscheidet Anwender über den <c>ExternalUserKey</c>.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_filtert_nach_ExternalUserKey()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1"));
            context.DialogSessions.Add(NewSession(dialogId, "user-2"));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-2");

        Assert.NotNull(found);
        Assert.Equal("user-2", found.ExternalUserKey);
    }

    /// <summary>Der Filter unterscheidet Dialoge über die <c>DialogId</c>.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_filtert_nach_DialogId()
    {
        var dialogA = Guid.NewGuid();
        var dialogB = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogA, "user-1"));
            context.DialogSessions.Add(NewSession(dialogB, "user-1"));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogB, "user-1");

        Assert.NotNull(found);
        Assert.Equal(dialogB, found.DialogId);
    }

    /// <summary>Ohne passende laufende Session liefert der Store <c>null</c>.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_ohne_Treffer_liefert_null()
    {
        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(Guid.NewGuid(), "user-1");

        Assert.Null(found);
    }

    /// <summary>Bei mehreren laufenden Sessions gewinnt die zuletzt gestartete.</summary>
    [Fact]
    public async Task FindActiveSessionAsync_liefert_neueste_bei_mehreren_laufenden()
    {
        var dialogId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1", startedAt: TestDialogFactory.SampleTime));
            context.DialogSessions.Add(NewSession(dialogId, "user-1",
                startedAt: TestDialogFactory.SampleTime.AddMinutes(5), id: newerId));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var found = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.NotNull(found);
        Assert.Equal(newerId, found.Id);
    }

    /// <summary>Auch die Aktiv-Session wird getrackt geliefert (Submit/Edit-Voraussetzung).</summary>
    [Fact]
    public async Task FindActiveSessionAsync_liefert_getrackte_Session()
    {
        var dialogId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(dialogId, "user-1"));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var session = await new DialogStore(readContext).FindActiveSessionAsync(dialogId, "user-1");

        Assert.NotNull(session);
        Assert.Equal(EntityState.Unchanged, readContext.Entry(session).State);
    }

    // ---- GetTriggersForSessionAsync (#42) ---------------------------------------------------

    /// <summary>
    /// Liefert genau die Trigger des Dialogs, an dem die Session hängt – und nur die des angefragten
    /// Zeitpunkts. Grundlage der Webhook-Auslieferung, die aus der Notification nur die SessionId kennt.
    /// </summary>
    [Fact]
    public async Task GetTriggersForSessionAsync_filtert_auf_Dialog_der_Session_und_Scope()
    {
        var dialogId = Guid.NewGuid();
        var fremderDialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            var dialog = TestDialogFactory.NewDialog("triggers", version: 1, name: "Triggers");
            dialog.Id = dialogId;
            dialog.Triggers.Add(NewTrigger(dialogId, TriggerScope.OnDialogCompleted, "https://example.test/fertig"));
            dialog.Triggers.Add(NewTrigger(dialogId, TriggerScope.AfterAnswer, "https://example.test/antwort"));

            var fremd = TestDialogFactory.NewDialog("andere", version: 1, name: "Andere");
            fremd.Id = fremderDialogId;
            fremd.Triggers.Add(
                NewTrigger(fremderDialogId, TriggerScope.OnDialogCompleted, "https://example.test/fremd"));

            context.Dialogs.Add(dialog);
            context.Dialogs.Add(fremd);
            context.DialogSessions.Add(NewSession(dialogId, "user-1", id: sessionId));
            context.SaveChanges();
        }

        using var readContext = CreateContext();
        var triggers = await new DialogStore(readContext)
            .GetTriggersForSessionAsync(sessionId, TriggerScope.OnDialogCompleted);

        var trigger = Assert.Single(triggers);
        Assert.Equal(dialogId, trigger.DialogId);
        Assert.Contains("fertig", trigger.Config, StringComparison.Ordinal);
    }

    /// <summary>Eine unbekannte Session liefert eine leere Liste statt einer Ausnahme.</summary>
    [Fact]
    public async Task GetTriggersForSessionAsync_unbekannte_Session_liefert_leere_Liste()
    {
        using var readContext = CreateContext();
        var triggers = await new DialogStore(readContext)
            .GetTriggersForSessionAsync(Guid.NewGuid(), TriggerScope.OnDialogCompleted);

        Assert.Empty(triggers);
    }

    // ---- AddSession + SaveChangesAsync (Unit of Work) ---------------------------------------

    /// <summary>Eine neu hinzugefügte Session wird samt Antworten erst mit <c>SaveChangesAsync</c> persistiert.</summary>
    [Fact]
    public async Task AddSession_und_SaveChangesAsync_persistiert_neue_Session()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        using (var context = CreateContext())
        {
            var store = new DialogStore(context);
            var session = NewSession(Guid.NewGuid(), "user-1", id: sessionId);
            session.CurrentQuestionId = questionId;
            session.Answers.Add(Answer(sessionId, questionId, "\"hello\"", sequence: 0));

            store.AddSession(session);
            await store.SaveChangesAsync();
        }

        using var readContext = CreateContext();
        var loaded = readContext.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal(questionId, loaded.CurrentQuestionId);
        Assert.Single(loaded.Answers);
    }

    /// <summary>Mutationen an einer getrackten Session (neue Antwort, Statuswechsel) werden gespeichert.</summary>
    [Fact]
    public async Task SaveChangesAsync_persistiert_Mutationen_einer_getrackten_Session()
    {
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.DialogSessions.Add(NewSession(Guid.NewGuid(), "user-1", id: sessionId));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var store = new DialogStore(context);
            var session = await store.GetSessionAsync(sessionId);
            Assert.NotNull(session);

            // Beim Anhängen an eine bereits getrackte Session die Id NICHT vorbelegen: der Guid-Key ist
            // store-generiert (EF-Konvention), eine vorbelegte Id an einem Kind eines getrackten Parents
            // würde als Update statt Insert interpretiert. EF vergibt den Key beim SaveChanges.
            session.Answers.Add(new SessionAnswer
            {
                SessionId = sessionId, QuestionId = questionId, Value = "\"done\"",
                AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
            });
            session.Status = SessionStatus.Completed;
            session.CompletedAt = TestDialogFactory.SampleTime.AddMinutes(10);
            session.CurrentQuestionId = null;

            await store.SaveChangesAsync();
        }

        using var readContext = CreateContext();
        var reloaded = readContext.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.Completed, reloaded.Status);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.Null(reloaded.CurrentQuestionId);
        Assert.Single(reloaded.Answers);
    }

    // ---- Konstruktor + DI -------------------------------------------------------------------

    /// <summary>Der Konstruktor lehnt einen <c>null</c>-Kontext ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Kontext()
        => Assert.Throws<ArgumentNullException>(() => new DialogStore(null!));

    /// <summary><c>AddFlirty()</c> registriert <see cref="IDialogStore"/> als scoped <see cref="DialogStore"/>.</summary>
    [Fact]
    public void AddFlirty_registriert_IDialogStore()
    {
        using var provider = new ServiceCollection()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDialogStore>();

        Assert.IsType<DialogStore>(store);
    }

    // ---- Testdaten-Helfer -------------------------------------------------------------------

    private static Dialog PublishedDialog(string key, int version)
    {
        var dialog = TestDialogFactory.NewDialog(key, version, name: $"{key} v{version}");
        dialog.IsPublished = true;
        return dialog;
    }

    private static Dialog UnpublishedDialog(string key, int version)
        => TestDialogFactory.NewDialog(key, version, name: $"{key} v{version} (Entwurf)");

    private static TriggerDefinition NewTrigger(Guid dialogId, TriggerScope scope, string url) => new()
    {
        Id = Guid.NewGuid(),
        DialogId = dialogId,
        Scope = scope,
        Kind = TriggerKind.Webhook,
        Config = $"{{\"url\":\"{url}\"}}",
    };

    private static DialogSession NewSession(
        Guid dialogId,
        string externalUserKey,
        SessionStatus status = SessionStatus.InProgress,
        DateTimeOffset? startedAt = null,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        DialogId = dialogId,
        DialogVersion = 1,
        ExternalUserKey = externalUserKey,
        Status = status,
        StartedAt = startedAt ?? TestDialogFactory.SampleTime,
    };

    private static SessionAnswer Answer(
        Guid sessionId,
        Guid questionId,
        string value,
        int sequence,
        Guid? loopInstanceId = null,
        int? iterationIndex = null) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        QuestionId = questionId,
        Value = value,
        AnsweredAt = TestDialogFactory.SampleTime,
        Sequence = sequence,
        LoopInstanceId = loopInstanceId,
        IterationIndex = iterationIndex,
    };
}
