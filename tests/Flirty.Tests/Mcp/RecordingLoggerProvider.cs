using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Minimal recording <see cref="ILoggerProvider"/>: keeps every entry so a test can assert that the
/// catch-all branch of <c>FlirtyMcpExceptionFilter</c> logged the swallowed exception server-side. Hand-made,
/// as every other test double in this suite.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLogEntry> _entries = new();

    /// <summary>The recorded entries, in order.</summary>
    public IReadOnlyCollection<RecordedLogEntry> Entries => [.. _entries];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<RecordedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue(new RecordedLogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>One recorded log entry.</summary>
/// <param name="Category">The logger category.</param>
/// <param name="Level">The log level.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The logged exception, if any.</param>
internal sealed record RecordedLogEntry(
    string Category, LogLevel Level, string Message, Exception? Exception);
