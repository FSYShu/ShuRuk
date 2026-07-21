namespace ShuRuk.Contracts.Interfaces;

public interface IConfigurationService
{
    /// <summary>
/// Retrieves a configuration value by key, optionally scoped to a module.
/// </summary>
/// <param name="key">The configuration key.</param>
/// <param name="module">The optional module scope.</param>
/// <param name="cancellationToken">The token used to cancel the operation.</param>
/// <returns>The configuration value converted to <typeparamref name="T"/>, or <see langword="null"/> if the key is not found.</returns>
Task<T?> GetValueAsync<T>(string key, string? module = null, CancellationToken cancellationToken = default);

    /// <summary>
/// Stores or updates a configuration value for a key, optionally within a module.
/// </summary>
/// <param name="key">The configuration key.</param>
/// <param name="value">The value to store.</param>
/// <param name="module">The optional module scope for the configuration value.</param>
/// <param name="cancellationToken">The token used to cancel the operation.</param>
Task SetValueAsync<T>(string key, T value, string? module = null, CancellationToken cancellationToken = default);

    /// <summary>
/// Removes a configuration value identified by its key.
/// </summary>
/// <param name="key">The configuration key to remove.</param>
/// <param name="module">The optional module containing the configuration value.</param>
Task RemoveKeyAsync(string key, string? module = null, CancellationToken cancellationToken = default);

    /// <summary>
/// Retrieves all configuration values for the specified module.
/// </summary>
/// <param name="module">The module whose configuration values to retrieve, or <c>null</c> for unscoped values.</param>
/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
/// <returns>A read-only dictionary containing the configuration values keyed by name.</returns>
Task<IReadOnlyDictionary<string, object>> GetAllValuesAsync(string? module = null, CancellationToken cancellationToken = default);
}