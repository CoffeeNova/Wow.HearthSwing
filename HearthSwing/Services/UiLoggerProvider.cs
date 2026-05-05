using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class UiLoggerProvider : ILoggerProvider
{
    private readonly IUiLogSink _sink;

    public UiLoggerProvider(IUiLogSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName) => new UiLogger(_sink);

    public void Dispose() { }

    private sealed class UiLogger : ILogger
    {
        private readonly IUiLogSink _sink;

        public UiLogger(IUiLogSink sink)
        {
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message))
                return;

            var prefix = logLevel switch
            {
                LogLevel.Warning => "Warning: ",
                LogLevel.Error or LogLevel.Critical => "ERROR: ",
                _ => string.Empty,
            };

            _sink.Write($"{prefix}{message}");
        }
    }
}
