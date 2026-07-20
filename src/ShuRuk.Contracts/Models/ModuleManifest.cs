using ShuRuk.Contracts.Enums;

namespace ShuRuk.Contracts.Models;

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
    public string? SubmodulePath { get; init; }
    public string? Repository { get; init; }
    public string? Signature { get; init; }
    public bool IsUninstalled { get; set; }
    public DateTime? UninstalledAt { get; set; }
}