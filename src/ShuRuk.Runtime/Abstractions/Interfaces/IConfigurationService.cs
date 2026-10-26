namespace ShuRuk.Runtime.Abstractions.Interfaces;

public interface IConfigurationService
{
    Task<T?> GetValueAsync<T>(string key, string? module = null, CancellationToken cancellationToken = default);

    Task SetValueAsync<T>(string key, T value, string? module = null, CancellationToken cancellationToken = default);

    Task RemoveKeyAsync(string key, string? module = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>> GetAllValuesAsync(string? module = null, CancellationToken cancellationToken = default);

    // Subscribe to value changes in a scope. Callback fires on any write/remove.
    // Returns IAsyncDisposable to stop watching. Uses native RegNotifyChangeKeyValue.
    // 订阅作用域内值变更，任意写入/移除触发回调。返回IAsyncDisposable停止监听，基于原生RegNotifyChangeKeyValue。
    Task<IAsyncDisposable> WatchChangesAsync(Action<string> onKeyChanged, string? module = null, CancellationToken cancellationToken = default);
}