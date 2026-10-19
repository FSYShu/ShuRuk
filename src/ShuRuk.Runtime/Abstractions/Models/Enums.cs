using System.Text.Json.Serialization;

namespace ShuRuk.Runtime.Abstractions.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ModuleSource>))]
public enum ModuleSource
{
    BuiltIn,
    External
}

public enum ModuleState
{
    Discovered,
    Validated,
    Installed,
    Loaded,
    Running,
    Paused,
    Crashed,
    LoadFailed,
    Unloaded,
    Uninstalled
}

public enum IconSourceType
{
    BuiltIn,
    CustomImage,
    IcoFile,
    PEIcon
}

public enum SnapshotType
{
    Full,
    Incremental
}

[JsonConverter(typeof(JsonStringEnumConverter<ModuleKind>))]
public enum ModuleKind
{
    Module,
    AtomicService
}

public enum ChangeType
{
    ContentChange,
    Rename,
    Move,
    Copy,
    Manual
}

public enum GestureType
{
    Mouse,
    Touchpad
}

public enum SplitLayoutType
{
    HalfHalf,
    FullHalf,
    ThirdEqual,
    LargeTriple,
    PinStyle
}

public enum DownloadStatus
{
    Pending,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}