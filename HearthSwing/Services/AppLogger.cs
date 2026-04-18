namespace HearthSwing.Services;

public sealed class AppLogger : IAppLogger
{
    private Action<string>? _sink;

    public void SetSink(Action<string> sink) => _sink = sink;

    public void Log(string message) => _sink?.Invoke(message);
}
