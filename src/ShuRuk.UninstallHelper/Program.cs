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
    private const string RuntimePackageFamily = "ShuRuk.Runtime_8wekyb3d8bbwe";

    private static int Main(string[] args)
    {
        try
        {
            // Prefer package full name from args (passed by windows.uninstallAction),
            // fall back to Package.Current for direct invocation.
            // 优先从args解析包全名(windows.uninstallAction传入)，回退到Package.Current供直接调用。
            var packageFullName = ParsePackageFullNameFromArgs(args) ?? GetCurrentPackageFullName();
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

    // Parse package full name from command-line args / 从命令行参数解析包全名
    private static string? ParsePackageFullNameFromArgs(string[] args)
    {
        // windows.uninstallAction passes package full name as first argument.
        // Filter out empty/whitespace and obvious non-package-name values.
        // windows.uninstallAction以包全名作为首个参数，过滤空值与非包名文本。
        if (args is { Length: > 0 })
        {
            var candidate = args[0].Trim();
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Contains('_'))
                return candidate;
        }
        return null;
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