using ShuRuk.Runtime.Abstractions.Models;

namespace ShuRuk.Runtime.Abstractions.Services;

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
        ("测试进程 1", ModuleState.Running, 1024),
        ("测试进程 2", ModuleState.Running, 2048),
        ("测试进程 3", ModuleState.Running, 3072),
        ("测试进程 4", ModuleState.Installed, 0),
        ("测试进程 5", ModuleState.Running, 4096),
        ("测试进程 6", ModuleState.Paused, 0),
        ("测试进程 7", ModuleState.Running, 5120),
        ("测试进程 8", ModuleState.Running, 6144),
        ("测试进程 9", ModuleState.Paused, 0),
        ("测试进程 10", ModuleState.Running, 7168),
        ("测试进程 11", ModuleState.Loaded, 0),
        ("测试进程 12", ModuleState.Running, 8192)
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

    // Get test module info for module page / 获取测试模块信息用于模块页
    public static List<TestModuleInfo> GetTestModules()
    {
        return
        [
            new TestModuleInfo("TestModule1", "测试模块 1", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Running),
            new TestModuleInfo("TestModule2", "测试模块 2", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Running),
            new TestModuleInfo("TestModule3", "测试模块 3", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.External, ModuleState.Running),
            new TestModuleInfo("TestModule4", "测试模块 4", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.External, ModuleState.Installed),
            new TestModuleInfo("TestModule5", "测试模块 5", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Running),
            new TestModuleInfo("TestModule6", "测试模块 6", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.External, ModuleState.Paused),
            new TestModuleInfo("TestModule7", "测试模块 7", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Running),
            new TestModuleInfo("TestModule8", "测试模块 8", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Running),
            new TestModuleInfo("TestModule9", "测试模块 9", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.External, ModuleState.Paused),
            new TestModuleInfo("TestModule10", "测试模块 10", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.External, ModuleState.Running),
            new TestModuleInfo("TestModule11", "测试模块 11", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Loaded),
            new TestModuleInfo("TestModule12", "测试模块 12", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Running),
            new TestModuleInfo("TestModule13", "测试模块 13", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.External, ModuleState.Installed),
            new TestModuleInfo("TestModule14", "测试模块 14", "测试用模拟模块，非实际存在", "1.0.0", ModuleSource.BuiltIn, ModuleState.Loaded)
        ];
    }

    // Count for scrollable test data / 可滚动测试数据条数
    private const int FullDataCount = 50;

    // Generate full snapshot for scrollable monitor / 生成足量快照用于可滚动监控页
    public static List<ModuleResourceUsage> GenerateFullSnapshot()
    {
        var snapshot = new List<ModuleResourceUsage>();
        var states = new[] { ModuleState.Running, ModuleState.Installed, ModuleState.Paused, ModuleState.Loaded };

        for (int i = 1; i <= FullDataCount; i++)
        {
            var state = states[i % states.Length];
            var cpu = state == ModuleState.Running
                ? Math.Round(_random.NextDouble() * MaxCpuPercent + MinCpuPercent, 2)
                : 0;
            var memory = state == ModuleState.Running
                ? _random.Next(MinMemoryMB, MaxMemoryMB) * BytesPerMB
                : 0;

            snapshot.Add(new ModuleResourceUsage
            {
                ModuleName = $"测试进程 {i}",
                Pid = state == ModuleState.Running ? 1024 + i * 10 : 0,
                State = state,
                CpuUsagePercent = cpu,
                MemoryUsageBytes = memory,
                Timestamp = DateTime.UtcNow
            });
        }

        return snapshot;
    }

    // Generate full test modules for scrollable module page / 生成足量测试模块用于可滚动模块页
    public static List<TestModuleInfo> GetFullTestModules()
    {
        var list = new List<TestModuleInfo>();
        var sources = new[] { ModuleSource.BuiltIn, ModuleSource.External };
        var states = new[] { ModuleState.Running, ModuleState.Installed, ModuleState.Paused, ModuleState.Loaded };

        for (int i = 1; i <= FullDataCount; i++)
        {
            list.Add(new TestModuleInfo(
                $"TestModule{i}",
                $"测试模块 {i}",
                "测试用模拟模块，非实际存在",
                "1.0.0",
                sources[i % sources.Length],
                states[i % states.Length]));
        }

        return list;
    }

    // Generate full discover entries for scrollable discover page / 生成足量发现条目用于可滚动发现页
    public static List<ModuleDiscoveryEntry> GetFullDiscoverEntries()
    {
        var list = new List<ModuleDiscoveryEntry>();
        var sources = new[] { ModuleSource.BuiltIn, ModuleSource.External };

        for (int i = 1; i <= FullDataCount; i++)
        {
            list.Add(new ModuleDiscoveryEntry
            {
                Name = $"TestDiscover{i}",
                DisplayName = $"测试发现模块 {i}",
                Description = "测试用模拟模块，非实际存在",
                Version = "1.0.0",
                Repository = $"test-repo-{i}",
                Source = sources[i % sources.Length],
                Category = "Test",
                Downloads = i * 100,
                Rating = Math.Round(3.5 + (i % 5) * 0.3, 1),
                UpdatedAt = DateTime.UtcNow.AddDays(-i),
                Installed = false
            });
        }

        return list;
    }
}

// Test module info for module page display / 测试模块信息用于模块页展示
public record TestModuleInfo(string Name, string DisplayName, string Description, string Version, ModuleSource Source, ModuleState State);