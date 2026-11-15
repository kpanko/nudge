using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nudge.Web.Tests.Api;

/// <summary>Keeps what the app logs, for tests to look at.</summary>
internal sealed class LogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void Dispose()
    {
    }

    internal sealed record Entry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class Logger(
        LogSink sink,
        string category)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            sink._entries.Enqueue(
                new Entry(
                    category,
                    logLevel,
                    formatter(state, exception),
                    exception));
        }
    }
}
