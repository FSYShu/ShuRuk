using System.Text.Json;
using Microsoft.Win32;
using ShuRuk.Runtime.Abstractions.Models;
using WinReg = Microsoft.Win32.Registry;

namespace ShuRuk.Runtime.Abstractions.Services;

// Global installation registry: single JSON DB with file lock / 全局安装注册表，单文件JSON+文件锁
public sealed class GlobalRegistry : IDisposable
{
    private readonly string _path;
    private readonly object _lock = new();
    private RegistryData? _cache;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GlobalRegistry(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ShuRuk", "registry.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public RegistryData Load()
    {
        lock (_lock)
        {
            if (_cache is not null) return _cache;
            if (!File.Exists(_path)) { _cache = new RegistryData(); return _cache; }
            var json = ReadWithLock();
            _cache = JsonSerializer.Deserialize<RegistryData>(json, JsonOpts) ?? new RegistryData();
            return _cache;
        }
    }

    public void Save(RegistryData data)
    {
        lock (_lock)
        {
            data.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            var json = JsonSerializer.Serialize(data, JsonOpts);
            WriteWithLock(json);
            _cache = data;
        }
    }

    public RuntimeRecord GetRuntime() => Load().Runtime;
    public void SaveRuntime(RuntimeRecord r) { var d = Load(); d.Runtime = r; Save(d); }

    public HostRecord GetHost() => Load().Host;
    public void SaveHost(HostRecord h) { var d = Load(); d.Host = h; Save(d); }

    public IReadOnlyList<ModuleRecord> GetModules() => Load().Modules;

    public void AddOrUpdateModule(ModuleRecord m)
    {
        var d = Load();
        var idx = d.Modules.FindIndex(x => x.Id == m.Id);
        if (idx >= 0) d.Modules[idx] = m; else d.Modules.Add(m);
        Save(d);
    }

    public bool RemoveModule(string id)
    {
        var d = Load();
        var removed = d.Modules.RemoveAll(x => x.Id == id) > 0;
        if (removed) Save(d);
        return removed;
    }

    // Compatibility check: runtime version >= required / 兼容判定：运行时版本不低于要求
    public bool IsRuntimeCompatible(string? minRuntimeVersion)
    {
        if (string.IsNullOrWhiteSpace(minRuntimeVersion)) return true;
        var current = GetRuntime().Version;
        if (string.IsNullOrWhiteSpace(current)) return false;
        return Version.TryParse(current, out var cur) && Version.TryParse(minRuntimeVersion, out var min) && cur >= min;
    }

    // ---- Registry-based refcount for runtime lifecycle / 注册表引用计数管理运行时生命周期 ----

    // Registry path for runtime refcount / 运行时引用计数注册表路径
    private const string RuntimeRegKey = @"Software\ShuRuk\Runtime";
    private const string RefCountValue = "RefCount";
    private const string InstalledAppsValue = "InstalledApps";

    // Register app install: add to InstalledApps (dedup), RefCount+1 / 注册应用安装：加入已安装列表(去重)，引用计数+1
    public void RegisterAppInstall(string packageFullName)
    {
        using var key = WinReg.CurrentUser.CreateSubKey(RuntimeRegKey, writable: true);
        var existing = ReadInstalledApps(key);
        if (existing.Contains(packageFullName, StringComparer.OrdinalIgnoreCase))
            return; // Already registered, skip / 已注册，跳过
        var updated = existing.Append(packageFullName).ToArray();
        key.SetValue(InstalledAppsValue, updated, RegistryValueKind.MultiString);
        var current = ReadRefCount(key);
        key.SetValue(RefCountValue, current + 1, RegistryValueKind.DWord);
    }

    // Unregister app install: remove from InstalledApps, RefCount-1 / 注销应用安装：移除，引用计数-1，返回是否归零
    public bool UnregisterAppInstall(string packageFullName)
    {
        using var key = WinReg.CurrentUser.OpenSubKey(RuntimeRegKey, writable: true);
        if (key is null) return false;
        var existing = ReadInstalledApps(key);
        var updated = existing.Where(p => !string.Equals(p, packageFullName, StringComparison.OrdinalIgnoreCase)).ToArray();
        key.SetValue(InstalledAppsValue, updated, RegistryValueKind.MultiString);
        var current = ReadRefCount(key);
        var next = Math.Max(0, current - 1);
        key.SetValue(RefCountValue, next, RegistryValueKind.DWord);
        return next == 0;
    }

    // Get current refcount / 获取当前引用计数
    public int GetRefCount()
    {
        using var key = WinReg.CurrentUser.OpenSubKey(RuntimeRegKey, writable: false);
        return key is null ? 0 : ReadRefCount(key);
    }

    // Get installed apps list / 获取已安装包列表
    public IReadOnlyList<string> GetInstalledApps()
    {
        using var key = WinReg.CurrentUser.OpenSubKey(RuntimeRegKey, writable: false);
        return key is null ? Array.Empty<string>() : ReadInstalledApps(key).ToArray();
    }

    // Reconcile InstalledApps against actual installed packages, fix dirty data / 兜底对账：按实际已装包修正脏数据
    public void ReconcileInstalledApps(IEnumerable<string> actualInstalledPackages)
    {
        using var key = WinReg.CurrentUser.CreateSubKey(RuntimeRegKey, writable: true);
        var actual = actualInstalledPackages.ToList();
        var recorded = ReadInstalledApps(key);
        var reconciled = recorded.Where(p => actual.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
        key.SetValue(InstalledAppsValue, reconciled, RegistryValueKind.MultiString);
        key.SetValue(RefCountValue, reconciled.Length, RegistryValueKind.DWord);
    }

    private static int ReadRefCount(RegistryKey key) =>
        key.GetValue(RefCountValue, 0) is int v ? v : 0;

    private static string[] ReadInstalledApps(RegistryKey key) =>
        (string[]?)key.GetValue(InstalledAppsValue, Array.Empty<string>(), RegistryValueOptions.DoNotExpandEnvironmentNames)
        ?? Array.Empty<string>();

    private string ReadWithLock()
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    private void WriteWithLock(string json)
    {
        using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sw = new StreamWriter(fs);
        sw.Write(json);
    }

    public void Dispose() { }
}