using System.Text.Json;

namespace ShuRuk.Infrastructure.Services;

public class AuditLogService
{
    private readonly string _logDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Initializes the audit log service and ensures the specified log directory exists.
    /// </summary>
    /// <param name="logDirectory">The directory where audit log files are stored.</param>
    public AuditLogService(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>
    /// Records an audit event in the log for its UTC calendar date.
    /// </summary>
    /// <param name="category">The category of the audit event.</param>
    /// <param name="action">The action being recorded.</param>
    /// <param name="details">Optional additional information about the event.</param>
    /// <param name="module">Optional identifier of the module associated with the event.</param>
    public async Task LogAsync(string category, string action, string? details = null, string? module = null, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Category = category,
            Action = action,
            Details = details,
            Module = module
        };

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var fileName = $"audit_{entry.Timestamp:yyyyMMdd}.jsonl";
            var filePath = Path.Combine(_logDirectory, fileName);
            var json = JsonSerializer.Serialize(entry);

            await using var writer = new StreamWriter(filePath, append: true);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Queries audit entries recorded for a specified date, with optional category and module filters.
    /// </summary>
    /// <param name="date">The date of the audit entries to query.</param>
    /// <param name="category">An optional category to match exactly.</param>
    /// <param name="module">An optional module identifier to match exactly.</param>
    /// <returns>The matching audit entries, or an empty collection when no log file exists for the date.</returns>
    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(DateTime date, string? category = null, string? module = null, CancellationToken cancellationToken = default)
    {
        var fileName = $"audit_{date:yyyyMMdd}.jsonl";
        var filePath = Path.Combine(_logDirectory, fileName);

        if (!File.Exists(filePath)) return Array.Empty<AuditLogEntry>();

        var entries = new List<AuditLogEntry>();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
            if (entry is null) continue;

            if (category is not null && entry.Category != category) continue;
            if (module is not null && entry.Module != module) continue;

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Deletes audit log files older than the specified retention period.
    /// </summary>
    /// <param name="retentionDays">The number of days of audit logs to retain.</param>
    /// <param name="cancellationToken">A token used to cancel the cleanup operation.</param>
    public async Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var file in Directory.GetFiles(_logDirectory, "audit_*.jsonl"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lastWrite = File.GetLastWriteTimeUtc(file);
            if (lastWrite < cutoff)
            {
                File.Delete(file);
            }
        }

        await Task.CompletedTask;
    }
}

public class AuditLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? Module { get; set; }
}