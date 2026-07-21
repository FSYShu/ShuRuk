using ShuRuk.Contracts.Models;

namespace ShuRuk.Runtime;

public class PermissionGuard
{
    private readonly HashSet<string> _grantedPermissions;

    public PermissionGuard(ModuleManifest manifest)
    {
        _grantedPermissions = new HashSet<string>(manifest.Permissions, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasPermission(string permission)
    {
        return _grantedPermissions.Contains(permission);
    }

    public void DemandPermission(string permission)
    {
        if (!HasPermission(permission))
        {
            throw new UnauthorizedAccessException(
                $"Module does not have '{permission}' permission. Declared permissions: {string.Join(", ", _grantedPermissions)}");
        }
    }

    public void DemandFilesystemAccess() => DemandPermission("filesystem");
    public void DemandNetworkAccess() => DemandPermission("network");
    public void DemandShellAccess() => DemandPermission("shell");
    public void DemandClipboardAccess() => DemandPermission("clipboard");
    public void DemandInputDeviceAccess() => DemandPermission("input_device");
    public void DemandCredentialsAccess() => DemandPermission("credentials");
    public void DemandSystemAccess() => DemandPermission("system");
    public void DemandUiAccess() => DemandPermission("ui");
}