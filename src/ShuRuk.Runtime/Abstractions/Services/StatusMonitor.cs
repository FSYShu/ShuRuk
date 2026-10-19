using System.Diagnostics;
using System.Runtime.InteropServices;

using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Abstractions.Models;

namespace ShuRuk.Runtime.Abstractions.Services;

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
                ProcessTestModeSnapshot();
                return;
            }

            await ProcessRealModeSnapshotAsync();
        }
        catch (Exception)
        {
        }
    }

    private void ProcessTestModeSnapshot()
    {
        // Fill data flag: full scrollable list or empty state / 填充标志: 足量可滚动列表或空状态
        var testSnapshot = _testModeService?.IsFillDataEnabled == true
            ? TestDataProvider.GenerateFullSnapshot()
            : [];
        lock (_lock)
        {
            foreach (var entry in testSnapshot)
            {
                AddToHistory(entry);
            }
        }
        ResourceUsageUpdated?.Invoke(this, testSnapshot);
    }

    private async Task ProcessRealModeSnapshotAsync()
    {
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
        var hostUsage = CreateUsageEntry(HostProcessName, hostPid, ModuleState.Running);

        lock (_lock)
        {
            AddToHistory(hostUsage);
        }
        snapshot.Add(hostUsage);

        foreach (var info in moduleInfos)
        {
            var moduleState = _moduleManager.GetModuleStateAsync(info.Manifest.Name).GetAwaiter().GetResult();
            var usage = CreateUsageEntry(info.Manifest.Name, info.Pid, moduleState);

            lock (_lock)
            {
                AddToHistory(usage);
            }
            snapshot.Add(usage);
        }

        ResourceUsageUpdated?.Invoke(this, snapshot);
    }

    // Add entry to history and trim to retention count / 添加条目到历史并按保留数截断
    private void AddToHistory(ModuleResourceUsage entry)
    {
        if (!_history.ContainsKey(entry.ModuleName))
            _history[entry.ModuleName] = new List<ModuleResourceUsage>();

        _history[entry.ModuleName].Add(entry);

        if (_history[entry.ModuleName].Count > _historyRetentionCount)
            _history[entry.ModuleName].RemoveRange(0, _history[entry.ModuleName].Count - _historyRetentionCount);
    }

    private ModuleResourceUsage CreateUsageEntry(string moduleName, int pid, ModuleState state)
    {
        return new ModuleResourceUsage
        {
            ModuleName = moduleName,
            Pid = pid,
            State = state,
            CpuUsagePercent = EstimateCpuUsage(pid, state),
            MemoryUsageBytes = EstimateMemoryUsage(pid, state)
        };
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
