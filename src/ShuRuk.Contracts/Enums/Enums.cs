namespace ShuRuk.Contracts.Enums;

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
    IcoFile
}

public enum SnapshotType
{
    Full,
    Incremental
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