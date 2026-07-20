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

public class StatusMonitor
{
    private readonly IModuleManager _moduleManager;
    private readonly Dictionary<string, List<ModuleResourceUsage>> _history = new();
    private readonly Timer _timer;
    private readonly int _historyRetentionCount;

    public event EventHandler<IReadOnlyList<ModuleResourceUsage>>? ResourceUsageUpdated;

    public StatusMonitor(IModuleManager moduleManager, TimeSpan interval, int historyRetentionCount = 60)
    {
        _moduleManager = moduleManager;
        _historyRetentionCount = historyRetentionCount;
        _timer = new Timer(OnTimerTick, null, interval, interval);
    }

    public IReadOnlyList<ModuleResourceUsage> GetLatestSnapshot()
    {
        return _history.Values
            .Select(h => h.LastOrDefault())
            .Where(h => h is not null)
            .Select(h => h!)
            .ToList();
    }

    public IReadOnlyList<ModuleResourceUsage> GetModuleHistory(string moduleName)
    {
        return _history.TryGetValue(moduleName, out var history)
            ? history
            : Array.Empty<ModuleResourceUsage>();
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            var modules = _moduleManager.GetInstalledModules();
            var currentProcess = Process.GetCurrentProcess();
            var snapshot = new List<ModuleResourceUsage>();

            foreach (var module in modules)
            {
                var moduleState = _moduleManager.GetModuleStateAsync(module.Name).GetAwaiter().GetResult();

                var usage = new ModuleResourceUsage
                {
                    ModuleName = module.Name,
                    State = moduleState,
                    CpuUsagePercent = EstimateCpuUsage(module.Name, moduleState),
                    MemoryUsageBytes = EstimateMemoryUsage(module.Name, moduleState)
                };

                if (!_history.ContainsKey(module.Name))
                    _history[module.Name] = new List<ModuleResourceUsage>();

                _history[module.Name].Add(usage);

                if (_history[module.Name].Count > _historyRetentionCount)
                    _history[module.Name].RemoveRange(0, _history[module.Name].Count - _historyRetentionCount);

                snapshot.Add(usage);
            }

            ResourceUsageUpdated?.Invoke(this, snapshot);
        }
        catch
        {
        }
    }

    private static double EstimateCpuUsage(string moduleName, ModuleState state)
    {
        if (state != ModuleState.Running) return 0;
        return 0;
    }

    private static long EstimateMemoryUsage(string moduleName, ModuleState state)
    {
        if (state != ModuleState.Running) return 0;
        return 0;
    }

    public void Stop()
    {
        _timer.Dispose();
    }
}