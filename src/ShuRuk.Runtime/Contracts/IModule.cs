using ShuRuk.Runtime.Models;

namespace ShuRuk.Runtime.Contracts;

public interface IModule
{
    ModuleManifest Manifest { get; }

    Task OnLoadAsync(IModuleContext context, CancellationToken cancellationToken = default);

    Task OnUnloadAsync(CancellationToken cancellationToken = default);

    Task OnPauseAsync(CancellationToken cancellationToken = default);

    Task OnResumeAsync(CancellationToken cancellationToken = default);

    object? SettingsPage { get; }
}
