using System.Diagnostics;
using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.Host.Services;

public class ModuleResourceUsage
{
    public string ModuleName { get; init; } = string.Empty;
    public ModuleState State { get; init; }
    public double CpuUsagePercent { get; init; }
    public long MemoryUsageBytes { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public class StatusMonitor : IDisposable
{
    private readonly IModuleManager _moduleManager;
    private readonly Dictionary<string, List<ModuleResourceUsage>> _history = new();
    private readonly object _lock = new();
    private readonly Timer _timer;
    private readonly int _historyRetentionCount;
    private int _isCollecting;

    public event EventHandler<IReadOnlyList<ModuleResourceUsage>>? ResourceUsageUpdated;

    public StatusMonitor(IModuleManager moduleManager, TimeSpan interval, int historyRetentionCount = 60)
    {
        _moduleManager = moduleManager;
        _historyRetentionCount = historyRetentionCount;
        _timer = new Timer(OnTimerTick, null, interval, interval);
    }

    public IReadOnlyList<ModuleResourceUsage> GetLatestSnapshot()
    {
        lock (_lock)
        {
            return _history.Values
                .Select(h => h.LastOrDefault())
                .Where(h => h is not null)
                .Select(h => h!)
                .ToList();
        }
    }

    public IReadOnlyList<ModuleResourceUsage> GetModuleHistory(string moduleName)
    {
        lock (_lock)
        {
            return _history.TryGetValue(moduleName, out var history)
                ? history.ToList()
                : Array.Empty<ModuleResourceUsage>();
        }
    }

    private async void OnTimerTick(object? state)
    {
        // Reentrancy guard: skip this tick if the previous one is still running
        if (Interlocked.CompareExchange(ref _isCollecting, 1, 0) != 0)
            return;

        try
        {
            var modules = _moduleManager.GetInstalledModules();
            var snapshot = new List<ModuleResourceUsage>();

            foreach (var module in modules)
            {
                var moduleState = await _moduleManager.GetModuleStateAsync(module.Name);

                var usage = new ModuleResourceUsage
                {
                    ModuleName = module.Name,
                    State = moduleState,
                    CpuUsagePercent = EstimateCpuUsage(module.Name, moduleState),
                    MemoryUsageBytes = EstimateMemoryUsage(module.Name, moduleState)
                };

                lock (_lock)
                {
                    if (!_history.ContainsKey(module.Name))
                        _history[module.Name] = new List<ModuleResourceUsage>();

                    _history[module.Name].Add(usage);

                    if (_history[module.Name].Count > _historyRetentionCount)
                        _history[module.Name].RemoveRange(0, _history[module.Name].Count - _historyRetentionCount);
                }

                snapshot.Add(usage);
            }

            ResourceUsageUpdated?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StatusMonitor] Error collecting status: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _isCollecting, 0);
        }
    }

    /// <summary>
    /// Placeholder: estimates CPU usage for a running module.
    /// Returns -1 (unknown) for running modules; real implementation requires per-process CPU sampling.
    /// </summary>
    private static double EstimateCpuUsage(string moduleName, ModuleState state)
    {
        if (state != ModuleState.Running) return 0;
        return -1; // Sentinel: unknown / not yet implemented
    }

    /// <summary>
    /// Placeholder: estimates memory usage for a running module.
    /// Returns -1 (unknown) for running modules; real implementation requires per-process memory querying.
    /// </summary>
    private static long EstimateMemoryUsage(string moduleName, ModuleState state)
    {
        if (state != ModuleState.Running) return 0;
        return -1; // Sentinel: unknown / not yet implemented
    }

    public void Stop()
    {
        _timer.Dispose();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}