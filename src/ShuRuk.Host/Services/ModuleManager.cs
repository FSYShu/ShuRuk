using System.Reflection;
using System.Text.Json;
using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Interfaces;
using ShuRuk.Contracts.Models;
using ShuRuk.Infrastructure.Data;
using ShuRuk.Runtime;

namespace ShuRuk.Host.Services;

public class ModuleManager : IModuleManager
{
    private readonly Dictionary<string, ModuleInfo> _modules = new();
    private readonly Dictionary<string, IModule> _loadedModules = new();
    private readonly Dictionary<string, IModuleContext> _moduleContexts = new();
    private readonly ModuleManifestValidator _validator = new();
    private readonly DatabaseInitializer _dbInitializer;
    private readonly IConfigurationService _configurationService;
    private readonly string _modulesPath;
    private readonly string _dataPath;

    public event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;

    public ModuleManager(DatabaseInitializer dbInitializer, IConfigurationService configurationService, string modulesPath, string dataPath)
    {
        _dbInitializer = dbInitializer;
        _configurationService = configurationService;
        _modulesPath = modulesPath;
        _dataPath = dataPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _dbInitializer.InitializeAsync(cancellationToken);
        await DiscoverBuiltInModulesAsync(cancellationToken);
    }

    private async Task DiscoverBuiltInModulesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_modulesPath)) return;

        foreach (var dir in Directory.GetDirectories(_modulesPath))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            ModuleManifest? manifest;
            try
            {
                manifest = await JsonSerializer.DeserializeAsync<ModuleManifest>(
                    File.OpenRead(manifestPath), cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                // Skip malformed manifests and continue scanning
                continue;
            }

            if (manifest is null) continue;

            var validationResult = _validator.Validate(manifest);
            if (!validationResult.IsValid) continue;

            var info = new ModuleInfo
            {
                Manifest = manifest,
                State = ModuleState.Discovered,
                Source = ModuleSource.BuiltIn,
                SubmodulePath = dir
            };

            _modules[manifest.Name] = info;
            TransitionState(manifest.Name, ModuleState.Discovered, ModuleState.Validated);
        }
    }

    public Task<ModuleState> GetModuleStateAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (_modules.TryGetValue(moduleName, out var info))
            return Task.FromResult(info.State);

        throw new KeyNotFoundException($"Module '{moduleName}' not found.");
    }

    public async Task InstallModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
        {
            // Bootstrap from on-disk manifest for externally installed modules
            var moduleDir = Path.Combine(_modulesPath, moduleName);
            var manifestPath = Path.Combine(moduleDir, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new KeyNotFoundException($"Module '{moduleName}' not found.");

            ModuleManifest manifest;
            try
            {
                using var stream = File.OpenRead(manifestPath);
                manifest = await JsonSerializer.DeserializeAsync<ModuleManifest>(stream, cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException($"Cannot parse manifest for '{moduleName}'.");
            }
            catch (Exception ex) when (ex is not KeyNotFoundException and not InvalidOperationException)
            {
                throw new InvalidOperationException($"Cannot parse manifest for '{moduleName}'.", ex);
            }

            info = new ModuleInfo
            {
                Manifest = manifest,
                State = ModuleState.Discovered,
                Source = manifest.Source,
                SubmodulePath = moduleDir
            };
            _modules[moduleName] = info;
            TransitionState(moduleName, ModuleState.Discovered, ModuleState.Validated);
        }

        if (info.State != ModuleState.Validated && info.State != ModuleState.Discovered)
            throw new InvalidOperationException($"Cannot install module '{moduleName}' in state {info.State}.");

        if (info.Manifest.Source == ModuleSource.BuiltIn)
        {
            TransitionState(moduleName, info.State, ModuleState.Installed);
            return;
        }

        TransitionState(moduleName, info.State, ModuleState.Installed);
        await Task.CompletedTask;
    }

    public async Task UninstallModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.State == ModuleState.Running || info.State == ModuleState.Loaded)
            await UnloadModuleAsync(moduleName, cancellationToken);

        if (info.Manifest.Source == ModuleSource.BuiltIn)
        {
            info.IsUninstalled = true;
            info.UninstalledAt = DateTime.UtcNow;
            TransitionState(moduleName, info.State, ModuleState.Uninstalled);
            return;
        }

        if (info.SubmodulePath is not null && Directory.Exists(info.SubmodulePath))
        {
            Directory.Delete(info.SubmodulePath, recursive: true);
        }

        var moduleDataPath = Path.Combine(_dataPath, moduleName);
        if (Directory.Exists(moduleDataPath))
        {
            Directory.Delete(moduleDataPath, recursive: true);
        }

        _modules.Remove(moduleName);
        await Task.CompletedTask;
    }

    public async Task LoadModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.State != ModuleState.Installed)
            throw new InvalidOperationException($"Cannot load module '{moduleName}' in state {info.State}.");

        var module = ResolveModule(info);
        if (module is null)
            throw new InvalidOperationException($"Cannot resolve module instance for '{moduleName}'.");

        var context = CreateModuleContext(moduleName);
        try
        {
            await module.OnLoadAsync(context, cancellationToken);
            _loadedModules[moduleName] = module;
            _moduleContexts[moduleName] = context;
            TransitionState(moduleName, info.State, ModuleState.Loaded);
        }
        catch
        {
            TransitionState(moduleName, info.State, ModuleState.LoadFailed);
            throw;
        }
    }

    public async Task UnloadModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.State == ModuleState.Running)
            await StopModuleAsync(moduleName, cancellationToken);

        if (!_loadedModules.TryGetValue(moduleName, out var module))
            return;

        await module.OnUnloadAsync(cancellationToken);
        _loadedModules.Remove(moduleName);
        _moduleContexts.Remove(moduleName);
        TransitionState(moduleName, info.State, ModuleState.Unloaded);
    }

    public async Task StartModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.State == ModuleState.Installed)
            await LoadModuleAsync(moduleName, cancellationToken);

        if (info.State != ModuleState.Loaded)
            throw new InvalidOperationException($"Cannot start module '{moduleName}' in state {info.State}.");

        TransitionState(moduleName, info.State, ModuleState.Running);
    }

    public Task StopModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.State != ModuleState.Running)
            throw new InvalidOperationException($"Cannot stop module '{moduleName}' in state {info.State}.");

        TransitionState(moduleName, info.State, ModuleState.Loaded);
        return Task.CompletedTask;
    }

    public async Task PauseModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.State != ModuleState.Running)
            throw new InvalidOperationException($"Cannot pause module '{moduleName}' in state {info.State}.");

        if (_loadedModules.TryGetValue(moduleName, out var module))
        {
            await module.OnPauseAsync(cancellationToken);
        }

        TransitionState(moduleName, info.State, ModuleState.Paused);
    }

    public async Task ResumeModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.State != ModuleState.Paused)
            throw new InvalidOperationException($"Cannot resume module '{moduleName}' in state {info.State}.");

        if (_loadedModules.TryGetValue(moduleName, out var module))
        {
            await module.OnResumeAsync(cancellationToken);
        }

        TransitionState(moduleName, info.State, ModuleState.Running);
    }

    public async Task ReinstallBuiltInModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleName, out var info))
            throw new KeyNotFoundException($"Module '{moduleName}' not found.");

        if (info.Manifest.Source != ModuleSource.BuiltIn)
            throw new InvalidOperationException($"Module '{moduleName}' is not a built-in module.");

        info.IsUninstalled = false;
        info.UninstalledAt = null;
        TransitionState(moduleName, info.State, ModuleState.Installed);
        await Task.CompletedTask;
    }

    public IReadOnlyList<ModuleManifest> GetInstalledModules()
    {
        return _modules.Values
            .Where(m => m.State is ModuleState.Installed or ModuleState.Loaded or ModuleState.Running or ModuleState.Paused)
            .Select(m => m.Manifest)
            .ToList();
    }

    private void TransitionState(string moduleName, ModuleState oldState, ModuleState newState)
    {
        if (_modules.TryGetValue(moduleName, out var info))
        {
            info.State = newState;
        }

        ModuleStateChanged?.Invoke(this, new ModuleStateChangedEventArgs
        {
            ModuleName = moduleName,
            OldState = oldState,
            NewState = newState
        });
    }

    private IModule? ResolveModule(ModuleInfo info)
    {
        var submodulePath = info.SubmodulePath;
        if (submodulePath is null) return null;

        var dllFiles = Directory.GetFiles(submodulePath, "*.dll", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("ShuRuk.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var dll in dllFiles)
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var moduleType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsClass: true });

                if (moduleType is null) continue;

                return Activator.CreateInstance(moduleType) as IModule;
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private IModuleContext CreateModuleContext(string moduleName)
    {
        var moduleDataPath = Path.Combine(_dataPath, moduleName);
        Directory.CreateDirectory(moduleDataPath);

        return new DefaultModuleContext(moduleDataPath, _configurationService);
    }
}

internal class DefaultModuleContext : IModuleContext
{
    private readonly Dictionary<Type, object> _services = new();

    public DefaultModuleContext(string dataPath, IConfigurationService configurationService)
    {
        DataPath = dataPath;
        Configuration = configurationService;
    }

    public T? GetService<T>() where T : class
    {
        return _services.TryGetValue(typeof(T), out var service) ? service as T : null;
    }

    public string DataPath { get; }

    public IConfigurationService Configuration { get; }

    public void RegisterSettingsPage(object page) { }
}