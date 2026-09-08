using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WealthLedger.Api.Tests;

internal sealed class TestLogCollector : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    internal IReadOnlyList<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName)
        => new CollectorLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CollectorLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _messages;

        internal CollectorLogger(
            ConcurrentQueue<string> messages)
        {
            _messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            _messages.Enqueue(formatter(state, exception));
        }
    }
}
