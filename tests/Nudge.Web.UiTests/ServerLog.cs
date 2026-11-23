using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nudge.Web.UiTests;

/// <summary>Keeps the app's warnings and errors, with their
/// exceptions, for the artifacts of a failed run.</summary>
internal sealed class ServerLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class Logger(
        ServerLog log,
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
            return logLevel >= LogLevel.Warning;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                log._lines.Enqueue(
                    $"{logLevel} {category}: {formatter(state, exception)}"
                    + (exception is null ? "" : $"\n{exception}"));
            }
        }
    }
}
