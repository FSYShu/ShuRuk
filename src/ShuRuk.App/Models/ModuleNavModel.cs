namespace ShuRuk.App.Models;

// Navigation model for modules and sub-modules / 模块与子模块导航模型
public sealed class ModuleNavModel
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public bool IsLight { get; init; }
    public bool IsPortable { get; init; }
    public IReadOnlyList<SubModuleNavModel> SubModules { get; init; } = Array.Empty<SubModuleNavModel>();
}

// Sub-module navigation model / 子模块导航模型
public sealed class SubModuleNavModel
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public bool IsLight { get; init; }
    public bool IsPortable { get; init; }
}