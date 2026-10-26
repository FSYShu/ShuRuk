using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using ShuRuk.Runtime.Abstractions.Interfaces;
using WinReg = Microsoft.Win32.Registry;

namespace ShuRuk.Runtime.Abstractions.Services;

// Registry-based configuration service: shared settings via HKCU\Software\ShuRuk\Settings\{scope}.
// ShuRuk and module apps read/write the same registry path for unified management.
// Multi-process sync relies on native RegNotifyChangeKeyValue (writes notify listeners).
// 基于注册表的配置服务：通过HKCU\Software\ShuRuk\Settings\{scope}共享设置。
// ShuRuk与模块APP读写同一路径统一管理。多进程同步依赖原生RegNotifyChangeKeyValue(写入通知监听者)。
public class ConfigurationService : IConfigurationService
{
    // Registry root for settings / 设置注册表根
    private const string SettingsRoot = @"Software\ShuRuk\Settings";
    // Default scope for app-level settings / 应用级设置默认作用域
    private const string HostScope = "Host";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false
    };

    // Native RegNotifyChangeKeyValue for cross-process change notification / 原生注册表变更通知API
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        IntPtr hKey, bool bWatchSubtree, int dwNotifyFilter, IntPtr hEvent, bool fAsynchronous);

    // REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_ATTRIBUTES | REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_CHANGE_SECURITY
    private const int RegNotifyAll = 0x1 | 0x2 | 0x4 | 0x8;

    // Resolve scope key path: Host for app-level, module name for module-level / 解析作用域键路径
    private static string GetScopePath(string? module) =>
        $"{SettingsRoot}\\{(string.IsNullOrWhiteSpace(module) ? HostScope : module)}";

    public Task<T?> GetValueAsync<T>(string key, string? module = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scopePath = GetScopePath(module);

        using var keyNode = WinReg.CurrentUser.OpenSubKey(scopePath, writable: false);
        if (keyNode is null) return Task.FromResult<T?>(default);

        var raw = keyNode.GetValue(key);
        if (raw is null) return Task.FromResult<T?>(default);

        // String type: return directly / string类型直接返回
        if (typeof(T) == typeof(string))
            return Task.FromResult<T?>((T?)(object?)raw.ToString());

        // Other types: JSON deserialize / 其他类型JSON反序列化
        var json = raw.ToString()!;
        return Task.FromResult(JsonSerializer.Deserialize<T>(json));
    }

    public Task SetValueAsync<T>(string key, T value, string? module = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scopePath = GetScopePath(module);

        using var keyNode = WinReg.CurrentUser.CreateSubKey(scopePath, writable: true);

        // String type: store directly / string类型直接存储
        if (typeof(T) == typeof(string))
        {
            keyNode.SetValue(key, value?.ToString() ?? string.Empty, RegistryValueKind.String);
        }
        else
        {
            // Other types: JSON serialize / 其他类型JSON序列化
            var json = JsonSerializer.Serialize(value, JsonOpts);
            keyNode.SetValue(key, json, RegistryValueKind.String);
        }

        return Task.CompletedTask;
    }

    public Task RemoveKeyAsync(string key, string? module = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scopePath = GetScopePath(module);

        using var keyNode = WinReg.CurrentUser.OpenSubKey(scopePath, writable: true);
        keyNode?.DeleteValue(key, throwOnMissingValue: false);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, object?>> GetAllValuesAsync(string? module = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scopePath = GetScopePath(module);
        var result = new Dictionary<string, object?>();

        using var keyNode = WinReg.CurrentUser.OpenSubKey(scopePath, writable: false);
        if (keyNode is not null)
        {
            foreach (var valueName in keyNode.GetValueNames())
            {
                var raw = keyNode.GetValue(valueName);
                if (raw is null) continue;

                // Attempt JSON deserialize, fallback to raw string / 尝试JSON反序列化，失败则保留原始字符串
                object? deserialized;
                try
                {
                    deserialized = JsonSerializer.Deserialize<object>(raw.ToString()!);
                }
                catch
                {
                    deserialized = raw.ToString();
                }
                result[valueName] = deserialized;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(result);
    }

    public Task<IAsyncDisposable> WatchChangesAsync(Action<string> onKeyChanged, string? module = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scopePath = GetScopePath(module);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Background loop: block on RegNotifyChangeKeyValue, invoke callback on each change.
        // 后台循环：阻塞等待注册表变更通知，每次变更触发回调。
        _ = Task.Run(() =>
        {
            try
            {
                using var keyNode = WinReg.CurrentUser.OpenSubKey(scopePath, writable: false)
                    ?? WinReg.CurrentUser.CreateSubKey(scopePath, writable: false);
                while (!cts.Token.IsCancellationRequested)
                {
                    // Synchronous block until any value/name/attribute change / 同步阻塞直到任意值/名称/属性变更
                    RegNotifyChangeKeyValue(keyNode.Handle.DangerousGetHandle(), true, RegNotifyAll, IntPtr.Zero, false);
                    if (cts.Token.IsCancellationRequested) break;
                    try { onKeyChanged?.Invoke(scopePath); }
                    catch { /* Swallow callback exceptions / 吞掉回调异常 */ }
                }
            }
            catch (OperationCanceledException) { }
            catch { /* Best-effort watch, swallow errors / 尽力监听，吞掉异常 */ }
        }, cts.Token);

        return Task.FromResult<IAsyncDisposable>(new ChangeSubscription(cts));
    }

    // IAsyncDisposable wrapper for stopping change subscription / 停止变更订阅的IAsyncDisposable包装
    private sealed class ChangeSubscription : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        public ChangeSubscription(CancellationTokenSource cts) => _cts = cts;
        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}