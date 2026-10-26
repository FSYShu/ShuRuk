using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace ShuRuk.UninstallHelper;

// Silent uninstall helper: registry refcount decrement and runtime cleanup
// 静默卸载辅助程序：注册表引用计数递减与运行时清理
internal static class Program
{
    // Registry root for runtime refcount / 运行时引用计数注册表根
    private const string RuntimeRegKey = @"Software\ShuRuk\Runtime";
    private const string RefCountValue = "RefCount";
    private const string InstalledAppsValue = "InstalledApps";
    private const string RuntimePackageFamily = "Shuruk.Runtime_8wekyb3d8bbwe";

    private static int Main(string[] args)
    {
        try
        {
            var packageFullName = GetCurrentPackageFullName();
            if (packageFullName is null)
            {
                // Not running in packaged context, nothing to do
                // 非打包上下文运行，无需处理
                return 0;
            }

            DecrementRefCount(packageFullName);
        }
        catch
        {
            // Swallow all exceptions to never block uninstall
            // 吞掉所有异常以永不阻断卸载
        }
        return 0;
    }

    // Get current MSIX package full name / 获取当前MSIX包全名
    private static string? GetCurrentPackageFullName()
    {
        try
        {
            return Package.Current.Id.FullName;
        }
        catch
        {
            return null;
        }
    }

    // Decrement refcount and conditionally uninstall runtime / 递减引用计数并按条件卸载运行时
    private static void DecrementRefCount(string packageFullName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RuntimeRegKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RuntimeRegKey, writable: true);

        // Remove this package from InstalledApps / 从已安装列表移除本包
        var existing = (string[]?)key.GetValue(InstalledAppsValue, Array.Empty<string>(), RegistryValueOptions.DoNotExpandEnvironmentNames);
        var updated = (existing ?? Array.Empty<string>())
            .Where(p => !string.Equals(p, packageFullName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        key.SetValue(InstalledAppsValue, updated, RegistryValueKind.MultiString);

        // Decrement RefCount, uninstall runtime if zero / 递减引用计数，归零则卸载运行时
        var current = (int)(key.GetValue(RefCountValue, 0) ?? 0);
        var next = Math.Max(0, current - 1);
        key.SetValue(RefCountValue, next, RegistryValueKind.DWord);

        if (next == 0)
        {
            TryUninstallRuntime();
        }
    }

    // Uninstall ShuRuk.Runtime via PackageManager COM API / 通过PackageManager COM API卸载运行时
    private static void TryUninstallRuntime()
    {
        try
        {
            var manager = new PackageManager();
            var packages = manager.FindPackagesForUser(string.Empty, RuntimePackageFamily);
            foreach (var pkg in packages)
            {
                manager.RemovePackageAsync(pkg.Id.FullName).AsTask().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Best-effort cleanup, never throw / 尽力清理，绝不抛出
        }
    }
}