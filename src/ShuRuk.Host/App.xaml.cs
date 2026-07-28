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

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuRuk", "crash.log");

    public static IServiceProvider Services { get; private set; } = null!;
    public static Window MainWindow { get; private set; } = null!;

    public App()
    {
        this.UnhandledException += OnUnhandledException;

        InitializeComponent();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("UnhandledException", e.Exception);
        e.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var services = new ServiceCollection();

            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuRuk");
            Directory.CreateDirectory(appDataPath);

            var dbPath = Path.Combine(appDataPath, "shuruk.db");
            var logsDir = Path.Combine(appDataPath, "logs");
            var modulesPath = Path.Combine(AppContext.BaseDirectory, "modules");
            var cachePath = Path.Combine(appDataPath, "cache");

            var databaseInitializer = new DatabaseInitializer(dbPath);
            services.AddSingleton(databaseInitializer);
            services.AddSingleton<IConfigurationService>(new ConfigurationService(dbPath));
            services.AddSingleton<IEncryptionService>(new EncryptionService());
            services.AddSingleton(new AuditLogService(logsDir));
            services.AddSingleton(new SearchEngine(dbPath));
            services.AddSingleton<IModuleManager>(new ModuleManager(databaseInitializer, modulesPath, appDataPath));
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
            services.AddSingleton<LanguageService>();
            services.AddSingleton<ThemeService>();

            Services = services.BuildServiceProvider();

            var languageService = Services.GetRequiredService<LanguageService>();
            languageService.LoadAsync().GetAwaiter().GetResult();

            var themeService = Services.GetRequiredService<ThemeService>();
            themeService.InitializeAsync().GetAwaiter().GetResult();

            var moduleManager = Services.GetRequiredService<IModuleManager>();
            moduleManager.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched services", ex);
        }

        try
        {
            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched MainWindow", ex);
            TryRestart();
        }

        Program.ClearStarting();

    }

    private static void TryRestart()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!
                });
            }
        }
        catch { }

        Environment.Exit(1);
    }

    private static void LogCrash(string context, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {ex}\n");
        }
        catch { }

        System.Diagnostics.Debug.WriteLine($"[{context}] {ex}");
    }
}
