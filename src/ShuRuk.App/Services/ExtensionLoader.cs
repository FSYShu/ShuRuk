using Windows.ApplicationModel.AppExtensions;

namespace ShuRuk.App.Services;

// Enumerate Windows extension points and activate WinRT extensions / 枚举Windows扩展点并激活WinRT扩展
public sealed class ExtensionLoader
{
    // Extension contract name / 扩展合约名
    private const string ExtensionContract = "shuruk.module";

    // Discover installed module extensions / 发现已安装的模块扩展
    public async Task<IReadOnlyList<AppExtension>> DiscoverExtensionsAsync()
    {
        try
        {
            var catalog = AppExtensionCatalog.Open(ExtensionContract);
            return await catalog.FindAllAsync();
        }
        catch
        {
            return Array.Empty<AppExtension>();
        }
    }
}