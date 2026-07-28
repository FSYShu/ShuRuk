using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Interfaces;
using ShuRuk.Contracts.Models;
using ShuRuk.Runtime;

namespace ShuRuk.Host.Services;

public class ModuleDiscoveryService : IModuleDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly IModuleManager _moduleManager;
    private readonly ModuleManifestValidator _validator = new();
    private readonly string _installPath;
    private readonly string _cachePath;

    private List<ModuleDiscoveryEntry>? _cachedEntries;
    private bool _isOffline;

    public bool IsOffline => _isOffline;

    public ModuleDiscoveryService(HttpClient httpClient, IModuleManager moduleManager, string installPath, string cachePath)
    {
        _httpClient = httpClient;
        _moduleManager = moduleManager;
        _installPath = installPath;
        _cachePath = cachePath;
        Directory.CreateDirectory(_cachePath);
    }

    public async Task<IReadOnlyList<ModuleDiscoveryEntry>> SearchModulesAsync(string? keyword = null, string? category = null, string? sortBy = null, CancellationToken cancellationToken = default)
    {
        var entries = await FetchEntriesAsync(cancellationToken);

        if (keyword is not null)
        {
            entries = entries.Where(e =>
                e.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (e.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                e.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (category is not null)
        {
            entries = entries.Where(e => e.Category?.Equals(category, StringComparison.OrdinalIgnoreCase) ?? false).ToList();
        }

        entries = sortBy?.ToLowerInvariant() switch
        {
            "downloads" => entries.OrderByDescending(e => e.Downloads).ToList(),
            "rating" => entries.OrderByDescending(e => e.Rating).ToList(),
            "updated" => entries.OrderByDescending(e => e.UpdatedAt).ToList(),
            _ => entries.OrderByDescending(e => e.Rating).ThenByDescending(e => e.Downloads).ToList()
        };

        return entries;
    }

    public async Task InstallModuleAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        var manifest = await FetchManifestFromRepositoryAsync(repositoryUrl, cancellationToken);
        if (manifest is null)
            throw new InvalidOperationException($"Cannot fetch manifest from repository: {repositoryUrl}");

        var validationResult = _validator.Validate(manifest);
        if (!validationResult.IsValid)
            throw new InvalidOperationException($"Invalid manifest: {string.Join("; ", validationResult.Errors)}");

        await DownloadAndInstallAsync(manifest, repositoryUrl, cancellationToken);
    }

    public async Task InstallFromDiscoveryAsync(ModuleDiscoveryEntry entry, CancellationToken cancellationToken = default)
    {
        var manifest = await FetchManifestFromRepositoryAsync(entry.Repository, cancellationToken);
        if (manifest is null)
            throw new InvalidOperationException($"Cannot fetch manifest from repository: {entry.Repository}");

        var validationResult = _validator.Validate(manifest);
        if (!validationResult.IsValid)
            throw new InvalidOperationException($"Invalid manifest: {string.Join("; ", validationResult.Errors)}");

        await DownloadAndInstallAsync(manifest, entry.Repository, cancellationToken);
    }

    public Task UninstallModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        return _moduleManager.UninstallModuleAsync(moduleName, cancellationToken);
    }

    public async Task<ModuleDiscoveryEntry?> CheckUpdateAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        var installed = _moduleManager.GetInstalledModules()
            .FirstOrDefault(m => m.Name == moduleName);

        if (installed is null) return null;

        var entries = await FetchEntriesAsync(cancellationToken);
        var remote = entries.FirstOrDefault(e => e.Name == moduleName);

        if (remote is null) return null;

        return remote.Version != installed.Version ? remote : null;
    }

    public async Task<IReadOnlyList<ModuleDiscoveryEntry>> GetCachedEntriesAsync(CancellationToken cancellationToken = default)
    {
        var cacheFile = Path.Combine(_cachePath, "discovery_cache.json");
        if (!File.Exists(cacheFile)) return Array.Empty<ModuleDiscoveryEntry>();

        await using var stream = File.OpenRead(cacheFile);
        var result = await JsonSerializer.DeserializeAsync<List<ModuleDiscoveryEntry>>(stream, ModuleManager.JsonOptions, cancellationToken: cancellationToken);
        return result ?? new List<ModuleDiscoveryEntry>();
    }

    private async Task<List<ModuleDiscoveryEntry>> FetchEntriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("https://api.github.com/search/repositories?q=shuruk-module+in:topics&per_page=100", cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var entries = new List<ModuleDiscoveryEntry>();

            if (result.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    entries.Add(new ModuleDiscoveryEntry
                    {
                        Name = item.GetProperty("name").GetString() ?? "",
                        DisplayName = item.GetProperty("name").GetString() ?? "",
                        Description = item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                        Version = "1.0.0",
                        Repository = item.GetProperty("html_url").GetString() ?? "",
                        Source = ModuleSource.External,
                        Category = null,
                        Downloads = item.TryGetProperty("stargazers_count", out var stars) ? stars.GetInt32() : 0,
                        Rating = 0,
                        UpdatedAt = item.TryGetProperty("updated_at", out var updated)
                        && DateTime.TryParse(updated.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                        ? parsed : DateTime.UtcNow,
                        Icon = null,
                        Installed = false
                    });
                }
            }

            _cachedEntries = entries;
            _isOffline = false;
            await SaveCacheAsync(entries, cancellationToken);
            return entries;
        }
        catch (HttpRequestException)
        {
            _isOffline = true;
            if (_cachedEntries is not null) return _cachedEntries;

            var cached = await GetCachedEntriesAsync(cancellationToken);
            _cachedEntries = cached.ToList();
            return _cachedEntries;
        }
    }

    private async Task<ModuleManifest?> FetchManifestFromRepositoryAsync(string repositoryUrl, CancellationToken cancellationToken)
    {
        var apiUri = ConvertToApiUrl(repositoryUrl);
        if (apiUri is null) return null;

        try
        {
            var manifestUrl = $"{apiUri}/contents/manifest.json";
            var response = await _httpClient.GetAsync(manifestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var content = result.GetProperty("content").GetString()!;
            var json = System.Convert.FromBase64String(content.Replace("\n", ""));

            return await JsonSerializer.DeserializeAsync<ModuleManifest>(new MemoryStream(json), ModuleManager.JsonOptions, cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task DownloadAndInstallAsync(ModuleManifest manifest, string repositoryUrl, CancellationToken cancellationToken)
    {
        if (!IsSafeModuleName(manifest.Name))
            throw new InvalidOperationException($"Unsafe module name: '{manifest.Name}'");

        var moduleDir = Path.Combine(_installPath, manifest.Name);
        var resolvedDir = Path.GetFullPath(moduleDir);
        if (!resolvedDir.StartsWith(Path.GetFullPath(_installPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Module directory '{moduleDir}' escapes install path.");

        Directory.CreateDirectory(resolvedDir);

        var manifestJson = JsonSerializer.Serialize(manifest, ModuleManager.JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(resolvedDir, "manifest.json"), manifestJson, cancellationToken);

        await _moduleManager.RegisterModuleAsync(manifest, resolvedDir, cancellationToken);
        await _moduleManager.InstallModuleAsync(manifest.Name, cancellationToken);
    }

    private async Task SaveCacheAsync(List<ModuleDiscoveryEntry> entries, CancellationToken cancellationToken)
    {
        var cacheFile = Path.Combine(_cachePath, "discovery_cache.json");
        await using var stream = File.Create(cacheFile);
        await JsonSerializer.SerializeAsync(stream, entries, ModuleManager.JsonOptions, cancellationToken: cancellationToken);
    }

    private static string? ConvertToApiUrl(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
            return null;

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2) return null;

        return $"https://api.github.com/repos/{segments[0]}/{segments[1]}";
    }

    private static bool IsSafeModuleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (Path.IsPathRooted(name)) return false;
        if (name.Contains("..")) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return true;
    }
}
