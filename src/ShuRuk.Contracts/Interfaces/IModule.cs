using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Contracts.Interfaces;

public interface IModule
{
    ModuleManifest Manifest { get; }

    Task OnLoadAsync(IModuleContext context, CancellationToken cancellationToken = default);

    Task OnUnloadAsync(CancellationToken cancellationToken = default);

    Task OnPauseAsync(CancellationToken cancellationToken = default);

    Task OnResumeAsync(CancellationToken cancellationToken = default);

    object? SettingsPage { get; }
}