using ShuRuk.Contracts.Enums;

namespace ShuRuk.Host.Pages;

public class ModuleCardViewModel
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string SourceBadge { get; init; }
    public required string StateText { get; init; }
    public required ModuleSource Source { get; init; }
    public required ModuleState State { get; init; }
}
