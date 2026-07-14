using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Flirty.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flirty.Tests;

/// <summary>
/// Verifiziert das Mediator-Setup aus Issue #14: Ein Dummy-Command läuft durch die
/// registrierten Basis-Pipeline-Behaviors (Logging + Validierung).
/// </summary>
public class MediatorPipelineBehaviorTests
{
    [Fact]
    public async Task DummyCommand_laeuft_durch_LoggingPipelineBehavior()
    {
        var spy = new SpyLoggerProvider();
        var provider = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(spy))
            .AddFlirty()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var response = await sender.Send(new PingCommand("ping"));

        Assert.Equal("ping", response.Message);

        // Der Beginn wird protokolliert -> das Behavior lief VOR dem Handler.
        Assert.Contains(spy.Entries, entry => entry.Message.Contains("Mediator verarbeitet PingCommand"));
        // Der Abschluss (mit Dauer) wird protokolliert -> next() kehrte zurück, der Command lief
        // vollständig DURCH das Behavior.
        Assert.Contains(spy.Entries, entry => entry.Message.Contains("PingCommand") && entry.Message.Contains("ms"));
    }

    [Fact]
    public async Task UngueltigerCommand_wird_von_ValidationPipelineBehavior_abgewiesen()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Message ist [Required]; null verletzt die DataAnnotations-Validierung.
        await Assert.ThrowsAsync<ValidationException>(async () => await sender.Send(new PingCommand(null!)));
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class SpyLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new SpyLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class SpyLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}
