using System.Windows;
using HearthSwing.Services;
using HearthSwing.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HearthSwing;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UpdateService.CleanupPreviousUpdate();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        Services.GetRequiredService<ISettingsService>().Load();

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<AppLogger>();
        services.AddSingleton<IAppLogger>(sp => sp.GetRequiredService<AppLogger>());
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IProcessManager, SystemProcessManager>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IProfileManager, ProfileManager>();
        services.AddSingleton<ICacheProtector, CacheProtector>();
        services.AddSingleton<IProcessMonitor, ProcessMonitor>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IArchiveService, TarGzArchiveService>();
        services.AddSingleton<IProfileVersionService, ProfileVersionService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
