using Microsoft.Data.Sqlite;

namespace ShuRuk.Infrastructure.Data;

public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS module_registry (
                    name TEXT PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    version TEXT NOT NULL,
                    description TEXT NOT NULL,
                    entry TEXT NOT NULL,
                    permissions TEXT NOT NULL,
                    min_host_version TEXT,
                    icon TEXT,
                    source INTEGER NOT NULL,
                    submodule_path TEXT,
                    repository TEXT,
                    signature TEXT,
                    is_uninstalled INTEGER NOT NULL DEFAULT 0,
                    uninstalled_at TEXT,
                    state INTEGER NOT NULL DEFAULT 0
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS file_notes (
                    file_path TEXT PRIMARY KEY,
                    content TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    color TEXT
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS file_identities (
                    identity_id TEXT PRIMARY KEY,
                    file_path TEXT NOT NULL,
                    current_file_path TEXT,
                    created_at TEXT NOT NULL,
                    tracking_enabled INTEGER NOT NULL DEFAULT 1
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS file_snapshots (
                    snapshot_id TEXT PRIMARY KEY,
                    file_identity TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    current_file_path TEXT,
                    timestamp TEXT NOT NULL,
                    size INTEGER NOT NULL,
                    original_size INTEGER,
                    checksum TEXT NOT NULL,
                    auto_created INTEGER NOT NULL DEFAULT 1,
                    change_type INTEGER NOT NULL,
                    snapshot_type INTEGER NOT NULL DEFAULT 0,
                    base_snapshot_id TEXT,
                    compressed INTEGER NOT NULL DEFAULT 0,
                    deduplication_refs TEXT,
                    copied_from_file_identity TEXT,
                    copied_from_snapshot_id TEXT,
                    FOREIGN KEY (file_identity) REFERENCES file_identities(identity_id)
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS folder_icon_configs (
                    config_id TEXT PRIMARY KEY,
                    folder_path TEXT NOT NULL UNIQUE,
                    icon_source_type INTEGER NOT NULL,
                    icon_source_ref TEXT,
                    folder_color TEXT,
                    custom_color TEXT,
                    overlay_position TEXT,
                    overlay_scale REAL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS automation_flows (
                    flow_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    enabled INTEGER NOT NULL DEFAULT 1,
                    triggers TEXT NOT NULL,
                    conditions TEXT NOT NULL,
                    actions TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS download_tasks (
                    task_id TEXT PRIMARY KEY,
                    url TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    save_path TEXT NOT NULL,
                    file_size INTEGER NOT NULL DEFAULT 0,
                    downloaded INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL DEFAULT 0,
                    thread_count INTEGER NOT NULL DEFAULT 8,
                    speed_limit INTEGER,
                    created_at TEXT NOT NULL
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS virtual_canvas_configs (
                    canvas_id TEXT PRIMARY KEY,
                    width REAL NOT NULL,
                    height REAL NOT NULL,
                    viewport_x REAL NOT NULL DEFAULT 0,
                    viewport_y REAL NOT NULL DEFAULT 0,
                    associated_desktop_id TEXT,
                    monitor_mappings TEXT,
                    updated_at TEXT NOT NULL
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS gesture_configs (
                    gesture_id TEXT PRIMARY KEY,
                    type INTEGER NOT NULL,
                    pattern TEXT NOT NULL,
                    fingers INTEGER NOT NULL DEFAULT 0,
                    action TEXT NOT NULL,
                    enabled INTEGER NOT NULL DEFAULT 1
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS configurations (
                    key TEXT NOT NULL,
                    module TEXT NOT NULL DEFAULT '',
                    value TEXT NOT NULL,
                    PRIMARY KEY (key, module)
                )");

            await ExecuteNonQueryAsync(connection, cancellationToken, @"
                CREATE TABLE IF NOT EXISTS password_entries (
                    entry_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    username TEXT NOT NULL,
                    password TEXT NOT NULL,
                    url TEXT,
                    totp_secret TEXT,
                    notes TEXT,
                    category TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                )");

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, CancellationToken cancellationToken, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}