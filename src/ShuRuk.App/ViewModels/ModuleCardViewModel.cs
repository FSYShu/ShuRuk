using ShuRuk.Runtime.Abstractions.Models;

namespace ShuRuk.App.ViewModels;

public class ModuleCardViewModel
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string SourceBadge { get; init; }
    public required string StateText { get; init; }
    public required ModuleSource Source { get; init; }
    public required ModuleState State { get; init; }


    // Extended display properties / 扩展显示属性
    public string Version { get; init; } = string.Empty;
    public string DownloadsText { get; init; } = string.Empty;
    public string RatingText { get; init; } = string.Empty;
    public string LastUpdatedText { get; init; } = string.Empty;

    // State badge color key / 状态徽章颜色键
    public string StateColorKey => State switch
    {
        ModuleState.Running => "SystemFillColorSuccessBrush",
        ModuleState.Paused => "SystemFillColorCautionBrush",
        ModuleState.Crashed or ModuleState.LoadFailed => "SystemFillColorCriticalBrush",
        ModuleState.Installed or ModuleState.Loaded => "SystemFillColorSuccessAltBrush",
        _ => "TextFillColorTertiaryBrush"
    };

    // State icon glyph / 状态图标字形 (linear/outline style)
    public string StateIconGlyph => State switch
    {
        ModuleState.Running => "\uE73E",
        ModuleState.Paused => "\uE769",
        ModuleState.Crashed => "\uE7BA",
        ModuleState.LoadFailed => "\uE7BA",
        ModuleState.Installed => "\uE73E",
        ModuleState.Loaded => "\uE9D9",
        ModuleState.Discovered => "\uE721",
        _ => "\uE7B8"
    };

    // Source icon glyph / 来源图标字形 (linear/outline style)
    public string SourceIconGlyph => Source == ModuleSource.BuiltIn ? "\uE950" : "\uE7B8";
}
