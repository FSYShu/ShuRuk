using System.Collections.Concurrent;

namespace ShuRuk.Runtime.Abstractions.Services;

// In-memory module state registry: live module list + heartbeat / 内存态模块状态注册表
public sealed class StateRegistry
{
    private readonly ConcurrentDictionary<string, ModuleStateEntry> _entries = new();

    public void Register(ModuleStateEntry entry) => _entries[entry.ModuleId] = entry;
    public bool Unregister(string moduleId) => _entries.TryRemove(moduleId, out _);
    public ModuleStateEntry? Get(string moduleId) => _entries.GetValueOrDefault(moduleId);
    public IReadOnlyCollection<ModuleStateEntry> GetAll() => _entries.Values.ToArray();

    public void UpdateHeartbeat(string moduleId, HeartbeatSnapshot snapshot)
    {
        if (_entries.TryGetValue(moduleId, out var entry))
        {
            entry.Heartbeat = snapshot;
            entry.LastSeen = DateTimeOffset.UtcNow;
        }
    }

    public void SetStatus(string moduleId, string status)
    {
        if (_entries.TryGetValue(moduleId, out var entry)) entry.Status = status;
    }
}

public sealed class ModuleStateEntry
{
    public required string ModuleId { get; init; }
    public string Status { get; set; } = "stopped";
    public int Pid { get; set; }
    public HeartbeatSnapshot Heartbeat { get; set; } = new();
    public DateTimeOffset LastSeen { get; set; }
}

public sealed class HeartbeatSnapshot
{
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }
}