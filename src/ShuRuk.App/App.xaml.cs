using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using ShuRuk.App.Helpers;
using ShuRuk.Contracts.Interfaces;
using ShuRuk.Contracts.Services;
using System.Runtime.InteropServices;

using ShuRuk.App.Services;
using ShuRuk.Infrastructure.Data;
using ShuRuk.Infrastructure.Services;
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

    private static string GetAppVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version is not null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : AppConstants.DefaultAppVersion;
    }
    public static IAppContext AppContextInstance { get; private set; } = null!;

    public App()
    {
        this.UnhandledException += OnUnhandledException;

        InitializeComponent();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        CrashLogger.LogCrash("UnhandledException", e.Exception);
        // Do NOT set e.Handled = true — let the app crash naturally
        // instead of keeping it alive in an inconsistent state / 不吞掉异常
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var services = new ServiceCollection();

            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppDataFolderName);
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
                http.DefaultRequestHeaders.Add("User-Agent", $"{AppConstants.AppName}/{GetAppVersion()}");
                return new ModuleDiscoveryService(http, manager, modulesPath, cachePath);
            });
            services.AddSingleton<StatusMonitor>(sp =>
            {
                var manager = sp.GetRequiredService<IModuleManager>();
                return new StatusMonitor(manager, TimeSpan.FromSeconds(AppConstants.StatusMonitorIntervalSeconds));
            });
            services.AddSingleton<LanguageService>();
            services.AddSingleton<ThemeService>();

            Services = services.BuildServiceProvider();
            AppContextInstance = this;
            AppContextAccessor.Current = this;

            // Run async init on background thread to avoid STA deadlock
            // 在后台线程运行异步初始化以避免STA死锁
            Task.Run(async () =>
            {
                try
                {
                    var languageService = Services.GetRequiredService<LanguageService>();
                    await languageService.LoadAsync();

                    var themeService = Services.GetRequiredService<ThemeService>();
                    await themeService.InitializeAsync();

                    var moduleManager = Services.GetRequiredService<IModuleManager>();
                    await moduleManager.InitializeAsync();
                }
                catch (Exception ex)
                {
                    CrashLogger.LogCrash("OnLaunched async init", ex);
                }
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash("OnLaunched services", ex);
            return;
        }

        try
        {
            _window = new MainWindow();
            _mainWindow = _window as MainWindow;
            MainWindowStatic = _window;
            _window.Activate();
            LoadAppIcon(_window);
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash("OnLaunched MainWindow", ex);
        }
    }

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

    public void CloseMainWindow()
    {
        _mainWindow?.Close();
    }

    // Set window icon from .ico file / 从.ico文件设置窗口图标
    private void LoadAppIcon(Window window)
    {
        try
        {
            window.AppWindow.SetIcon("Assets/ShuRuk.ico");
            SetWindowAppUserModelProperties(window, "Assets/ShuRuk.ico");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadAppIcon: {ex}"); }
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
            System.Diagnostics.Debug.WriteLine($"SetWindowAppUserModelProperties: {ex}");
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

        public static PROPVARIANT FromString(string value)
        {
            var pv = new PROPVARIANT();
            pv.vt = (ushort)VarEnum.VT_LPWSTR;
            pv.pwszVal = Marshal.StringToCoTaskMemUni(value);
            return pv;
        }

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
