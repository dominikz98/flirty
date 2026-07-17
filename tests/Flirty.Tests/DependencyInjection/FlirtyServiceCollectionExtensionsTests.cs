using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.DependencyInjection;

/// <summary>
/// Verifiziert den Options-Ausbau von <c>AddFlirty(Action&lt;FlirtyOptions&gt;)</c> aus Issue #34:
/// Provider-Wahl (<c>UseSqlite</c>/<c>UsePostgreSql</c>/<c>UseSqlServer</c>) inkl. automatischer
/// <see cref="FlirtyDbContext"/>-Registrierung mit korrekter <c>MigrationsAssembly</c>, austauschbarer
/// <see cref="IExpressionEvaluator"/> (<c>UseExpressionEvaluator&lt;T&gt;()</c>) und die Webhook-Stub-
/// Registrierung (<c>AddWebhook</c>). Enthält zusätzlich ein reines Console-Setup ohne ASP.NET, das
/// einen Dialog end-to-end über die Facade <see cref="IFlirtyEngine"/> durchspielt.
/// </summary>
public sealed class FlirtyServiceCollectionExtensionsTests
{
    /// <summary><c>UseSqlite</c> registriert einen auflösbaren, mit SQLite konfigurierten <see cref="FlirtyDbContext"/>.</summary>
    [Fact]
    public void UseSqlite_registriert_FlirtyDbContext_mit_Sqlite_Provider_und_MigrationsAssembly()
    {
        using var provider = BuildProvider(options => options.UseSqlite("Data Source=:memory:"));
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDialogStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFlirtyEngine>());
    }

    /// <summary><c>UsePostgreSql</c> wählt den Npgsql-Provider und die PostgreSQL-Migrations-Assembly.</summary>
    [Fact]
    public void UsePostgreSql_waehlt_den_Npgsql_Provider_und_MigrationsAssembly()
    {
        using var provider = BuildProvider(
            options => options.UsePostgreSql("Host=localhost;Database=flirty;Username=u;Password=p"));
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
    }

    /// <summary><c>UseSqlServer</c> wählt den SQL-Server-Provider und die SQL-Server-Migrations-Assembly.</summary>
    [Fact]
    public void UseSqlServer_waehlt_den_SqlServer_Provider_und_MigrationsAssembly()
    {
        using var provider = BuildProvider(
            options => options.UseSqlServer("Server=localhost;Database=flirty;Trusted_Connection=True;"));
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
    }

    /// <summary><c>UseExpressionEvaluator&lt;T&gt;()</c> ersetzt den Default-Evaluator durch die eigene Implementierung.</summary>
    [Fact]
    public void UseExpressionEvaluator_ersetzt_den_Default()
    {
        using var provider = BuildProvider(options => options.UseExpressionEvaluator<FakeExpressionEvaluator>());

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<FakeExpressionEvaluator>(evaluator);
    }

    /// <summary>Ohne <c>UseExpressionEvaluator</c> bleibt der Default <c>DynamicExpressoExpressionEvaluator</c> registriert.</summary>
    [Fact]
    public void Ohne_UseExpressionEvaluator_bleibt_der_Default()
    {
        using var provider = BuildProvider(_ => { });

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<DynamicExpressoExpressionEvaluator>(evaluator);
    }

    /// <summary><c>AddWebhook</c> stellt die gesammelten Registrierungen als <see cref="IReadOnlyList{T}"/> bereit.</summary>
    [Fact]
    public void AddWebhook_stellt_die_Registrierungen_bereit()
    {
        using var provider = BuildProvider(options => options
            .AddWebhook("order-created", "https://example.test/order")
            .AddWebhook("dialog-completed", "https://example.test/done"));

        var webhooks = provider.GetRequiredService<IReadOnlyList<FlirtyWebhookRegistration>>();

        Assert.Equal(2, webhooks.Count);
        Assert.Contains(webhooks, hook => hook == new FlirtyWebhookRegistration("order-created", "https://example.test/order"));
        Assert.Contains(webhooks, hook => hook == new FlirtyWebhookRegistration("dialog-completed", "https://example.test/done"));
    }

    /// <summary>Ohne <c>AddWebhook</c> ist die Webhook-Liste auflösbar und leer.</summary>
    [Fact]
    public void Ohne_AddWebhook_ist_die_Webhook_Liste_leer()
    {
        using var provider = BuildProvider(_ => { });

        var webhooks = provider.GetRequiredService<IReadOnlyList<FlirtyWebhookRegistration>>();

        Assert.Empty(webhooks);
    }

    /// <summary>
    /// Reines Console-Setup ohne ASP.NET: <c>AddLogging().AddFlirty(o =&gt; o.UseSqlite(...))</c> verdrahtet den
    /// gesamten Stack; ein veröffentlichter Dialog lässt sich über die Facade <see cref="IFlirtyEngine"/>
    /// starten und beantworten (Branching liefert die nächste Frage).
    /// </summary>
    [Fact]
    public async Task Console_Setup_ohne_AspNet_spielt_Dialog_ueber_die_Facade_durch()
    {
        // Shared-Cache-in-memory: solange die keep-alive-Verbindung offen ist, teilen sich alle
        // DI-erzeugten FlirtyDbContext-Instanzen dieselbe in-memory-Datenbank.
        const string connectionString = "Data Source=FlirtyDiConsoleTest;Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.UseSqlite(connectionString))
            .BuildServiceProvider();

        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seedScope = provider.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            context.Database.EnsureCreated();
            context.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            context.SaveChanges();
        }

        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var next = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        Assert.False(next.IsCompleted);
        Assert.NotNull(next.NextQuestion);
        Assert.Equal(ids.DevQuestionId, next.NextQuestion.Id);
    }

    private static ServiceProvider BuildProvider(Action<FlirtyOptions> configure)
        => new ServiceCollection()
            .AddLogging()
            .AddFlirty(configure)
            .BuildServiceProvider();

    /// <summary>Test-Doppel für <see cref="IExpressionEvaluator"/>; wird nur zur Prüfung der DI-Ersetzung aufgelöst.</summary>
    private sealed class FakeExpressionEvaluator : IExpressionEvaluator
    {
        public bool Evaluate(string expression, ExpressionContext context) => throw new NotSupportedException();

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
            => throw new NotSupportedException();
    }
}
