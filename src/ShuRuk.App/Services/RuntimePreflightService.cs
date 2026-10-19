using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace ShuRuk.App.Services;

// Runtime preflight check and auto-install / 运行时预检与自动安装
public sealed class RuntimePreflightService
{
    // Runtime package family name / 运行时包族名
    private const string RuntimePackageFamily = "Shuruk.Runtime_8wekyb3d8bbwe";
    private static readonly Version MinRuntimeVersion = new(0, 2, 0);

    // Check if runtime is installed and version meets requirement / 检查运行时是否已安装且版本满足
    public bool IsRuntimeSatisfied()
    {
        try
        {
            var manager = new PackageManager();
            foreach (var pkg in manager.FindPackagesForUser(string.Empty, RuntimePackageFamily))
            {
                var v = pkg.Id.Version;
                if (new Version(v.Major, v.Minor, v.Build) >= MinRuntimeVersion) return true;
            }
        }
        catch
        {
            // Best-effort check, never throw / 尽力检测，绝不抛出
        }
        return false;
    }

    // Get current package full name, null if unpackaged / 获取当前包全名，非打包返回null
    public static string? GetCurrentPackageFullName()
    {
        try { return Package.Current.Id.FullName; }
        catch { return null; }
    }
}