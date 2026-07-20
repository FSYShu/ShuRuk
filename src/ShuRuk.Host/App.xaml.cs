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

    public App()
    {
        this.UnhandledException += (s, e) =>
        {
            e.Handled = true;
        };

        InitializeComponent();
    }

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
        services.AddSingleton<IModuleManager>(sp =>
        {
            var dbInit = sp.GetRequiredService<DatabaseInitializer>();
            var manager = new ModuleManager(dbInit, modulesPath, appDataPath);
            manager.InitializeAsync().GetAwaiter().GetResult();
            return manager;
        });
        services.AddSingleton<IModuleDiscoveryService>(sp =>
        {
            var manager = sp.GetRequiredService<IModuleManager>();
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "ShuRuk/1.0");
            return new ModuleDiscoveryService(http, manager, modulesPath, cachePath);
        });
        services.AddSingleton<StatusMonitor>(sp =>
        {
            var manager = sp.GetRequiredService<IModuleManager>();
            return new StatusMonitor(manager, TimeSpan.FromSeconds(5));
        });

        Services = services.BuildServiceProvider();
    }
}
