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
// Sub-module page type: function page or settings-only page / 子模块页面类型：功能页或仅介绍设置页
public enum SubModulePageType
{
    // Full function page with buttons / 全量功能页(含按钮跳转设置)
    FunctionPage,
    // Settings & about page only / 仅介绍设置页
    SettingsOnlyPage
}