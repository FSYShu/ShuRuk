using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Contracts.Interfaces;

public interface IModuleDiscoveryService
{
    Task<IReadOnlyList<ModuleDiscoveryEntry>> SearchModulesAsync(string? keyword = null, string? category = null, string? sortBy = null, CancellationToken cancellationToken = default);

    Task InstallModuleAsync(string repositoryUrl, CancellationToken cancellationToken = default);

    Task InstallFromDiscoveryAsync(ModuleDiscoveryEntry entry, CancellationToken cancellationToken = default);

    Task UninstallModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    Task<ModuleDiscoveryEntry?> CheckUpdateAsync(string moduleName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModuleDiscoveryEntry>> GetCachedEntriesAsync(CancellationToken cancellationToken = default);
}