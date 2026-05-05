namespace HearthSwing.Services;

public sealed class UiLogSink : IUiLogSink
{
    public event Action<string>? MessageLogged;

    public void Write(string message) => MessageLogged?.Invoke(message);
}
