using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert die Layout-Persistenz (#102): den Batch-Upsert
/// (<see cref="SetDialogLayoutCommandHandler"/>), das Zurücksetzen
/// (<see cref="ResetDialogLayoutCommandHandler"/>) und – als Kern der Stufe – die beiden
/// <b>Handarbeits-Zweige</b>, die man beim nächsten Elementtyp vergisst: das Mitklonen in
/// <see cref="CreateDialogVersionCommandHandler"/> und das Aufräumen in
/// <see cref="DeleteQuestionCommandHandler"/>.
/// </summary>
/// <remarks>
/// Die wichtigste Zusicherung ist <c>SetDialogLayout_aendert_auch_ein_veroeffentlichtes_Layout</c>:
/// Der Layout-Command läuft bewusst <b>nicht</b> unter <see cref="DialogEditGuard"/> (ADR 0007). Ohne
/// diesen Test wäre das eine Behauptung im Kommentar – und ein versehentlich ergänzter Guard fiele
/// erst im Browser auf.
/// </remarks>
public sealed class DialogLayoutTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Öffnet die SQLite-in-memory-Verbindung und legt das Schema an.</summary>
    public DialogLayoutTests()
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

    // ---- Setzen und Zurücksetzen --------------------------------------------------------------

    /// <summary>
    /// Ein zweiter Aufruf für dasselbe Element aktualisiert die Zeile, statt eine zweite anzulegen –
    /// sonst liefe der Unique-Index über (<c>DialogId</c>, <c>ElementKind</c>, <c>ElementId</c>) auf.
    /// </summary>
    [Fact]
    public async Task SetDialogLayout_legt_an_und_aktualisiert_bestehende_Positionen()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.RoleQuestionId, 100, 200)]), default);
        }

        IReadOnlyList<DialogLayoutDetail> result;
        using (var act = CreateContext())
        {
            result = await Handler(act).Handle(
                new SetDialogLayoutCommand(
                    dialogId, [Entry(ids.RoleQuestionId, 140, 260), Entry(ids.DevQuestionId, 400, 260)]),
                default);
        }

        // Die Antwort trägt das VOLLSTÄNDIGE Layout, damit der Aufrufer seinen Stand ersetzen kann.
        Assert.Equal(2, result.Count);

        using var assert = CreateContext();
        var rows = assert.Set<DialogLayout>().Where(row => row.DialogId == dialogId).ToList();

        Assert.Equal(2, rows.Count);
        var start = Assert.Single(rows, row => row.ElementId == ids.RoleQuestionId);
        Assert.Equal(140, start.X);
        Assert.Equal(260, start.Y);
    }

    /// <summary>
    /// Nicht genannte Elemente bleiben stehen: Eine Zieh-Geste verschiebt ein Element und darf nicht
    /// die Positionen aller übrigen verwerfen.
    /// </summary>
    [Fact]
    public async Task SetDialogLayout_laesst_nicht_genannte_Positionen_unangetastet()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.RoleQuestionId, 10, 20), Entry(ids.DevQuestionId, 30, 40)]),
                default);
        }

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.DevQuestionId, 300, 400)]), default);
        }

        using var assert = CreateContext();
        var rows = assert.Set<DialogLayout>().Where(row => row.DialogId == dialogId).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(10, Assert.Single(rows, row => row.ElementId == ids.RoleQuestionId).X);
        Assert.Equal(300, Assert.Single(rows, row => row.ElementId == ids.DevQuestionId).X);
    }

    /// <summary>
    /// <b>Die Zusage dieser Stufe:</b> Verschieben funktioniert auch bei veröffentlichtem Dialog und
    /// quittiert nicht mit einem Konflikt. Koordinaten sind kein Teil des Graphen (ADR 0007) – die
    /// Publish-Sperre aus ADR 0005 endet an dieser Tabelle.
    /// </summary>
    [Fact]
    public async Task SetDialogLayout_aendert_auch_ein_veroeffentlichtes_Layout()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out var questionId));

        using (var check = CreateContext())
        {
            // Vorbedingung: Der Dialog ist wirklich veröffentlicht – sonst prüfte der Test nichts.
            Assert.True(check.Dialogs.Single(dialog => dialog.Id == dialogId).IsPublished);
        }

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(questionId, 640, 480)]), default);
        }

        using var assert = CreateContext();
        var row = Assert.Single(assert.Set<DialogLayout>().Where(entry => entry.ElementId == questionId));
        Assert.Equal(640, row.X);
        Assert.Equal(480, row.Y);
    }

    /// <summary>
    /// Die Gegenprobe zur vorigen: Eine echte Graph-Änderung bleibt am selben veröffentlichten Dialog
    /// gesperrt. Ohne sie belegte der Layout-Test nur, dass irgendwo ein Guard fehlt.
    /// </summary>
    [Fact]
    public async Task Graph_Aenderung_bleibt_am_selben_veroeffentlichten_Dialog_gesperrt()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out var questionId));

        using var act = CreateContext();
        var store = new DialogAdminStore(act);

        await Assert.ThrowsAsync<DialogPublishedException>(async () =>
            await new DeleteQuestionCommandHandler(store)
                .Handle(new DeleteQuestionCommand(dialogId, questionId), default));
    }

    /// <summary>Ohne Dialog gibt es kein Layout – die Meldung ist ein Not-Found, kein Konflikt.</summary>
    [Fact]
    public async Task SetDialogLayout_fuer_unbekannten_Dialog_wirft_NotFound()
    {
        using var act = CreateContext();

        await Assert.ThrowsAsync<ConfigurationNotFoundException>(async () =>
            await Handler(act).Handle(
                new SetDialogLayoutCommand(Guid.NewGuid(), [Entry(Guid.NewGuid(), 1, 1)]), default));
    }

    /// <summary>
    /// Anfrage-Regeln liegen am Command (<see cref="IValidatableObject"/>) und ergeben eine 400 – hier
    /// geprüft über denselben Validator, den das <c>ValidationPipelineBehavior</c> aufruft.
    /// </summary>
    [Theory]
    [MemberData(nameof(UngueltigeBatches))]
    public void SetDialogLayout_mit_unstimmigem_Batch_ist_ungueltig(DialogLayoutEntry[] entries)
    {
        var command = new SetDialogLayoutCommand(Guid.NewGuid(), entries);
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(
            command, new ValidationContext(command), results, validateAllProperties: true));
        Assert.NotEmpty(results);
    }

    /// <summary>Die drei Anfrage-Regeln: leer, doppeltes Element, negative Koordinate.</summary>
    public static TheoryData<DialogLayoutEntry[]> UngueltigeBatches()
    {
        var element = Guid.NewGuid();
        var data = new TheoryData<DialogLayoutEntry[]>();

        data.Add([]);
        data.Add([Entry(element, 10, 10), Entry(element, 20, 20)]);
        data.Add([Entry(element, -1, 10)]);

        return data;
    }

    /// <summary>Zurücksetzen verwirft alle Zeilen des Dialogs; danach greift wieder das Auto-Layout.</summary>
    [Fact]
    public async Task ResetDialogLayout_entfernt_alle_Zeilen_des_Dialogs()
    {
        var dialogId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));
        Seed(TestDialogFactory.BuildFullDialog(otherId, out _));

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.RoleQuestionId, 10, 20), Entry(ids.DevQuestionId, 30, 40)]),
                default);
        }

        using (var act = CreateContext())
        {
            await new ResetDialogLayoutCommandHandler(new DialogAdminStore(act))
                .Handle(new ResetDialogLayoutCommand(dialogId), default);
        }

        using var assert = CreateContext();
        Assert.Empty(assert.Set<DialogLayout>().Where(row => row.DialogId == dialogId));

        // Der zweite Dialog behält seine Position – zurückgesetzt wird genau einer.
        Assert.Single(assert.Set<DialogLayout>().Where(row => row.DialogId == otherId));
    }

    // ---- Handarbeits-Zweig 1: Klonen ----------------------------------------------------------

    /// <summary>
    /// <b>Akzeptanzkriterium „Klonen":</b> Die abgeleitete Version liegt genauso. Geprüft wird gegen die
    /// <b>umgeschriebene</b> <c>ElementId</c> – dass irgendeine Zeile existiert, wäre keine Aussage:
    /// <c>CreateDialogVersionCommand</c> vergibt jeder Frage eine neue Guid, eine unübersetzte Zeile
    /// zeigte ins Leere.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_schreibt_das_Layout_auf_die_geklonten_Frage_Ids_um()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.RoleQuestionId, 120, 240)]), default);
        }

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        var layout = Assert.Single(copy.Layout);
        Assert.Equal(LayoutElementKind.Question, layout.ElementKind);
        Assert.Equal(120, layout.X);
        Assert.Equal(240, layout.Y);

        // Die Zeile zeigt auf die KOPIE der Einstiegsfrage, nicht mehr auf das Original.
        Assert.NotEqual(ids.RoleQuestionId, layout.ElementId);

        var roleCopy = Assert.Single(copy.Questions, question => question.Key == "role");
        Assert.Equal(roleCopy.Id, layout.ElementId);

        // Die Quelle behält ihre eigene Zeile.
        using var assert = CreateContext();
        Assert.Equal(ids.RoleQuestionId, Assert.Single(
            assert.Set<DialogLayout>().Where(row => row.DialogId == dialogId)).ElementId);
    }

    /// <summary>
    /// Eine Zeile ohne Element wird beim Klonen <b>verworfen</b>, nicht unverändert übernommen: Sie hat
    /// in der Kopie kein Ziel und trüge sich sonst durch jede Folgeversion.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_verwirft_eine_Position_ohne_Element()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));

        using (var act = CreateContext())
        {
            // Ein Verweis, den es im Dialog nicht gibt – der Command prüft ElementId bewusst nicht.
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(Guid.NewGuid(), 10, 10)]), default);
        }

        DialogDetail copy;
        using (var act = CreateContext())
        {
            copy = await new CreateDialogVersionCommandHandler(new DialogAdminStore(act))
                .Handle(new CreateDialogVersionCommand(dialogId), default);
        }

        Assert.Empty(copy.Layout);
    }

    // ---- Handarbeits-Zweig 2: Aufräumen -------------------------------------------------------

    /// <summary>
    /// <b>Akzeptanzkriterium „Aufräumen":</b> Mit der Frage verschwindet ihre Position.
    /// <c>ElementId</c> ist FK-los, die Datenbank räumt hier also nichts ab.
    /// </summary>
    [Fact]
    public async Task DeleteQuestion_entfernt_die_Layout_Zeile_der_Frage()
    {
        var dialogId = Guid.NewGuid();

        // Als Entwurf: Das Löschen einer Frage ist eine Graph-Änderung und am veröffentlichten Dialog
        // gesperrt (ADR 0005) – geprüft wird hier der Aufräum-Zweig, nicht der Guard.
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids), published: false);

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.RoleQuestionId, 10, 20), Entry(ids.DevQuestionId, 30, 40)]),
                default);
        }

        using (var act = CreateContext())
        {
            await new DeleteQuestionCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteQuestionCommand(dialogId, ids.DevQuestionId), default);
        }

        using var assert = CreateContext();
        var remaining = assert.Set<DialogLayout>().Where(row => row.DialogId == dialogId).ToList();

        Assert.Equal(ids.RoleQuestionId, Assert.Single(remaining).ElementId);
    }

    /// <summary>Das Löschen des Dialogs räumt das Layout per Datenbank-Cascade ab.</summary>
    [Fact]
    public async Task DeleteDialog_entfernt_das_Layout_per_Cascade()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.RoleQuestionId, 10, 20)]), default);
        }

        using (var act = CreateContext())
        {
            await new DeleteDialogCommandHandler(new DialogAdminStore(act))
                .Handle(new DeleteDialogCommand(dialogId), default);
        }

        using var assert = CreateContext();
        Assert.Empty(assert.Set<DialogLayout>());
    }

    // ---- Lesen --------------------------------------------------------------------------------

    /// <summary>Der Designer bekommt die Positionen über dieselbe Abfrage wie den Graphen.</summary>
    [Fact]
    public async Task GetDialog_liefert_das_Layout_mit()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out var ids));

        using (var act = CreateContext())
        {
            await Handler(act).Handle(
                new SetDialogLayoutCommand(dialogId, [Entry(ids.RoleQuestionId, 88, 99)]), default);
        }

        using var read = CreateContext();
        var detail = await new GetDialogQueryHandler(new DialogAdminStore(read))
            .Handle(new GetDialogQuery(dialogId), default);

        var layout = Assert.Single(detail.Layout);
        Assert.Equal(ids.RoleQuestionId, layout.ElementId);
        Assert.Equal(88, layout.X);
        Assert.Equal(99, layout.Y);
    }

    // ---- Helfer -------------------------------------------------------------------------------

    private static DialogLayoutEntry Entry(Guid questionId, int x, int y)
        => new(LayoutElementKind.Question, questionId, x, y);

    private static SetDialogLayoutCommandHandler Handler(FlirtyDbContext context)
        => new(new DialogAdminStore(context));

    private void Seed(Dialog dialog, bool? published = null)
    {
        if (published is { } state)
        {
            dialog.IsPublished = state;
        }

        using var arrange = CreateContext();
        arrange.Dialogs.Add(dialog);
        arrange.SaveChanges();
    }
}
