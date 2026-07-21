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
            try
            {
                var auditLog = Services?.GetService(typeof(AuditLogService)) as AuditLogService;
                auditLog?.LogEventAsync("UnhandledApplicationException", new Dictionary<string, object>
                {
                    ["ExceptionType"] = e.Exception.GetType().FullName ?? "Unknown",
                    ["Message"] = e.Exception.Message,
                    ["StackTrace"] = e.Exception.StackTrace ?? string.Empty
                }).GetAwaiter().GetResult();
            }
            catch
            {
                // Suppress logging errors to avoid recursive exception handling
            }
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

        Services = services.BuildServiceProvider();

        var dbInitializer = Services.GetService(typeof(DatabaseInitializer)) as DatabaseInitializer;
        dbInitializer?.InitializeAsync().GetAwaiter().GetResult();

        _window = new MainWindow();
        _window.Activate();
    }
}
