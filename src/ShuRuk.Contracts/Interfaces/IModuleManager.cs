using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Contracts.Interfaces;

public interface IModuleManager
{
    /// <summary>
/// Retrieves the current state of a module.
/// </summary>
/// <param name="moduleName">The name of the module.</param>
/// <param name="cancellationToken">A token used to cancel the operation.</param>
/// <returns>The current state of the module.</returns>
Task<ModuleState> GetModuleStateAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Installs the specified module.
/// </summary>
/// <param name="moduleName">The name of the module to install.</param>
/// <param name="cancellationToken">A token used to cancel the operation.</param>
Task InstallModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Uninstalls the specified module.
/// </summary>
/// <param name="moduleName">The name of the module to uninstall.</param>
Task UninstallModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Loads the specified module for use.
/// </summary>
/// <param name="moduleName">The name of the module to load.</param>
Task LoadModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Unloads the specified module.
/// </summary>
/// <param name="moduleName">The name of the module to unload.</param>
/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
Task UnloadModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Starts the specified module.
/// </summary>
/// <param name="moduleName">The name of the module to start.</param>
/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
Task StartModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Stops the specified module.
/// </summary>
/// <param name="moduleName">The name of the module to stop.</param>
Task StopModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Pauses the specified module.
/// </summary>
Task PauseModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Resumes the specified module.
/// </summary>
/// <param name="moduleName">The name of the module to resume.</param>
Task ResumeModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Reinstalls the specified built-in module.
/// </summary>
/// <param name="moduleName">The name of the built-in module to reinstall.</param>
Task ReinstallBuiltInModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Retrieves the modules currently installed.
/// </summary>
/// <returns>The installed module manifests.</returns>
IReadOnlyList<ModuleManifest> GetInstalledModules();

    event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;
}

public class ModuleStateChangedEventArgs : EventArgs
{
    public required string ModuleName { get; init; }
    public required ModuleState OldState { get; init; }
    public required ModuleState NewState { get; init; }
}
