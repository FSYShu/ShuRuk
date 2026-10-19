using ShuRuk.Runtime.Abstractions.Models;

namespace ShuRuk.Runtime.Abstractions.Models;

public class ModuleManifest
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required string Entry { get; init; }
    public required string[] Permissions { get; init; }
    public string? MinHostVersion { get; init; }
    public string? Icon { get; init; }
    public ModuleSource Source { get; init; }
    public ModuleKind Kind { get; init; }
    public string? SubmodulePath { get; init; }
    public string? Repository { get; init; }
    public string? Signature { get; init; }
    public bool IsUninstalled { get; set; }
    public DateTime? UninstalledAt { get; set; }

    // ---- ShuRuk.module.json feature flags / 模块特性标记 ----

    // Module identifier (author.module format) / 模块标识(author.module格式)
    public string? ModuleId { get; init; }
    // Module author / 模块作者
    public string? Author { get; init; }
    // Light feature flag: small popup-style window / 轻量标记：小弹窗式窗口
    public bool Light { get; init; }
    // Portable flag: self-contained single-file exe / 便携标记：自包含单文件exe
    public bool Portable { get; init; }
    // Sub-modules list / 子模块列表
    public List<SubModuleManifest>? SubModules { get; init; }
}

// Sub-module manifest from ShuRuk.module.json / 子模块清单(来自ShuRuk.module.json)
public class SubModuleManifest
{
    public required string ModuleId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    // Light feature flag / 轻量标记
    public bool Light { get; init; }
    // Portable flag / 便携标记
    public bool Portable { get; init; }
}