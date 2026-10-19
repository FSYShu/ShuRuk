namespace ShuRuk.Runtime.Contracts;

// WinRT extension info exported by modules for host discovery / 模块导出的WinRT扩展信息供宿主发现
public interface IModuleExtensionInfo
{
    string GetModuleId();
    string GetDisplayName();
    string GetIconGlyph();
    IReadOnlyList<ISubModuleInfo> GetSubModules();
}

// Sub-module metadata and page activation interface / 子模块元数据与页面激活接口
public interface ISubModuleInfo
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    bool IsLight { get; }
    bool IsPortable { get; }

    // Activate sub-module page, returns page instance / 激活子模块页面，返回页面实例
    object? ActivatePage();
}