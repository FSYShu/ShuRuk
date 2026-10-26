using System.Text.Json.Serialization;

namespace ShuRuk.Runtime.Abstractions.Models;

// Global registry data model / 全局注册表数据模型
public sealed class RegistryData
{
    public string SchemaVersion { get; set; } = "1.0";
    public string UpdatedAt { get; set; } = string.Empty;
    public RuntimeRecord Runtime { get; set; } = new();
    public HostRecord Host { get; set; } = new();
    public List<ModuleRecord> Modules { get; set; } = new();
}

public sealed class RuntimeRecord
{
    public string Version { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string SdkPackageVersion { get; set; } = string.Empty;
    public string InstalledAt { get; set; } = string.Empty;
    public List<string> CompatibleHostVersions { get; set; } = new();
    public string DistributionUri { get; set; } = string.Empty;

    // RefCount and InstalledApps are maintained in registry HKCU\Software\ShuRuk\Runtime
    // (see GlobalRegistry), NOT duplicated here to avoid data drift.
    // RefCount与InstalledApps由注册表HKCU\Software\ShuRuk\Runtime维护(见GlobalRegistry)，不在此重复以避免数据漂移。
}

public sealed class HostRecord
{
    public bool Installed { get; set; }
    public string Version { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string InstalledAt { get; set; } = string.Empty;
}

public sealed class ModuleRecord
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Kind { get; set; } = "module";
    public string Source { get; set; } = "external";
    public string InstallPath { get; set; } = string.Empty;
    public string Entry { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string MinRuntimeVersion { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public string Signature { get; set; } = string.Empty;
    public string InstalledAt { get; set; } = string.Empty;
    public HeartbeatRecord Heartbeat { get; set; } = new();
}

public sealed class HeartbeatRecord
{
    public string Endpoint { get; set; } = string.Empty;
    public string Status { get; set; } = "stopped";
    public int Pid { get; set; }
    public string LastSeen { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }
}