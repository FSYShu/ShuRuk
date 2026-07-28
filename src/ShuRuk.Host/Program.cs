using Microsoft.UI.Xaml;

namespace ShuRuk.Host;

internal static class Program
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuRuk");

    private static readonly string CrashLogPath = Path.Combine(AppDataPath, "crash.log");

    private static readonly string StartupFlagPath = Path.Combine(AppDataPath, ".starting");

    private static readonly string CrashCountPath = Path.Combine(AppDataPath, ".crash_count");

    private static readonly string WatchdogBatPath = Path.Combine(AppDataPath, "watchdog.bat");

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        var crashCount = ReadCrashCount();
        if (crashCount >= 3)
        {
            ResetCrashCount();
            LogCrash("CrashLoop", new Exception($"Cleared crash count after {crashCount} consecutive crashes"));
        }

        MarkStarting();

        StartWatchdog();

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
                LogCrash("App() constructor", ex);
            }
        });

    }

    private static void MarkStarting()
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);
            File.WriteAllText(StartupFlagPath, Environment.ProcessId.ToString());
            IncrementCrashCount();
        }
        catch { }
    }

    public static void ClearStarting()
    {
        try { if (File.Exists(StartupFlagPath)) File.Delete(StartupFlagPath); } catch { }
        ResetCrashCount();
    }

    private static int ReadCrashCount()
    {
        try
        {
            if (File.Exists(CrashCountPath) && int.TryParse(File.ReadAllText(CrashCountPath), out var count))
            {
                var writeTime = File.GetLastWriteTime(CrashCountPath);
                if ((DateTime.Now - writeTime).TotalMinutes < 2)
                    return count;
            }
        }
        catch { }
        return 0;
    }

    private static void IncrementCrashCount()
    {
        try
        {
            var count = ReadCrashCount() + 1;
            File.WriteAllText(CrashCountPath, count.ToString());
        }
        catch { }
    }

    private static void ResetCrashCount()
    {
        try { if (File.Exists(CrashCountPath)) File.Delete(CrashCountPath); } catch { }
    }

    private static void StartWatchdog()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null) return;

            Directory.CreateDirectory(AppDataPath);
            File.WriteAllText(WatchdogBatPath,
                $"@echo off\r\nping -n 15 127.0.0.1 >nul\r\nif exist \"{StartupFlagPath}\" (\r\n  del \"{StartupFlagPath}\"\r\n  start \"\" \"{exePath}\"\r\n)\r\ndel \"{WatchdogBatPath}\"\r\n");

            var psi = new System.Diagnostics.ProcessStartInfo(WatchdogBatPath)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private static void LogCrash(string context, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {ex}\n");
        }
        catch { }

        System.Diagnostics.Debug.WriteLine($"[{context}] {ex}");
    }
}
