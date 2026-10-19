using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.App.Services;

// Scan installed modules and load extensions / 扫描已安装模块并加载扩展
public sealed class ModuleScanner
{
    private readonly IModuleManager _moduleManager;
    private readonly ExtensionLoader _extensionLoader = new();

    public ModuleScanner(IModuleManager moduleManager) => _moduleManager = moduleManager;

    // Scan all installed modules via extension points / 通过扩展点扫描所有已安装模块
    public async Task ScanAsync()
    {
        try
        {
            var extensions = await _extensionLoader.DiscoverExtensionsAsync();
            foreach (var ext in extensions)
            {
                // Load module metadata from extension / 从扩展加载模块元数据
                System.Diagnostics.Debug.WriteLine($"ModuleScanner: found extension {ext.Id} from {ext.Package.Id.FamilyName}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ModuleScanner error: {ex}");
        }
    }
}