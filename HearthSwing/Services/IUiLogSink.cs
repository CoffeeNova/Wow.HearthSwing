namespace HearthSwing.Services;

public interface IUiLogSink
{
    event Action<string>? MessageLogged;

    void Write(string message);
}
