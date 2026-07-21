using ShuRuk.Contracts.Enums;

namespace ShuRuk.Contracts.Models;

public class ModuleInfo
{
    public required ModuleManifest Manifest { get; init; }
    public ModuleState State { get; set; }
    public ModuleSource Source { get; init; }
    public string? SubmodulePath { get; init; }
    public bool IsUninstalled { get; set; }
    public DateTime? UninstalledAt { get; set; }
}