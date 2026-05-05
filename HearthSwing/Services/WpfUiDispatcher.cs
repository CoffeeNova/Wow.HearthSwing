using System.Windows;

namespace HearthSwing.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => Application.Current?.Dispatcher.CheckAccess() ?? true;

    public void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
