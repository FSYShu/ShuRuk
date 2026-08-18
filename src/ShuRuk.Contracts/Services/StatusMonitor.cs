using System.Diagnostics;
using System.Runtime.InteropServices;
using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Interfaces;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Contracts.Services;

public class ModuleResourceUsage
{
    public string ModuleName { get; init; } = string.Empty;
    public int Pid { get; init; }
    public ModuleState State { get; init; }
    public double CpuUsagePercent { get; init; }
    public long MemoryUsageBytes { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public class StatusMonitor
{
    // Host process display name / 宿主进程显示名
    public const string HostProcessName = "<host>";

    private readonly IModuleManager _moduleManager;
    private readonly ITestModeService? _testModeService;
    private readonly Dictionary<string, List<ModuleResourceUsage>> _history = new();
    private readonly Dictionary<int, (TimeSpan TotalProcessorTime, DateTime Timestamp)> _lastCpuSample = new();
    private readonly object _lock = new();
    private readonly Timer _timer;
    private readonly TimeSpan _interval;
    private readonly int _historyRetentionCount;
    private bool _isRunning;

    public event EventHandler<IReadOnlyList<ModuleResourceUsage>>? ResourceUsageUpdated;

    public StatusMonitor(IModuleManager moduleManager, ITestModeService? testModeService, TimeSpan interval, int historyRetentionCount = 60)
    {
        _moduleManager = moduleManager;
        _testModeService = testModeService;
        _interval = interval;
        _historyRetentionCount = historyRetentionCount;
        _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _timer.Change(TimeSpan.Zero, _interval);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsRunning => _isRunning;

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

    // Async timer tick to avoid blocking on .GetAwaiter().GetResult()
    // 异步定时器回调避免同步阻塞
    private async void OnTimerTick(object? state)
    {
        try
        {
            if (_testModeService?.IsEnabled == true)
            {
                var testSnapshot = TestDataProvider.GenerateSnapshot();
                lock (_lock)
                {
                    foreach (var entry in testSnapshot)
                    {
                        if (!_history.ContainsKey(entry.ModuleName))
                            _history[entry.ModuleName] = new List<ModuleResourceUsage>();
                        _history[entry.ModuleName].Add(entry);
                        if (_history[entry.ModuleName].Count > _historyRetentionCount)
                            _history[entry.ModuleName].RemoveRange(0, _history[entry.ModuleName].Count - _historyRetentionCount);
                    }
                }
                ResourceUsageUpdated?.Invoke(this, testSnapshot);
                return;
            }

            var moduleInfos = _moduleManager.GetInstalledModuleInfos();
            var snapshot = new List<ModuleResourceUsage>();

            var validNames = new HashSet<string> { HostProcessName };
            foreach (var info in moduleInfos)
                validNames.Add(info.Manifest.Name);

            lock (_lock)
            {

                var keysToRemove = _history.Keys.Where(k => !validNames.Contains(k)).ToList();
                foreach (var key in keysToRemove)
                    _history.Remove(key);
            }

            var hostPid = Environment.ProcessId;
            var hostUsage = new ModuleResourceUsage
            {
                ModuleName = HostProcessName,
                Pid = hostPid,
                State = ModuleState.Running,
                CpuUsagePercent = EstimateCpuUsage(hostPid, ModuleState.Running),
                MemoryUsageBytes = EstimateMemoryUsage(hostPid, ModuleState.Running)
            };

            lock (_lock)
            {
                if (!_history.ContainsKey(HostProcessName))
                    _history[HostProcessName] = new List<ModuleResourceUsage>();
                _history[HostProcessName].Add(hostUsage);
                if (_history[HostProcessName].Count > _historyRetentionCount)
                    _history[HostProcessName].RemoveRange(0, _history[HostProcessName].Count - _historyRetentionCount);
            }
            snapshot.Add(hostUsage);

            foreach (var info in moduleInfos)
            {
                var moduleState = _moduleManager.GetModuleStateAsync(info.Manifest.Name).GetAwaiter().GetResult();
                var pid = info.Pid;

                var usage = new ModuleResourceUsage
                {
                    ModuleName = info.Manifest.Name,
                    Pid = pid,
                    State = moduleState,
                    CpuUsagePercent = EstimateCpuUsage(pid, moduleState),
                    MemoryUsageBytes = EstimateMemoryUsage(pid, moduleState)
                };

                lock (_lock)
                {
                    if (!_history.ContainsKey(info.Manifest.Name))
                        _history[info.Manifest.Name] = new List<ModuleResourceUsage>();

                    _history[info.Manifest.Name].Add(usage);

                    if (_history[info.Manifest.Name].Count > _historyRetentionCount)
                        _history[info.Manifest.Name].RemoveRange(0, _history[info.Manifest.Name].Count - _historyRetentionCount);
                }

                snapshot.Add(usage);
            }

            ResourceUsageUpdated?.Invoke(this, snapshot);
        }
        catch (Exception)
        {
        }
    }

    private double EstimateCpuUsage(int pid, ModuleState state)
    {
        if (state != ModuleState.Running || pid <= 0) return 0;

        try
        {
            var process = Process.GetProcessById(pid);
            var currentCpuTime = process.TotalProcessorTime;
            var now = DateTime.UtcNow;

            if (_lastCpuSample.TryGetValue(pid, out var last))
            {
                var cpuDelta = currentCpuTime - last.TotalProcessorTime;
                var wallDelta = now - last.Timestamp;
                var percent = wallDelta.TotalMilliseconds > 0
                    ? cpuDelta.TotalMilliseconds / wallDelta.TotalMilliseconds / Environment.ProcessorCount * 100.0
                    : 0;
                _lastCpuSample[pid] = (currentCpuTime, now);
                return Math.Min(Math.Max(percent, 0), 100.0);
            }

            _lastCpuSample[pid] = (currentCpuTime, now);
            return 0;
        }
        catch
        {
            _lastCpuSample.Remove(pid);
            return 0;
        }
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS_EX2 counters, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX2
    {
        public uint cb;
        public uint PageFaultCount;
        public ulong PeakWorkingSetSize;
        public ulong WorkingSetSize;
        public ulong QuotaPeakPagedPoolUsage;
        public ulong QuotaPagedPoolUsage;
        public ulong QuotaPeakNonPagedPoolUsage;
        public ulong QuotaNonPagedPoolUsage;
        public ulong PagefileUsage;
        public ulong PeakPagefileUsage;
        public ulong PrivateUsage;
        public ulong PrivateWorkingSetSize;
        public ulong SharedWorkingSetSize;
    }

    private static long EstimateMemoryUsage(int pid, ModuleState state)
    {
        if (state != ModuleState.Running || pid <= 0) return 0;

        try
        {
            var process = Process.GetProcessById(pid);
            var counters = new PROCESS_MEMORY_COUNTERS_EX2 { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>() };
            return GetProcessMemoryInfo(process.Handle, out counters, counters.cb)
                ? (long)counters.PrivateWorkingSetSize
                : 0;
        }
        catch
        {
            return 0;
        }
    }

}
