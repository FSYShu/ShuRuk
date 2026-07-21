using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Contracts.Interfaces;

public interface IModuleManager
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ModuleState> GetModuleStateAsync(string moduleName, CancellationToken cancellationToken = default);

    Task InstallModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task UninstallModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task LoadModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task UnloadModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task StartModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task StopModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task PauseModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task ResumeModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task ReinstallBuiltInModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task RegisterModuleAsync(ModuleManifest manifest, string moduleDir, CancellationToken cancellationToken = default);

    IReadOnlyList<ModuleManifest> GetInstalledModules();

    event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;
}

public class ModuleStateChangedEventArgs : EventArgs
{
    public required string ModuleName { get; init; }
    public required ModuleState OldState { get; init; }
    public required ModuleState NewState { get; init; }
}
