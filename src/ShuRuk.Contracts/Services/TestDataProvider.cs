using ShuRuk.Contracts.Enums;

namespace ShuRuk.Contracts.Services;

public static class TestDataProvider
{
    // Memory range for test data / 测试数据内存范围
    private const int MinMemoryMB = 8;
    private const int MaxMemoryMB = 384;
    private const long BytesPerMB = 1024 * 1024;

    // Max CPU usage percent for test data / 测试数据最大CPU百分比
    private const double MaxCpuPercent = 8.0;
    private const double MinCpuPercent = 0.1;

    private static readonly (string Name, ModuleState State, int Pid)[] TestModules =
    [
        ("VideoProcessor", ModuleState.Running, 1024),
        ("AudioMixer", ModuleState.Running, 2048),
        ("ScreenRecorder", ModuleState.Running, 3072),
        ("FileConverter", ModuleState.Installed, 0),
        ("NetworkMonitor", ModuleState.Running, 4096),
        ("DataAnalyzer", ModuleState.Paused, 0),
        ("ClipboardManager", ModuleState.Running, 5120),
        ("WindowArranger", ModuleState.Running, 6144),
        ("ColorPicker", ModuleState.Paused, 0),
        ("TextExtractor", ModuleState.Running, 7168),
        ("ShortcutLauncher", ModuleState.Loaded, 0),
        ("SystemCleaner", ModuleState.Running, 8192)
    ];

    private static readonly Random _random = new();

    public static List<ModuleResourceUsage> GenerateSnapshot()
    {
        var snapshot = new List<ModuleResourceUsage>();

        foreach (var (name, state, pid) in TestModules)
        {
            var cpu = state == ModuleState.Running
                ? Math.Round(_random.NextDouble() * MaxCpuPercent + MinCpuPercent, 2)
                : 0;
            var memory = state == ModuleState.Running
                ? _random.Next(MinMemoryMB, MaxMemoryMB) * BytesPerMB
                : 0;

            snapshot.Add(new ModuleResourceUsage
            {
                ModuleName = name,
                Pid = pid,
                State = state,
                CpuUsagePercent = cpu,
                MemoryUsageBytes = memory,
                Timestamp = DateTime.UtcNow
            });
        }

        return snapshot;
    }
}