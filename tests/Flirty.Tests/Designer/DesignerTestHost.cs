using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Builds the same DI stack as <c>src/Flirty.Designer/Program.cs</c> and provides a freshly migrated
/// SQLite temp database. The shared basis of the gateway tests, so that the wiring has to be caught
/// up in <b>one</b> place in the tests when <c>Program.cs</c> changes.
/// </summary>
internal static class DesignerTestHost
{
    /// <summary>
    /// Builds the container: the engine without a hard-wired provider, the context factory against
    /// the active profile, both gateways and the trigger log incl. its notification handlers.
    /// </summary>
    /// <param name="store">The (in-memory) profile store.</param>
    /// <returns>The finished container.</returns>
    public static ServiceProvider BuildProvider(IConnectionProfileStore store)
        => new ServiceCollection()
            .AddLogging()
            .AddFlirty()
            .AddSingleton(store)
            .AddScoped<ActiveConnectionProfile>()
            .AddScoped<IDbContextFactory<FlirtyDbContext>, FlirtyDesignerDbContextFactory>()
            .AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FlirtyDbContext>>().CreateDbContext())
            .AddScoped<FlirtyAdminGateway>()
            .AddScoped<DesignerTriggerLog>()
            .AddScoped<FlirtyRuntimeGateway>()
            .AddFlirtyHandler<DialogStartedNotification, DesignerTriggerLogHandlers.DialogStarted>()
            .AddFlirtyHandler<AnswerSubmittedNotification, DesignerTriggerLogHandlers.AnswerSubmitted>()
            .AddFlirtyHandler<QuestionAnsweredNotification, DesignerTriggerLogHandlers.QuestionAnswered>()
            .AddFlirtyHandler<DialogCompletedNotification, DesignerTriggerLogHandlers.DialogCompleted>()
            .BuildServiceProvider();

    /// <summary>
    /// Creates a migrated SQLite temp database incl. container/scope, runs the test and clears the
    /// files away again.
    /// </summary>
    /// <remarks>
    /// Deliberately a <b>file</b> database instead of <c>:memory:</c>: the gateways open a fresh scope
    /// per operation and thereby a new connection – an in-memory database would be empty for each of
    /// them.
    /// </remarks>
    /// <param name="test">The test body (service provider of the circuit scope, migrated profile).</param>
    public static async Task RunWithTempDbAsync(Func<IServiceProvider, ConnectionProfile, Task> test)
    {
        ArgumentNullException.ThrowIfNull(test);

        var dbPath = Path.Combine(Path.GetTempPath(), $"flirty-designer-{Guid.NewGuid():N}.db");
        var profile = new ConnectionProfile
        {
            Name = "Temp",
            Provider = FlirtyDatabaseProvider.Sqlite,
            // Pooling=False: otherwise the SQLite connection pool keeps the file open and the cleanup fails.
            ConnectionString = $"Data Source={dbPath};Pooling=False",
        };

        await new ConnectionProfileOperations().ApplyMigrationsAsync(profile);

        var store = new InMemoryConnectionProfileStore();
        store.Save(profile);

        try
        {
            using var provider = BuildProvider(store);
            using var scope = provider.CreateScope();

            await test(scope.ServiceProvider, profile);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var file = dbPath + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }

    /// <summary>
    /// Creates an <b>unpublished</b> dialog with a loop over the admin commands – that is, exactly the
    /// designer's path: <c>position</c> -> <c>more</c>, on <c>more == "yes"</c> back to
    /// <c>position</c>, otherwise on to the terminal question <c>summary</c>.
    /// </summary>
    /// <param name="admin">The admin gateway.</param>
    /// <returns>The ids of the created graph.</returns>
    public static async Task<LoopGraph> ArrangeLoopDialogAsync(FlirtyAdminGateway admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var dialog = await ExpectAsync(admin, (sender, token) =>
            sender.Send(new CreateDialogCommand("loop", "Loop", null), token));

        var position = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateQuestionCommand(
                dialog.Id, "position", "Which position?", QuestionType.FreeText, 0, true, null),
            token));

        var more = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateQuestionCommand(
                dialog.Id, "more", "Another position?", QuestionType.SingleChoice, 1, true, null),
            token));

        foreach (var (key, label, order) in new[] { ("yes", "Yes", 0), ("no", "No", 1) })
        {
            _ = await ExpectAsync(admin, (sender, token) => sender.Send(
                new CreateAnswerOptionCommand(dialog.Id, more.Id, key, label, key, order), token));
        }

        var summary = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateQuestionCommand(
                dialog.Id, "summary", "Summary?", QuestionType.FreeText, 2, false, null),
            token));

        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateTransitionCommand(dialog.Id, position.Id, more.Id, null, 0, true), token));
        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateTransitionCommand(dialog.Id, more.Id, position.Id, "more == \"yes\"", 0, false), token));
        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateTransitionCommand(dialog.Id, more.Id, summary.Id, null, 1, true), token));

        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateLoopCommand(dialog.Id, "positions", position.Id, more.Id), token));

        // Set the entry question – but do NOT publish: the runner plays the draft through.
        var updated = await ExpectAsync(admin, (sender, token) => sender.Send(
            new UpdateDialogCommand(dialog.Id, "loop", "Loop", null, position.Id), token));
        Assert.False(updated.IsPublished);

        return new LoopGraph(dialog.Id, position.Id, more.Id, summary.Id);
    }

    /// <summary>Runs an admin operation and fails if it does not succeed.</summary>
    /// <typeparam name="TValue">The result type.</typeparam>
    /// <param name="admin">The admin gateway.</param>
    /// <param name="operation">The operation.</param>
    /// <returns>The result.</returns>
    public static async Task<TValue> ExpectAsync<TValue>(
        FlirtyAdminGateway admin, Func<ISender, CancellationToken, ValueTask<TValue>> operation)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var result = await admin.ExecuteAsync(operation);

        Assert.True(result.Success, result.Error);
        return result.Value!;
    }

    /// <summary>The ids of the loop dialog created by <see cref="ArrangeLoopDialogAsync"/>.</summary>
    /// <param name="DialogId">The created (unpublished) dialog.</param>
    /// <param name="PositionQuestionId">The loop's entry question.</param>
    /// <param name="MoreQuestionId">The breaking question.</param>
    /// <param name="SummaryQuestionId">The terminal question outside the loop.</param>
    public sealed record LoopGraph(
        Guid DialogId, Guid PositionQuestionId, Guid MoreQuestionId, Guid SummaryQuestionId);

    /// <summary>
    /// Hand-written test double of the <see cref="IConnectionProfileStore"/> (no mocking framework,
    /// project convention): keeps the profiles in memory instead of in a JSON file.
    /// </summary>
    public sealed class InMemoryConnectionProfileStore : IConnectionProfileStore
    {
        private readonly List<ConnectionProfile> _profiles = [];

        /// <inheritdoc />
        public IReadOnlyList<ConnectionProfile> GetAll() => [.. _profiles];

        /// <inheritdoc />
        public ConnectionProfile? Get(string id) => _profiles.FirstOrDefault(profile => profile.Id == id);

        /// <inheritdoc />
        public void Save(ConnectionProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);

            _profiles.RemoveAll(existing => existing.Id == profile.Id);
            _profiles.Add(profile);
        }

        /// <inheritdoc />
        public void Delete(string id) => _profiles.RemoveAll(profile => profile.Id == id);

        /// <inheritdoc />
        public string? DefaultProfileId { get; private set; }

        /// <inheritdoc />
        public void SetDefault(string? id) => DefaultProfileId = id;
    }
}
