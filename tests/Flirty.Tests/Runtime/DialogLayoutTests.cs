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
/// Verifies the layout persistence (#102): the batch upsert
/// (<see cref="SetDialogLayoutCommandHandler"/>), the reset
/// (<see cref="ResetDialogLayoutCommandHandler"/>) and – as the core of this stage – the two
/// <b>manual branches</b> one forgets with the next element kind: cloning along in
/// <see cref="CreateDialogVersionCommandHandler"/> and cleaning up in
/// <see cref="DeleteQuestionCommandHandler"/>.
/// </summary>
/// <remarks>
/// The most important assurance is <c>SetDialogLayout_changes_a_published_layout_too</c>: the layout
/// command deliberately does <b>not</b> run under the <see cref="DialogEditGuard"/> (ADR 0007).
/// Without this test that would be a claim in a comment – and an accidentally added guard would only
/// show up in the browser.
/// </remarks>
public sealed class DialogLayoutTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Opens the SQLite in-memory connection and creates the schema.</summary>
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

    /// <summary>Closes the connection and thereby discards the in-memory database.</summary>
    public void Dispose() => _connection.Dispose();

    private FlirtyDbContext CreateContext() => new(_options);

    // ---- Setting and resetting ----------------------------------------------------------------

    /// <summary>
    /// A second call for the same element updates the row instead of creating a second one –
    /// otherwise the unique index over (<c>DialogId</c>, <c>ElementKind</c>, <c>ElementId</c>) would
    /// trip.
    /// </summary>
    [Fact]
    public async Task SetDialogLayout_creates_and_updates_existing_positions()
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

        // The response carries the COMPLETE layout, so the caller can replace its own state.
        Assert.Equal(2, result.Count);

        using var assert = CreateContext();
        var rows = assert.Set<DialogLayout>().Where(row => row.DialogId == dialogId).ToList();

        Assert.Equal(2, rows.Count);
        var start = Assert.Single(rows, row => row.ElementId == ids.RoleQuestionId);
        Assert.Equal(140, start.X);
        Assert.Equal(260, start.Y);
    }

    /// <summary>
    /// Elements that are not named stay put: a drag gesture moves one element and must not discard
    /// the positions of all the others.
    /// </summary>
    [Fact]
    public async Task SetDialogLayout_leaves_positions_it_does_not_name_untouched()
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
    /// <b>This stage's promise:</b> moving works on a published dialog too and does not answer with a
    /// conflict. Coordinates are not part of the graph (ADR 0007) – the publish lock from ADR 0005
    /// ends at this table.
    /// </summary>
    [Fact]
    public async Task SetDialogLayout_changes_a_published_layout_too()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out var questionId));

        using (var check = CreateContext())
        {
            // Precondition: the dialog really is published – otherwise the test would check nothing.
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
    /// The counter-check to the previous one: a real graph change stays locked on that same published
    /// dialog. Without it, the layout test would only prove that a guard is missing somewhere.
    /// </summary>
    [Fact]
    public async Task Graph_change_stays_locked_on_the_same_published_dialog()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildFullDialog(dialogId, out var questionId));

        using var act = CreateContext();
        var store = new DialogAdminStore(act);

        await Assert.ThrowsAsync<DialogPublishedException>(async () =>
            await new DeleteQuestionCommandHandler(store)
                .Handle(new DeleteQuestionCommand(dialogId, questionId), default));
    }

    /// <summary>Without a dialog there is no layout – the report is a not-found, not a conflict.</summary>
    [Fact]
    public async Task SetDialogLayout_for_an_unknown_dialog_throws_NotFound()
    {
        using var act = CreateContext();

        await Assert.ThrowsAsync<ConfigurationNotFoundException>(async () =>
            await Handler(act).Handle(
                new SetDialogLayoutCommand(Guid.NewGuid(), [Entry(Guid.NewGuid(), 1, 1)]), default));
    }

    /// <summary>
    /// Request rules live on the command (<see cref="IValidatableObject"/>) and produce a 400 –
    /// checked here over the same validator the <c>ValidationPipelineBehavior</c> calls.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidBatches))]
    public void SetDialogLayout_with_an_inconsistent_batch_is_invalid(DialogLayoutEntry[] entries)
    {
        var command = new SetDialogLayoutCommand(Guid.NewGuid(), entries);
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(
            command, new ValidationContext(command), results, validateAllProperties: true));
        Assert.NotEmpty(results);
    }

    /// <summary>The three request rules: empty, duplicate element, negative coordinate.</summary>
    public static TheoryData<DialogLayoutEntry[]> InvalidBatches()
    {
        var element = Guid.NewGuid();
        var data = new TheoryData<DialogLayoutEntry[]>();

        data.Add([]);
        data.Add([Entry(element, 10, 10), Entry(element, 20, 20)]);
        data.Add([Entry(element, -1, 10)]);

        return data;
    }

    /// <summary>Resetting discards all rows of the dialog; afterwards the auto-layout applies again.</summary>
    [Fact]
    public async Task ResetDialogLayout_removes_all_rows_of_the_dialog()
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

        // The second dialog keeps its position – exactly one is reset.
        Assert.Single(assert.Set<DialogLayout>().Where(row => row.DialogId == otherId));
    }

    // ---- Manual branch 1: cloning -------------------------------------------------------------

    /// <summary>
    /// <b>Acceptance criterion "cloning":</b> the derived version is laid out the same way. Checked
    /// against the <b>rewritten</b> <c>ElementId</c> – that some row exists would say nothing:
    /// <c>CreateDialogVersionCommand</c> assigns every question a new Guid, so an untranslated row
    /// would point into the void.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_rewrites_the_layout_onto_the_cloned_question_ids()
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

        // The row points at the COPY of the entry question, no longer at the original.
        Assert.NotEqual(ids.RoleQuestionId, layout.ElementId);

        var roleCopy = Assert.Single(copy.Questions, question => question.Key == "role");
        Assert.Equal(roleCopy.Id, layout.ElementId);

        // The source keeps its own row.
        using var assert = CreateContext();
        Assert.Equal(ids.RoleQuestionId, Assert.Single(
            assert.Set<DialogLayout>().Where(row => row.DialogId == dialogId)).ElementId);
    }

    /// <summary>
    /// A row without an element is <b>discarded</b> while cloning, not carried over unchanged: it has
    /// no target in the copy and would otherwise be dragged through every follow-up version.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_discards_a_position_without_an_element()
    {
        var dialogId = Guid.NewGuid();
        Seed(TestDialogFactory.BuildBranchingDialog(dialogId, out _));

        using (var act = CreateContext())
        {
            // A reference that does not exist in the dialog – the command deliberately does not check ElementId.
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

    // ---- Manual branch 2: cleaning up ---------------------------------------------------------

    /// <summary>
    /// <b>Acceptance criterion "cleaning up":</b> the position disappears together with the question.
    /// <c>ElementId</c> is FK-free, so the database clears nothing here.
    /// </summary>
    [Fact]
    public async Task DeleteQuestion_removes_the_layout_row_of_the_question()
    {
        var dialogId = Guid.NewGuid();

        // As a draft: deleting a question is a graph change and is locked on a published dialog
        // (ADR 0005) – what is checked here is the cleanup branch, not the guard.
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

    /// <summary>Deleting the dialog clears the layout via the database cascade.</summary>
    [Fact]
    public async Task DeleteDialog_removes_the_layout_by_cascade()
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

    // ---- Reading ------------------------------------------------------------------------------

    /// <summary>The designer gets the positions over the same query as the graph.</summary>
    [Fact]
    public async Task GetDialog_carries_the_layout_along()
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

    // ---- Helpers ------------------------------------------------------------------------------

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
