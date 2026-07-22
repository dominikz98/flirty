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
/// Baut denselben DI-Stack wie <c>src/Flirty.Designer/Program.cs</c> und stellt eine frisch migrierte
/// SQLite-Temp-Datenbank bereit. Gemeinsame Grundlage der Gateway-Tests, damit die Verdrahtung nur an
/// <b>einer</b> Stelle im Test nachgezogen werden muss, wenn <c>Program.cs</c> sich ändert.
/// </summary>
internal static class DesignerTestHost
{
    /// <summary>
    /// Baut den Container: Engine ohne fest verdrahteten Provider, Kontext-Factory gegen das aktive
    /// Profil, beide Gateways und das Trigger-Protokoll samt seiner Notification-Handler.
    /// </summary>
    /// <param name="store">Der (In-Memory-)Profil-Store.</param>
    /// <returns>Der fertige Container.</returns>
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
    /// Legt eine migrierte SQLite-Temp-Datenbank samt Container/Scope an, führt den Test aus und räumt
    /// die Dateien wieder weg.
    /// </summary>
    /// <remarks>
    /// Bewusst eine <b>Datei</b>-Datenbank statt <c>:memory:</c>: Die Gateways öffnen je Operation einen
    /// frischen Scope und damit eine neue Verbindung – eine in-memory-Datenbank wäre für jede davon leer.
    /// </remarks>
    /// <param name="test">Der Testkörper (Service-Provider des Circuit-Scopes, migriertes Profil).</param>
    public static async Task RunWithTempDbAsync(Func<IServiceProvider, ConnectionProfile, Task> test)
    {
        ArgumentNullException.ThrowIfNull(test);

        var dbPath = Path.Combine(Path.GetTempPath(), $"flirty-designer-{Guid.NewGuid():N}.db");
        var profile = new ConnectionProfile
        {
            Name = "Temp",
            Provider = FlirtyDatabaseProvider.Sqlite,
            // Pooling=False: sonst hält der SQLite-Connection-Pool die Datei offen und der Cleanup scheitert.
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
    /// Legt über die Admin-Commands – also genau den Weg des Designers – einen <b>unveröffentlichten</b>
    /// Dialog mit einer Schleife an: <c>position</c> → <c>more</c>, bei <c>more == "yes"</c> zurück auf
    /// <c>position</c>, sonst weiter auf die terminale Frage <c>summary</c>.
    /// </summary>
    /// <param name="admin">Das Admin-Gateway.</param>
    /// <returns>Die Ids des angelegten Graphen.</returns>
    public static async Task<LoopGraph> ArrangeLoopDialogAsync(FlirtyAdminGateway admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var dialog = await ExpectAsync(admin, (sender, token) =>
            sender.Send(new CreateDialogCommand("loop", "Loop", null), token));

        var position = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateQuestionCommand(
                dialog.Id, "position", "Welche Position?", QuestionType.FreeText, 0, true, null),
            token));

        var more = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateQuestionCommand(
                dialog.Id, "more", "Weitere Position?", QuestionType.SingleChoice, 1, true, null),
            token));

        foreach (var (key, label, order) in new[] { ("yes", "Ja", 0), ("no", "Nein", 1) })
        {
            _ = await ExpectAsync(admin, (sender, token) => sender.Send(
                new CreateAnswerOptionCommand(dialog.Id, more.Id, key, label, key, order), token));
        }

        var summary = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateQuestionCommand(
                dialog.Id, "summary", "Zusammenfassung?", QuestionType.FreeText, 2, false, null),
            token));

        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateTransitionCommand(dialog.Id, position.Id, more.Id, null, 0, true), token));
        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateTransitionCommand(dialog.Id, more.Id, position.Id, "more == \"yes\"", 0, false), token));
        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateTransitionCommand(dialog.Id, more.Id, summary.Id, null, 1, true), token));

        _ = await ExpectAsync(admin, (sender, token) => sender.Send(
            new CreateLoopCommand(dialog.Id, "positions", position.Id, more.Id), token));

        // Einstiegsfrage setzen – aber NICHT veröffentlichen: der Runner spielt den Entwurf durch.
        var updated = await ExpectAsync(admin, (sender, token) => sender.Send(
            new UpdateDialogCommand(dialog.Id, "loop", "Loop", null, position.Id), token));
        Assert.False(updated.IsPublished);

        return new LoopGraph(dialog.Id, position.Id, more.Id, summary.Id);
    }

    /// <summary>Führt eine Admin-Operation aus und schlägt fehl, wenn sie nicht gelingt.</summary>
    /// <typeparam name="TValue">Der Ergebnistyp.</typeparam>
    /// <param name="admin">Das Admin-Gateway.</param>
    /// <param name="operation">Die Operation.</param>
    /// <returns>Das Ergebnis.</returns>
    public static async Task<TValue> ExpectAsync<TValue>(
        FlirtyAdminGateway admin, Func<ISender, CancellationToken, ValueTask<TValue>> operation)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var result = await admin.ExecuteAsync(operation);

        Assert.True(result.Success, result.Error);
        return result.Value!;
    }

    /// <summary>Die Ids des von <see cref="ArrangeLoopDialogAsync"/> angelegten Schleifen-Dialogs.</summary>
    /// <param name="DialogId">Der angelegte (unveröffentlichte) Dialog.</param>
    /// <param name="PositionQuestionId">Die Einstiegsfrage der Schleife.</param>
    /// <param name="MoreQuestionId">Die Breaking Question.</param>
    /// <param name="SummaryQuestionId">Die terminale Frage außerhalb der Schleife.</param>
    public sealed record LoopGraph(
        Guid DialogId, Guid PositionQuestionId, Guid MoreQuestionId, Guid SummaryQuestionId);

    /// <summary>
    /// Handgeschriebenes TestDouble des <see cref="IConnectionProfileStore"/> (kein Mocking-Framework,
    /// Projektkonvention): hält die Profile im Speicher statt in einer JSON-Datei.
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
