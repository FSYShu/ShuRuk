using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Contracts.Interfaces;

public interface IModuleDiscoveryService
{
    /// <summary>
/// Searches for module discovery entries using optional filters and sorting.
/// </summary>
/// <param name="keyword">Text to match against module information.</param>
/// <param name="category">The category used to filter modules.</param>
/// <param name="sortBy">The field used to sort the results.</param>
/// <returns>The matching module discovery entries.</returns>
Task<IReadOnlyList<ModuleDiscoveryEntry>> SearchModulesAsync(string? keyword = null, string? category = null, string? sortBy = null, CancellationToken cancellationToken = default);

    /// <summary>
/// Installs a module from the specified repository.
/// </summary>
/// <param name="repositoryUrl">The URL of the module repository.</param>
Task InstallModuleAsync(string repositoryUrl, CancellationToken cancellationToken = default);

    /// <summary>
/// Installs a module described by a discovery entry.
/// </summary>
/// <param name="entry">The discovery entry describing the module to install.</param>
Task InstallFromDiscoveryAsync(ModuleDiscoveryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
/// Uninstalls the specified module.
/// </summary>
/// <param name="moduleName">The name of the module to uninstall.</param>
Task UninstallModuleAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Checks whether an update is available for a module.
/// </summary>
/// <param name="moduleName">The name of the module to check.</param>
/// <returns>The available update, or <see langword="null"/> if no update is available.</returns>
Task<ModuleDiscoveryEntry?> CheckUpdateAsync(string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
/// Retrieves the cached module discovery entries.
/// </summary>
/// <returns>The cached module discovery entries.</returns>
Task<IReadOnlyList<ModuleDiscoveryEntry>> GetCachedEntriesAsync(CancellationToken cancellationToken = default);
}