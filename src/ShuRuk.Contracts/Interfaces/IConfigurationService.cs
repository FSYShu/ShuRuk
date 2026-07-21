namespace ShuRuk.Contracts.Interfaces;

public interface IConfigurationService
{
    Task<T?> GetValueAsync<T>(string key, string? module = null, CancellationToken cancellationToken = default);

    Task SetValueAsync<T>(string key, T value, string? module = null, CancellationToken cancellationToken = default);

    Task RemoveKeyAsync(string key, string? module = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>> GetAllValuesAsync(string? module = null, CancellationToken cancellationToken = default);
}