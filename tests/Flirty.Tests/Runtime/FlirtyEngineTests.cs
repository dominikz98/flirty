using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifiziert die öffentliche Facade <see cref="IFlirtyEngine"/> (Issue #25) end-to-end durch die
/// Mediator-Pipeline: DI-Registrierung, Start eines Dialogs über die Facade (Facade → <c>ISender</c>
/// → Handler → <see cref="IDialogStore"/> → EF Core) und die deklarative Command-Validierung.
/// </summary>
public sealed class FlirtyEngineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Öffnet eine SQLite-in-memory-Verbindung (die offen bleiben muss, sonst wird die DB verworfen)
    /// und erzeugt das Schema einmalig via <c>EnsureCreated()</c>.
    /// </summary>
    public FlirtyEngineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FlirtyDbContext(_options);
        context.Database.EnsureCreated();
    }

    /// <summary>Schließt die Verbindung und verwirft damit die in-memory-Datenbank.</summary>
    public void Dispose() => _connection.Dispose();

    private ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddLogging()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

    /// <summary>Die Facade startet einen veröffentlichten Dialog und liefert Session + erste Frage.</summary>
    [Fact]
    public async Task StartDialogAsync_startet_Dialog_ueber_die_Facade()
    {
        var dialogId = Guid.NewGuid();
        Guid questionId;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out questionId));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var result = await engine.StartDialogAsync("onboarding", "user-1");

        Assert.False(result.IsResumed);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Equal(questionId, result.CurrentQuestion.Id);
        Assert.Equal(2, result.CurrentQuestion.Options.Count);
    }

    /// <summary>Ein leerer <c>DialogKey</c> wird durch das <c>ValidationPipelineBehavior</c> abgewiesen.</summary>
    [Fact]
    public async Task StartDialogAsync_leerer_DialogKey_wird_von_der_Pipeline_abgewiesen()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await engine.StartDialogAsync(string.Empty, "user-1"));
    }

    /// <summary><c>AddFlirty()</c> registriert <see cref="IFlirtyEngine"/> als <see cref="FlirtyEngine"/>.</summary>
    [Fact]
    public void AddFlirty_registriert_IFlirtyEngine()
    {
        using var provider = new ServiceCollection()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        Assert.IsType<FlirtyEngine>(engine);
    }
}
