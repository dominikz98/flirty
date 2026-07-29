using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert die Dialog-Versionierung: das Ableiten einer neuen Version
/// (<see cref="CreateDialogVersionCommandHandler"/>), die Unveränderlichkeit einer veröffentlichten
/// Version (<see cref="DialogEditGuard"/>) und – als <b>Kernprobe</b> – dass eine laufende Session von
/// einer neu veröffentlichten Version unberührt zu Ende läuft. Genau das versprechen
/// <c>docs/RUNTIME.md</c> und <c>docs/ARCHITECTURE.md</c>; vorher hielt die Zusage nicht, weil es
/// überhaupt keinen Weg zu einer zweiten Version gab und Änderungen dieselbe Zeile trafen, aus der die
/// Session ihren Graphen lädt.
/// </summary>
public sealed class DialogVersioningTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Öffnet die SQLite-in-memory-Verbindung und legt das Schema an.</summary>
    public DialogVersioningTests()
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

    // ---- Neue Version ableiten --------------------------------------------------------------

    /// <summary>
    /// Die Kopie trägt denselben Schlüssel, die nächste Versionsnummer und ist ein <b>Entwurf</b> –
    /// zwei veröffentlichte Versionen wären für <c>StartDialogCommand</c> nicht eindeutig.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_legt_die_Folgeversion_als_Entwurf_an()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out _));

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        Assert.Equal("onboarding", copy.Dialog.Key);
        Assert.Equal(2, copy.Dialog.Version);
        Assert.False(copy.Dialog.IsPublished);
        Assert.NotEqual(dialogId, copy.Dialog.Id);

        // Die Quelle bleibt unverändert veröffentlicht.
        using var assert = CreateContext();
        var source = await assert.Dialogs.AsNoTracking().FirstAsync(dialog => dialog.Id == dialogId);
        Assert.True(source.IsPublished);
        Assert.Equal(1, source.Version);
    }

    /// <summary>
    /// Der gesamte Graph wird kopiert – mit durchgängig <b>neuen</b> Ids, damit die Quelle unberührt
    /// bleibt.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_klont_den_gesamten_Graphen_mit_neuen_Ids()
    {
        var dialogId = Guid.NewGuid();
        var source = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        Seed(source);

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        var question = Assert.Single(copy.Questions);
        Assert.Equal("role", question.Key);
        Assert.NotEqual(questionId, question.Id);
        Assert.Equal(copy.Dialog.Id, question.DialogId);
        Assert.Equal("{\"maxLength\":50}", question.ValidationRules);
        Assert.Equal(2, question.Options.Count);
        Assert.All(question.Options, option => Assert.Equal(question.Id, option.QuestionId));
        Assert.Equal(["dev", "pm"], question.Options.Select(option => option.Value));

        Assert.Single(copy.Transitions);
        Assert.Single(copy.Loops);
        var trigger = Assert.Single(copy.Triggers);
        Assert.Equal("{\"url\":\"https://example.test/hook\"}", trigger.Config);

        // Kein einziges Kind teilt seine Id mit der Quelle.
        using var assert = CreateContext();
        var sourceIds = await assert.Set<Question>().AsNoTracking()
            .Where(entity => entity.DialogId == dialogId).Select(entity => entity.Id).ToListAsync();
        Assert.DoesNotContain(question.Id, sourceIds);
    }

    /// <summary>
    /// Alle Frage-Verweise (Einstiegsfrage, Übergänge, Schleifen-Marker, Trigger) zeigen nach dem Klonen
    /// auf die <b>Kopien</b>. Ohne diese Umschreibung zeigte die neue Version auf die Fragen der alten –
    /// der Dialog wäre unbrauchbar.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_schreibt_die_Frage_Verweise_auf_die_Kopien_um()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildLoopDialog(dialogId, out var ids));

        // Ein Trigger mit Frage-Bezug, damit auch dieser Verweis geprüft wird.
        using (var arrange = CreateContext())
        {
            arrange.Set<TriggerDefinition>().Add(new TriggerDefinition
            {
                Id = Guid.NewGuid(),
                DialogId = dialogId,
                Scope = TriggerScope.AfterQuestion,
                QuestionId = ids.MoreQuestionId,
                Kind = TriggerKind.InProcess,
                Config = "{}",
            });
            arrange.SaveChanges();
        }

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        var copiedIds = copy.Questions.Select(question => question.Id).ToHashSet();
        var position = copy.Questions.Single(question => question.Key == "position");
        var more = copy.Questions.Single(question => question.Key == "more");

        Assert.Equal(position.Id, copy.Dialog.StartQuestionId);
        Assert.All(copy.Transitions, transition =>
        {
            Assert.Contains(transition.FromQuestionId, copiedIds);
            Assert.Contains(transition.TargetQuestionId, copiedIds);
        });

        var loop = Assert.Single(copy.Loops);
        Assert.Equal("positions", loop.CollectionKey);
        Assert.Equal(position.Id, loop.EntryQuestionId);
        Assert.Equal(more.Id, loop.BreakingQuestionId);

        var trigger = copy.Triggers.Single(entry => entry.Scope == TriggerScope.AfterQuestion);
        Assert.Equal(more.Id, trigger.QuestionId);

        // Der Zyklus ist erhalten: Rücksprung von 'more' auf 'position' samt Bedingung.
        Assert.Contains(
            copy.Transitions,
            transition => transition.FromQuestionId == more.Id
                       && transition.TargetQuestionId == position.Id
                       && transition.Expression == "more == \"yes\"");
    }

    /// <summary>Mehrfaches Ableiten zählt weiter (Version 2, dann 3).</summary>
    [Fact]
    public async Task CreateDialogVersion_zaehlt_die_hoechste_Version_weiter()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out _));

        DialogDetail zweite;
        DialogDetail dritte;
        using (var act = CreateContext())
        {
            var handler = new CreateDialogVersionCommandHandler(new DialogAdminStore(act));
            zweite = await handler.Handle(new CreateDialogVersionCommand(dialogId), default);
            dritte = await handler.Handle(new CreateDialogVersionCommand(zweite.Dialog.Id), default);
        }

        Assert.Equal(2, zweite.Dialog.Version);
        Assert.Equal(3, dritte.Dialog.Version);
    }

    /// <summary>Ein unbekannter Dialog wird als Not-Found gemeldet.</summary>
    [Fact]
    public async Task CreateDialogVersion_meldet_unbekannten_Dialog()
    {
        using var act = CreateContext();
        var handler = new CreateDialogVersionCommandHandler(new DialogAdminStore(act));

        await Assert.ThrowsAsync<ConfigurationNotFoundException>(
            async () => await handler.Handle(new CreateDialogVersionCommand(Guid.NewGuid()), default));
    }

    // ---- Sperre der veröffentlichten Version ------------------------------------------------

    /// <summary>
    /// Jede Graph-Änderung an einer veröffentlichten Version wird abgelehnt. Geprüft wird je
    /// Element-Art ein Command – alle laufen über <see cref="DialogEditGuard"/>.
    /// </summary>
    [Fact]
    public async Task Graph_Aenderungen_an_veroeffentlichter_Version_werden_abgelehnt()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        var optionId = dialog.Questions.Single().Options.First().Id;
        var transitionId = dialog.Transitions.Single().Id;
        var loopId = dialog.Loops.Single().Id;
        var triggerId = dialog.Triggers.Single().Id;
        Seed(dialog);

        using var act = CreateContext();
        var store = new DialogAdminStore(act);

        await AssertPublishedAsync(() => new CreateQuestionCommandHandler(store)
            .Handle(new CreateQuestionCommand(dialogId, "neu", "Neu?", QuestionType.FreeText, 9, false, null), default));
        await AssertPublishedAsync(() => new UpdateQuestionCommandHandler(store)
            .Handle(new UpdateQuestionCommand(dialogId, questionId, "role", "Geändert?", QuestionType.FreeText, 0, true, null), default));
        await AssertPublishedAsync(() => new DeleteQuestionCommandHandler(store)
            .Handle(new DeleteQuestionCommand(dialogId, questionId), default));
        await AssertPublishedAsync(() => new CreateAnswerOptionCommandHandler(store)
            .Handle(new CreateAnswerOptionCommand(dialogId, questionId, "x", "X", "x", 2), default));
        await AssertPublishedAsync(() => new UpdateAnswerOptionCommandHandler(store)
            .Handle(new UpdateAnswerOptionCommand(dialogId, questionId, optionId, "dev", "Dev", "dev", 0), default));
        await AssertPublishedAsync(() => new DeleteAnswerOptionCommandHandler(store)
            .Handle(new DeleteAnswerOptionCommand(dialogId, questionId, optionId), default));
        await AssertPublishedAsync(() => new CreateTransitionCommandHandler(store)
            .Handle(new CreateTransitionCommand(dialogId, questionId, questionId, null, 0, true), default));
        await AssertPublishedAsync(() => new UpdateTransitionCommandHandler(store)
            .Handle(new UpdateTransitionCommand(dialogId, transitionId, questionId, questionId, null, 0, true), default));
        await AssertPublishedAsync(() => new DeleteTransitionCommandHandler(store)
            .Handle(new DeleteTransitionCommand(dialogId, transitionId), default));
        await AssertPublishedAsync(() => new CreateLoopCommandHandler(store)
            .Handle(new CreateLoopCommand(dialogId, "weitere", questionId, questionId), default));
        await AssertPublishedAsync(() => new UpdateLoopCommandHandler(store)
            .Handle(new UpdateLoopCommand(dialogId, loopId, "positions", questionId, questionId), default));
        await AssertPublishedAsync(() => new DeleteLoopCommandHandler(store)
            .Handle(new DeleteLoopCommand(dialogId, loopId), default));
        await AssertPublishedAsync(() => new CreateTriggerCommandHandler(store)
            .Handle(new CreateTriggerCommand(dialogId, TriggerScope.OnDialogStarted, null, TriggerKind.InProcess, "{}", null), default));
        await AssertPublishedAsync(() => new UpdateTriggerCommandHandler(store)
            .Handle(new UpdateTriggerCommand(dialogId, triggerId, TriggerScope.OnDialogStarted, null, TriggerKind.InProcess, "{}", null), default));
        await AssertPublishedAsync(() => new DeleteTriggerCommandHandler(store)
            .Handle(new DeleteTriggerCommand(dialogId, triggerId), default));
    }

    /// <summary>
    /// Dieselben Änderungen greifen am <b>Entwurf</b> – die Sperre hängt am Veröffentlichungsstatus,
    /// nicht an der Version.
    /// </summary>
    [Fact]
    public async Task Graph_Aenderungen_am_Entwurf_greifen()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out _);
        dialog.IsPublished = false;
        Seed(dialog);

        using var act = CreateContext();
        var created = await new CreateQuestionCommandHandler(new DialogAdminStore(act))
            .Handle(new CreateQuestionCommand(dialogId, "neu", "Neu?", QuestionType.FreeText, 9, false, null), default);

        Assert.Equal("neu", created.Key);
    }

    /// <summary>
    /// Name und Beschreibung bleiben auch an einer veröffentlichten Version änderbar (rein beschreibend),
    /// der Wechsel der <b>Einstiegsfrage</b> nicht (Teil des Ablaufs).
    /// </summary>
    [Fact]
    public async Task UpdateDialog_erlaubt_Metadaten_und_sperrt_die_Einstiegsfrage()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out var questionId));

        using var act = CreateContext();
        var handler = new UpdateDialogCommandHandler(new DialogAdminStore(act));

        var renamed = await handler.Handle(
            new UpdateDialogCommand(dialogId, "onboarding", "Neuer Name", "Neue Beschreibung", questionId), default);
        Assert.Equal("Neuer Name", renamed.Name);

        await AssertPublishedAsync(() => handler.Handle(
            new UpdateDialogCommand(dialogId, "onboarding", "Neuer Name", null, null), default));
    }

    /// <summary>
    /// Der Schlüssel identifiziert die Dialog-Familie. Ihn nur an einer von mehreren Versionen zu
    /// ändern, würde die Reihe zerreißen – das wird abgelehnt.
    /// </summary>
    [Fact]
    public async Task UpdateDialog_lehnt_Umbenennung_bei_mehreren_Versionen_ab()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        dialog.IsPublished = false;
        Seed(dialog);

        using var act = CreateContext();
        var store = new DialogAdminStore(act);
        await new CreateDialogVersionCommandHandler(store)
            .Handle(new CreateDialogVersionCommand(dialogId), default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new UpdateDialogCommandHandler(store).Handle(
                new UpdateDialogCommand(dialogId, "anders", "Onboarding", null, questionId), default));

        Assert.Contains("multiple versions", exception.Message);
    }

    /// <summary>Ein einzelner Dialog lässt sich weiterhin umbenennen.</summary>
    [Fact]
    public async Task UpdateDialog_erlaubt_Umbenennung_bei_einer_einzigen_Version()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildFullDialog(dialogId, out var questionId);
        dialog.IsPublished = false;
        Seed(dialog);

        using var act = CreateContext();
        var updated = await new UpdateDialogCommandHandler(new DialogAdminStore(act)).Handle(
            new UpdateDialogCommand(dialogId, "anders", "Onboarding", null, questionId), default);

        Assert.Equal("anders", updated.Key);
    }

    // ---- Veröffentlichen ---------------------------------------------------------------------

    /// <summary>
    /// Je Schlüssel ist höchstens eine Version produktiv: Das Veröffentlichen der neuen Version zieht die
    /// bisherige zurück (sonst trüge sie einen Status, der nichts mehr bewirkt).
    /// </summary>
    [Fact]
    public async Task Publish_zieht_die_vorherige_Version_zurueck()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out _));

        Guid secondId;
        using (var act = CreateContext())
        {
            var store = new DialogAdminStore(act);
            var copy = await new CreateDialogVersionCommandHandler(store)
                .Handle(new CreateDialogVersionCommand(dialogId), default);
            secondId = copy.Dialog.Id;

            await new PublishDialogCommandHandler(store).Handle(new PublishDialogCommand(secondId), default);
        }

        using var assert = CreateContext();
        var versions = await assert.Dialogs.AsNoTracking()
            .Where(dialog => dialog.Key == "onboarding")
            .ToDictionaryAsync(dialog => dialog.Version, dialog => dialog.IsPublished);

        Assert.False(versions[1]);
        Assert.True(versions[2]);
    }

    /// <summary>
    /// <b>Kernprobe der Zusage:</b> Eine laufende Session der Version 1 läuft unverändert zu Ende,
    /// während Version 2 abgeleitet, geändert und veröffentlicht wird. Ein neuer Anwender startet
    /// dagegen auf Version 2.
    /// </summary>
    [Fact]
    public async Task Laufende_Session_ueberlebt_eine_neu_veroeffentlichte_Version()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));

        // 1. Session auf Version 1 starten (steht auf der Auswahlfrage 'role').
        StartDialogResult start;
        using (var act = CreateContext())
        {
            start = await new StartDialogCommandHandler(new DialogStore(act), new SpyPublisher())
                .Handle(new StartDialogCommand("branching", "user-1"), default);
        }

        Assert.Equal("role", start.CurrentQuestion!.Key);

        // 2. Version 2 ableiten, dort die Auswahlfrage umbauen und veröffentlichen.
        Guid secondId;
        using (var act = CreateContext())
        {
            var store = new DialogAdminStore(act);
            var copy = await new CreateDialogVersionCommandHandler(store)
                .Handle(new CreateDialogVersionCommand(dialogId), default);
            secondId = copy.Dialog.Id;

            var roleCopy = copy.Questions.Single(question => question.Key == "role");
            await new UpdateQuestionCommandHandler(store).Handle(
                new UpdateQuestionCommand(
                    secondId, roleCopy.Id, "role", "Ganz andere Frage?", QuestionType.FreeText, 0, true, null),
                default);

            await new PublishDialogCommandHandler(store).Handle(new PublishDialogCommand(secondId), default);
        }

        // 3. Die laufende Session antwortet weiter – auf ihrem alten Graphen.
        SubmitAnswerResult submit;
        using (var act = CreateContext())
        {
            submit = await new SubmitAnswerCommandHandler(
                    new DialogStore(act), new DynamicExpressoExpressionEvaluator(), new SpyPublisher())
                .Handle(new SubmitAnswerCommand(start.SessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.Equal("devDetail", submit.NextQuestion!.Key);

        // Auch lesend ist die Session unverändert erreichbar (das war vorher der 409-Fall).
        ResumeDialogResult resumed;
        using (var act = CreateContext())
        {
            resumed = await new ResumeDialogQueryHandler(new DialogStore(act))
                .Handle(new ResumeDialogQuery(start.SessionId), default);
        }

        Assert.Equal(SessionStatus.InProgress, resumed.Status);
        Assert.Equal("devDetail", resumed.CurrentQuestion!.Key);

        // 4. Ein neuer Anwender landet auf Version 2 – mit dem geänderten Fragetext.
        StartDialogResult neu;
        using (var act = CreateContext())
        {
            neu = await new StartDialogCommandHandler(new DialogStore(act), new SpyPublisher())
                .Handle(new StartDialogCommand("branching", "user-2"), default);
        }

        Assert.Equal("Ganz andere Frage?", neu.CurrentQuestion!.Text);
    }

    // ---- Löschen und Sessions beenden --------------------------------------------------------

    /// <summary>
    /// Solange Sessions laufen, wird das Löschen abgelehnt – sie überlebten den Dialog als unlesbare
    /// Waisen.
    /// </summary>
    [Fact]
    public async Task DeleteDialog_wird_bei_laufender_Session_abgelehnt()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.InProgress);

        using var act = CreateContext();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new DeleteDialogCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteDialogCommand(dialogId), default));

        Assert.Contains("1 session(s)", exception.Message);
    }

    /// <summary>Abgeschlossene und abgebrochene Sessions blockieren das Löschen nicht.</summary>
    [Fact]
    public async Task DeleteDialog_greift_bei_beendeten_Sessions()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.Completed);
        SeedSession(dialogId, SessionStatus.Abandoned);

        using (var act = CreateContext())
        {
            await new DeleteDialogCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteDialogCommand(dialogId), default);
        }

        using var assert = CreateContext();
        Assert.Empty(await assert.Dialogs.AsNoTracking().Where(dialog => dialog.Id == dialogId).ToListAsync());
    }

    /// <summary>
    /// Der Abbruch beendet die laufenden Sessions (Antworten bleiben erhalten) und macht damit den Weg
    /// zum Löschen frei.
    /// </summary>
    [Fact]
    public async Task AbandonDialogSessions_beendet_die_Sessions_und_gibt_das_Loeschen_frei()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.InProgress);
        SeedSession(dialogId, SessionStatus.InProgress);

        AbandonSessionsResult result;
        using (var act = CreateContext())
        {
            result = await new AbandonDialogSessionsCommandHandler(new DialogAdminStore(act))
                .Handle(new AbandonDialogSessionsCommand(dialogId), default);
        }

        Assert.Equal(2, result.AbandonedSessions);

        using (var assert = CreateContext())
        {
            var sessions = await assert.DialogSessions.AsNoTracking()
                .Where(session => session.DialogId == dialogId).ToListAsync();
            Assert.All(sessions, session => Assert.Equal(SessionStatus.Abandoned, session.Status));
            Assert.All(sessions, session => Assert.NotNull(session.CompletedAt));
        }

        using (var act = CreateContext())
        {
            await new DeleteDialogCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteDialogCommand(dialogId), default);
        }

        using var assertDeleted = CreateContext();
        Assert.Empty(await assertDeleted.Dialogs.AsNoTracking().Where(dialog => dialog.Id == dialogId).ToListAsync());
    }

    /// <summary>Ohne laufende Sessions ist der Abbruch ein No-Op.</summary>
    [Fact]
    public async Task AbandonDialogSessions_meldet_null_wenn_keine_Session_laeuft()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));

        using var act = CreateContext();
        var result = await new AbandonDialogSessionsCommandHandler(new DialogAdminStore(act))
            .Handle(new AbandonDialogSessionsCommand(dialogId), default);

        Assert.Equal(0, result.AbandonedSessions);
    }

    /// <summary>Die Zähl-Query berücksichtigt nur laufende Sessions.</summary>
    [Fact]
    public async Task CountActiveSessions_zaehlt_nur_laufende_Sessions()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        SeedSession(dialogId, SessionStatus.InProgress);
        SeedSession(dialogId, SessionStatus.Completed);
        SeedSession(dialogId, SessionStatus.Abandoned);

        using var act = CreateContext();
        var count = await new CountActiveSessionsQueryHandler(new DialogAdminStore(act))
            .Handle(new CountActiveSessionsQuery(dialogId), default);

        Assert.Equal(1, count);
    }

    // ---- Hilfen ------------------------------------------------------------------------------

    private void Seed(Dialog dialog)
    {
        using var arrange = CreateContext();
        arrange.Dialogs.Add(dialog);
        arrange.SaveChanges();
    }

    private void SeedSession(Guid dialogId, SessionStatus status)
    {
        using var arrange = CreateContext();
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = Guid.NewGuid(),
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = $"user-{Guid.NewGuid():N}",
            Status = status,
            StartedAt = TestDialogFactory.SampleTime,
            CompletedAt = status == SessionStatus.InProgress ? null : TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();
    }

    private static async Task AssertPublishedAsync<TResult>(Func<ValueTask<TResult>> operation)
    {
        var exception = await Assert.ThrowsAsync<DialogPublishedException>(async () => await operation());
        Assert.Contains("published", exception.Message);
    }
}
