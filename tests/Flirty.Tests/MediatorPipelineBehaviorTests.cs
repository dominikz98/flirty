using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Flirty.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flirty.Tests;

/// <summary>
/// Verifies the mediator setup from issue #14: a dummy command runs through the registered
/// base pipeline behaviors (logging + validation).
/// </summary>
public class MediatorPipelineBehaviorTests
{
    [Fact]
    public async Task DummyCommand_runs_through_the_LoggingPipelineBehavior()
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

        // The start is logged -> the behavior ran BEFORE the handler.
        Assert.Contains(spy.Entries, entry => entry.Message.Contains("Mediator processes PingCommand"));
        // The completion (with duration) is logged -> next() returned, so the command ran all the way
        // THROUGH the behavior.
        Assert.Contains(spy.Entries, entry => entry.Message.Contains("PingCommand") && entry.Message.Contains("ms"));
    }

    [Fact]
    public async Task Invalid_command_is_rejected_by_the_ValidationPipelineBehavior()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Message is [Required]; null violates the DataAnnotations validation.
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
