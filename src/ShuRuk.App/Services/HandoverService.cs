namespace ShuRuk.App.Services;

// Handover service: launch module via URL scheme / 接管服务：通过URL Scheme唤起模块
public static class HandoverService
{
    // Build shuruk:// URI for module handover / 构建shuruk://协议URI用于模块接管
    public static Uri BuildModuleUri(string moduleId) => new($"shuruk://module/{moduleId}");

    // Build shuruk:// URI for sub-module handover / 构建子模块接管URI
    public static Uri BuildSubModuleUri(string subModuleId) => new($"shuruk://submodule/{subModuleId}");

    // Launch ShuRuk host with module URI / 通过模块URI唤起ShuRuk宿主
    public static async Task<bool> LaunchHostAsync(Uri uri)
    {
        try
        {
            return await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            return false;
        }
    }
}