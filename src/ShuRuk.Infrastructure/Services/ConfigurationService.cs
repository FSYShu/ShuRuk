using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.Infrastructure.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a configuration service for the specified SQLite database file.
    /// </summary>
    /// <param name="dbPath">The path to the SQLite database file.</param>
    public ConfigurationService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// Retrieves a configuration value for the specified key and module.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="module">The module associated with the configuration value.</param>
    /// <returns>The stored value, deserialized to the requested type, or the default value if no matching configuration exists.</returns>
    public async Task<T?> GetValueAsync<T>(string key, string? module = null, CancellationToken cancellationToken = default)
    {
        var moduleValue = module ?? string.Empty;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM configurations WHERE key = @key AND module = @module";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@module", moduleValue);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null) return default;

        return typeof(T) == typeof(string)
            ? (T?)(object?)result.ToString()
            : JsonSerializer.Deserialize<T>(result.ToString()!);
    }

    /// <summary>
    /// Stores a configuration value for a key and module, replacing any existing value with the same identifiers.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="module">The module associated with the configuration value.</param>
    public async Task SetValueAsync<T>(string key, T value, string? module = null, CancellationToken cancellationToken = default)
    {
        var moduleValue = module ?? string.Empty;
        var serialized = value is string s ? s : JsonSerializer.Serialize(value);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO configurations (key, module, value) VALUES (@key, @module, @value)
            ON CONFLICT(key, module) DO UPDATE SET value = @value";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@module", moduleValue);
        command.Parameters.AddWithValue("@value", serialized);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Removes a configuration value for the specified key and module.
    /// </summary>
    /// <param name="key">The configuration key to remove.</param>
    /// <param name="module">The module associated with the configuration value, or <c>null</c> for the default module.</param>
    public async Task RemoveKeyAsync(string key, string? module = null, CancellationToken cancellationToken = default)
    {
        var moduleValue = module ?? string.Empty;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM configurations WHERE key = @key AND module = @module";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@module", moduleValue);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves all configuration values for a module.
    /// </summary>
    /// <param name="module">The module whose configuration values are retrieved.</param>
    /// <returns>A dictionary mapping configuration keys to their stored values.</returns>
    public async Task<IReadOnlyDictionary<string, object>> GetAllValuesAsync(string? module = null, CancellationToken cancellationToken = default)
    {
        var moduleValue = module ?? string.Empty;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM configurations WHERE module = @module";
        command.Parameters.AddWithValue("@module", moduleValue);

        var result = new Dictionary<string, object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }
}