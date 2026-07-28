using System.Windows;
using HearthSwing.Services;
using HearthSwing.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HearthSwing;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        Services.GetRequiredService<IUpdateService>().CleanupPreviousUpdate();
        Services.GetRequiredService<ISettingsService>().Load();

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var logSink = new UiLogSink();
        services.AddSingleton<IUiLogSink>(logSink);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new UiLoggerProvider(logSink));
        });

        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IProcessManager, SystemProcessManager>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IWtfInspector, WtfInspector>();
        services.AddSingleton<ICacheProtector, CacheProtector>();
        services.AddSingleton<IProcessMonitor, ProcessMonitor>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IArchiveService, TarGzArchiveService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<ISwitchingOrchestrator, SwitchingOrchestrator>();

        services.AddSingleton<ITemplateTokenizer, TemplateTokenizer>();
        services.AddSingleton<ITemplateFileClassifier, TemplateFileClassifier>();
        services.AddSingleton<IDirectoryReplacer, DirectoryReplacer>();
        services.AddSingleton<IChangeHistoryService, ChangeHistoryService>();
        services.AddSingleton<ITemplateCatalog, TemplateCatalog>();
        services.AddSingleton<ILegacyDataCleanupService, LegacyDataCleanupService>();
        services.AddSingleton<ITemplateCaptureService, TemplateCaptureService>();
        services.AddSingleton<ITemplateApplyService, TemplateApplyService>();
        services.AddSingleton<ITemplateRestoreOrchestrator, TemplateRestoreOrchestrator>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
