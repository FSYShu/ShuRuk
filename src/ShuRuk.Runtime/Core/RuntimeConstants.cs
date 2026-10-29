namespace ShuRuk.Runtime.Core;

/// <summary>
/// Constants shared between Runtime and App / Runtime与App共享的常量。
/// </summary>
public static class RuntimeConstants
{
    // Shared paths and limits / 共享路径与限制
    public const string AppDataFolderName = "ShuRuk";
    public const string CrashLogFileName = "crash.log";
    public const int InnerExceptionDepthLimit = 5;

    // Animation durations / 动画持续时间
    public const int RestartInfoBarShowMs = 250;
    public const int RestartInfoBarHideMs = 200;

    // Theme codes / 主题代码
    public static class Themes
    {
        public const string System = "system";
        public const string Light = "light";
        public const string Dark = "dark";
    }

    // Language codes / 语言代码
    public static class Languages
    {
        public const string System = "system";
    }
}