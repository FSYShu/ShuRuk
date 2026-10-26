using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using ShuRuk.App.Helpers;
using ShuRuk.App.Services;
using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Abstractions.Services;
using ShuRuk.Runtime.Abstractions;
using System.Runtime.InteropServices;
using WinRT.Interop;


namespace ShuRuk.App;

public partial class App : Application, IAppContext
{
    private Window? _window;
    private MainWindow? _mainWindow;


    public static IServiceProvider Services { get; private set; } = null!;
    public static Window MainWindowStatic { get; private set; } = null!;

    // IAppContext implementation / IAppContext接口实现
    IServiceProvider IAppContext.Services => Services;

    string IAppContext.AppTitle => _mainWindow?.AppTitle ?? AppConstants.AppName;
    string IAppContext.AppSubtitle => _mainWindow?.AppSubtitleText ?? "";
    string IAppContext.AppVersion => GetAppVersion();

    // Cached app version from assembly / 缓存的应用版本(从程序集获取)
    private static readonly string _appVersion = InitializeAppVersion();

    // Get app version from assembly (cached) / 从程序集获取应用版本(已缓存)
    private static string GetAppVersion() => _appVersion;

    private static string InitializeAppVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version is not null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : AppConstants.DefaultAppVersion;
    }
    public static IAppContext AppContextInstance { get; private set; } = null!;

    // Constructor: wire crash logging and init XAML / 构造函数:挂载崩溃日志并初始化XAML
    public App()
    {
        // AppDomain-level crash logging / AppDomain级崩溃日志
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashLogger.LogCrashSimple("AppDomain.UnhandledException", ex);
        };

        this.UnhandledException += OnUnhandledException;

        InitializeComponent();
    }

    // Log unhandled exceptions without suppressing them / 记录未处理异常但不吞掉
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        CrashLogger.LogCrash("UnhandledException", e.Exception);
        // Do NOT set e.Handled = true — let the app crash naturally
        // instead of keeping it alive in an inconsistent state / 不吞掉异常
    }

    // App launched: register services, init, create window / 应用启动:注册服务、初始化、创建窗口
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TryRegisterServices())
            return;

        TryInitializeServices();
        RegisterRuntimeServices();
        RegisterAppRefCount();
        CreateMainWindow();
        _mainWindow?.PopulateModuleNavItems();
        Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().Activated += OnAppActivated;
    }

    // Register app in runtime refcount, reconcile on each launch / 注册引用计数，每次启动兜底对账
    private void RegisterAppRefCount()
    {
        try
        {
            var packageFullName = RuntimePreflightService.GetCurrentPackageFullName();
            if (packageFullName is null) return;
            var registry = new GlobalRegistry();
            registry.ReconcileInstalledApps(new[] { packageFullName });
            registry.RegisterAppInstall(packageFullName);
        }
        catch (Exception ex) { CrashLogger.LogCrash("RefCount", ex); }
    }

    // Ensure services and window are initialized (shared by OnLaunched and OnAppActivated) / 确保服务和窗口已初始化(OnLaunched和OnAppActivated共用)
    private bool EnsureInitialized()
    {
        if (_mainWindow is not null) return true;
        if (!TryRegisterServices()) return false;
        TryInitializeServices();
        RegisterRuntimeServices();
        RegisterAppRefCount();
        CreateMainWindow();
        return true;
    }

    // Handle protocol activation: shuruk://module/{id} or shuruk://submodule/{id} / 处理协议唤起
    private void OnAppActivated(object? sender, Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        if (args.Kind != Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol) return;
        try
        {
            var protocolArgs = args.Data as Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;
            var uri = protocolArgs?.Uri;
            if (uri is null) return;

            if (!EnsureInitialized()) return;
            _mainWindow?.HandleProtocolActivation(uri);
        }
        catch (Exception ex) { CrashLogger.LogCrash("OnActivated", ex); }
    }

    // Register all DI services / 注册所有DI服务
    private bool TryRegisterServices()
    {
        try
        {
            var services = new ServiceCollection();

            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppDataFolderName);
            Directory.CreateDirectory(appDataPath);

            var dbPath = Path.Combine(appDataPath, AppConstants.DbFileName);
            var logsDir = Path.Combine(appDataPath, AppConstants.LogsFolderName);
            var modulesPath = Path.Combine(AppContext.BaseDirectory, AppConstants.ModulesFolderName);
            var cachePath = Path.Combine(appDataPath, AppConstants.CacheFolderName);

            var databaseInitializer = new DatabaseInitializer(dbPath);
            services.AddSingleton(databaseInitializer);
            services.AddSingleton<IConfigurationService>(new ConfigurationService());
            services.AddSingleton<IEncryptionService>(new EncryptionService());
            services.AddSingleton(new AuditLogService(logsDir));
            services.AddSingleton(new SearchEngine(dbPath));
            services.AddSingleton<IModuleManager>(new ModuleManager(databaseInitializer, modulesPath, appDataPath));
            services.AddSingleton<IModuleDiscoveryService>(sp =>
            {
                var manager = sp.GetRequiredService<IModuleManager>();
                var http = new HttpClient { Timeout = TimeSpan.FromSeconds(AppConstants.HttpTimeoutSeconds) };
                http.DefaultRequestHeaders.Add("User-Agent", $"{AppConstants.AppName}/{GetAppVersion()}");
                return new ModuleDiscoveryService(http, manager, modulesPath, cachePath);
            });
            services.AddSingleton<StatusMonitor>(sp =>
            {
                var manager = sp.GetRequiredService<IModuleManager>();
                var testMode = sp.GetRequiredService<ITestModeService>();
                return new StatusMonitor(manager, testMode, TimeSpan.FromSeconds(AppConstants.StatusMonitorIntervalSeconds));
            });
            services.AddSingleton<LanguageService>();
            services.AddSingleton<ITestModeService, TestModeService>();
            services.AddSingleton<ThemeService>();

            Services = services.BuildServiceProvider();
            AppContextInstance = this;
            AppContextAccessor.Current = this;
            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash("OnLaunched services", ex);
            return false;
        }
    }

    // Run async init on background thread to avoid STA deadlock
    // 在后台线程运行异步初始化以避免STA死锁
    private void TryInitializeServices()
    {
        Task.Run(async () =>
        {
            try
            {
                var languageService = Services.GetRequiredService<LanguageService>();
                await languageService.LoadAsync();

                var themeService = Services.GetRequiredService<ThemeService>();
                await themeService.InitializeAsync();

                // Initialize test mode from config / 从配置初始化测试模式
                var testModeService = Services.GetRequiredService<ITestModeService>();
                await testModeService.InitializeAsync();

                var moduleManager = Services.GetRequiredService<IModuleManager>();
                await moduleManager.InitializeAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogCrash("OnLaunched async init", ex);
            }
        }).GetAwaiter().GetResult();
    }

    // Register Runtime service providers based on app state / 根据应用状态注册Runtime服务提供者
    private void RegisterRuntimeServices()
    {
        try
        {
            var testModeService = Services.GetRequiredService<ITestModeService>();
            RuntimeServices.TestModeService = testModeService;
            RuntimeServices.LanguageService = Services.GetRequiredService<LanguageService>();
            RuntimeServices.ThemeService = Services.GetRequiredService<ThemeService>();
            RuntimeServices.AppContext = this;

            RuntimeServices.ChangelogDataProvider = testModeService.IsEnabled && testModeService.IsFillDataEnabled
                ? new GitHubChangelogDataProvider()
                : null;
        }
        catch (Exception ex) { CrashLogger.LogCrash("RegisterRuntimeServices", ex); }
    }

    // Create and activate main window with icon / 创建并激活主窗口并加载图标
    private void CreateMainWindow()
    {
        try
        {
            _mainWindow = new MainWindow();
            _window = _mainWindow;
            MainWindowStatic = _window;
            _window.Activate();
            LoadAppIcon(_window);
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash("OnLaunched MainWindow", ex);
        }
    }

    // Apply theme by name to main window / 按名称应用主题到主窗口
    public void ApplyTheme(string theme)
    {
        var elementTheme = theme switch
        {
            AppConstants.Themes.Light => ElementTheme.Light,
            AppConstants.Themes.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        _mainWindow?.ApplyTheme(elementTheme);
    }

    // Restart app: close window, relaunch process, exit / 重启应用:关闭窗口、重新启动进程、退出
    public void Restart()
    {
        CloseMainWindow();
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            var workingDir = Path.GetDirectoryName(exePath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = workingDir ?? AppContext.BaseDirectory
            });
        }
        Environment.Exit(0);
    }

    // Close main window if open / 关闭主窗口(若存在)
    public void CloseMainWindow()
    {
        _mainWindow?.Close();
    }

    // Set window icon from .ico file / 从.ico文件设置窗口图标
    private void LoadAppIcon(Window window)
    {
        try
        {
            window.AppWindow.SetIcon(AppConstants.AppIconPath);
            SetWindowAppUserModelProperties(window, AppConstants.AppIconPath);
        }
        catch (Exception ex) { CrashLogger.LogCrash("LoadAppIcon", ex); }
    }
    // Set System.AppUserModel.ID and Icon for unified Task Manager display
    // 为窗口设置System.AppUserModel.ID和Icon以统一任务管理器显示
    private void SetWindowAppUserModelProperties(Window window, string icoRelativePath)
    {
        try
        {
            var hWnd = WindowNative.GetWindowHandle(window);
            var icoFullPath = Path.GetFullPath(icoRelativePath);

            var riid = typeof(IPropertyStore).GUID;
            var hr = SHGetPropertyStoreForWindow(hWnd, ref riid, out var psObj);
            if (hr != 0 || psObj is not IPropertyStore ps) return;

            var idKey = new PROPERTYKEY
            {
                fmtid = new Guid(AppConstants.AppUserModelFmtid),
                pid = AppConstants.AppUserModelIdPid
            };
            var idProp = PROPVARIANT.FromString(AppConstants.AppUserModelIdPackaged);

            var iconKey = new PROPERTYKEY
            {
                fmtid = new Guid(AppConstants.AppUserModelFmtid),
                pid = AppConstants.AppUserModelIconPid
            };
            var iconProp = PROPVARIANT.FromString(icoFullPath);

            try
            {
                ps.SetValue(ref idKey, idProp);
                ps.SetValue(ref iconKey, iconProp);
                ps.Commit();
            }
            finally
            {
                idProp.Dispose();
                iconProp.Dispose();
                Marshal.ReleaseComObject(ps);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash("SetWindowAppUserModelProperties", ex);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    // Sequential PROPVARIANT for reliable COM marshalling / 顺序布局PROPVARIANT确保COM marshalling可靠
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT : IDisposable
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pwszVal;

        // Create PROPVARIANT from string (VT_LPWSTR) / 从字符串创建PROPVARIANT
        public static PROPVARIANT FromString(string value)
        {
            var pv = new PROPVARIANT();
            pv.vt = (ushort)VarEnum.VT_LPWSTR;
            pv.pwszVal = Marshal.StringToCoTaskMemUni(value);
            return pv;
        }

        // Free allocated unmanaged memory / 释放分配的非托管内存
        public void Dispose()
        {
            if (vt == (ushort)VarEnum.VT_LPWSTR && pwszVal != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pwszVal);
                pwszVal = IntPtr.Zero;
            }
        }
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        int SetValue(ref PROPERTYKEY key, PROPVARIANT pv);
        int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

}
