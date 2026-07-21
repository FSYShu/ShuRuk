using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ShuRuk.Contracts.Interfaces;
using ShuRuk.Infrastructure.Data;
using ShuRuk.Infrastructure.Services;

namespace ShuRuk.Host;

public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        this.UnhandledException += (s, e) =>
        {
            // Only mark known recoverable exceptions as handled;
            // let all others propagate to the default crash/diagnostics path.
            e.Handled = false;
        };

        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();

        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuRuk");
        Directory.CreateDirectory(appDataPath);

        var dbPath = Path.Combine(appDataPath, "shuruk.db");
        var logPath = Path.Combine(appDataPath, "logs");
        var modulesPath = Path.Combine(AppContext.BaseDirectory, "modules");
        var cachePath = Path.Combine(appDataPath, "cache");

        var databaseInitializer = new DatabaseInitializer(dbPath);
        services.AddSingleton(databaseInitializer);
        services.AddSingleton<IConfigurationService>(new ConfigurationService(dbPath));
        services.AddSingleton(new AuditLogService(logPath));
        services.AddSingleton(new SearchEngine(dbPath));

        await databaseInitializer.InitializeAsync();

        Services = services.BuildServiceProvider();
    }
}
