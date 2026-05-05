namespace HearthSwing.Services;

public interface IUiDispatcher
{
    bool IsOnUiThread { get; }

    void Invoke(Action action);

    Task InvokeAsync(Action action);
}
