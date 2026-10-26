using ShuRuk.Runtime.Abstractions.Models;

namespace ShuRuk.Runtime.Contracts;

// WinRT extension info exported by modules for host discovery.
// Modules implement this in their WinRTExtension project; CsWinRT generates
// the .winmd metadata. ShuRuk activates via WinRT activation factory.
// 模块导出的WinRT扩展信息供宿主发现。模块在WinRTExtension项目中实现，
// CsWinRT生成.winmd元数据，ShuRuk通过WinRT激活工厂实例化。
public interface IModuleExtensionInfo
{
    string GetModuleId();
    string GetDisplayName();
    string GetIconGlyph();
    IReadOnlyList<ISubModuleInfo> GetSubModules();
}

// Sub-module metadata and page activation interface.
// PageType indicates whether the sub-module provides a full function page
// or only a settings & about page. ActivatePage returns the page instance
// to be embedded in the host NavigationView's Frame.
// 子模块元数据与页面激活接口。PageType标明功能页或仅介绍设置页，
// ActivatePage返回页面实例嵌入宿主NavigationView的Frame。
public interface ISubModuleInfo
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    bool IsLight { get; }
    bool IsPortable { get; }

    // Page type: function page or settings-only / 页面类型：功能页或仅介绍设置页
    SubModulePageType PageType { get; }

    // Activate sub-module page, returns page instance / 激活子模块页面，返回页面实例
    object? ActivatePage();
}