using Microsoft.UI.Xaml;
using ShuRuk.App.Helpers;
using System.Runtime.InteropServices;

namespace ShuRuk.App;

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string AppID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    // Check if running inside an MSIX package / 检测是否在MSIX包内运行
    private static bool IsRunningInPackage()
    {
        uint len = 0;
        var hr = GetCurrentPackageFullName(ref len, IntPtr.Zero);
        return hr != AppConstants.AppmodelErrorNoPackage;
    }

    [STAThread]
    static void Main(string[] args)
    {
        // Only set explicit AppUserModelID when running unpackaged
        // 仅在非MSIX包启动时设置显式AppUserModelID
        if (!IsRunningInPackage())
        {
            try { SetCurrentProcessExplicitAppUserModelID(AppConstants.AppUserModelId); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SetAppUserModelID failed: {ex}"); }

        }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashLogger.LogCrashSimple("AppDomain.UnhandledException", ex);
        };

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(p =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);

            try
            {
                new App();
            }
            catch (Exception ex)
            {
                CrashLogger.LogCrashSimple("App() constructor", ex);
            }
        });
    }
}
