using ShuRuk.Contracts.Enums;

namespace ShuRuk.Contracts.Models;

public class ModuleDiscoveryEntry
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string Version { get; init; }
    public required string Repository { get; init; }
    public ModuleSource Source { get; init; }
    public string? Category { get; init; }
    public int Downloads { get; init; }
    public double Rating { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? Icon { get; init; }
    public bool Installed { get; set; }
}