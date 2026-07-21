using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Contracts.Interfaces;

public interface IModule
{
    ModuleManifest Manifest { get; }

    /// <summary>
/// Loads and initializes the module using the specified context.
/// </summary>
/// <param name="context">The context provided for module initialization.</param>
Task OnLoadAsync(IModuleContext context, CancellationToken cancellationToken = default);

    /// <summary>
/// Unloads the module and releases its runtime resources.
/// </summary>
/// <param name="cancellationToken">A token that can be used to cancel the unload operation.</param>
Task OnUnloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
/// Pauses module activity.
/// </summary>
/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
Task OnPauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
/// Resumes module activity.
/// </summary>
/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
Task OnResumeAsync(CancellationToken cancellationToken = default);

    object? SettingsPage { get; }
}