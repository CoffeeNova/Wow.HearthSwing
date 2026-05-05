using Microsoft.Extensions.Logging;

namespace HearthSwing.Tests;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        _entries.Add((logLevel, formatter(state, exception)));
    }

    public bool HasLog(LogLevel level, Func<string, bool> predicate) =>
        _entries.Any(e => e.Level == level && predicate(e.Message));

    public bool HasInformation(Func<string, bool> predicate) =>
        HasLog(LogLevel.Information, predicate);

    public bool HasWarning(Func<string, bool> predicate) =>
        HasLog(LogLevel.Warning, predicate);
}
