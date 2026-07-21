using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ShuRuk.Contracts.Interfaces;
using ShuRuk.Host.Services;
using ShuRuk.Infrastructure.Data;
using ShuRuk.Infrastructure.Services;

namespace ShuRuk.Host;

public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Initializes the application and marks unhandled exceptions as handled.
    /// </summary>
    public App()
    {
        this.UnhandledException += (s, e) =>
        {
            e.Handled = true;
        };

        InitializeComponent();
    }

    /// <summary>
    /// Initializes application services and creates the application-wide service provider when the application launches.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();

        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuRuk");
        Directory.CreateDirectory(appDataPath);

        var dbPath = Path.Combine(appDataPath, "shuruk.db");
        var logPath = Path.Combine(appDataPath, "logs");
        var modulesPath = Path.Combine(AppContext.BaseDirectory, "modules");
        var cachePath = Path.Combine(appDataPath, "cache");

        services.AddSingleton(new DatabaseInitializer(dbPath));
        services.AddSingleton<IConfigurationService>(new ConfigurationService(dbPath));
        services.AddSingleton(new AuditLogService(logPath));
        services.AddSingleton(new SearchEngine(dbPath));

        Services = services.BuildServiceProvider();
    }
}
