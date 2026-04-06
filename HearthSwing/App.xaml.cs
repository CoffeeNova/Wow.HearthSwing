using System.Windows;
using HearthSwing.Services;

namespace HearthSwing;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UpdateService.CleanupPreviousUpdate();
    }
}
