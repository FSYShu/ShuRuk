namespace ShuRuk.App.Helpers;

/// <summary>
/// Centralized application constants / 集中的应用常量定义。
/// Eliminates magic numbers and strings scattered across the codebase
/// 消除散布于代码各处的魔法数字与魔法字符串。
/// </summary>
internal static class AppConstants
{
    // Application identity / 应用标识
    public const string AppName = "ShuRuk";
    public const string AppUserModelId = "ShuRuk";
    public const string AppUserModelIdPackaged = "ShuRuk_e6tahm9t1221g!App";
    public const string AppDataFolderName = "ShuRuk";
    public const string CrashLogFileName = "crash.log";
    public const string DefaultAppVersion = "0.0.0";

    // Window sizing / 窗口尺寸
    public const int DefaultWindowWidth = 1200;
    public const int DefaultWindowHeight = 800;
    public const int MinWindowWidth = 800;
    public const int MinWindowHeight = 600;
    public const double DpiBase = 96.0;

    // Title bar / 标题栏
    public const int TitleBarButtonSize = 40;
    public const int LayoutRetryLimit = 5;

    // DWM dark mode attribute ids / DWM 暗色模式属性 ID
    public const int DwmwaUseImmersiveDarkMode20 = 20;
    public const int DwmwaUseImmersiveDarkMode19 = 19;

    // Win32 message constants / Win32 消息常量
    public const uint WmGetMinMaxInfo = 0x0024;

    // COM PropertyStore keys / COM 属性存储键
    public const string AppUserModelFmtid = "9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3";
    public const int AppUserModelIdPid = 5;
    public const int AppUserModelIconPid = 12;

    // MSIX packaging / MSIX 打包
    public const int AppmodelErrorNoPackage = unchecked((int)0x80073D54);

    // Crash logging / 崩溃日志
    public const int InnerExceptionDepthLimit = 5;

    // Status monitor / 状态监控
    public const int StatusMonitorIntervalSeconds = 5;
    public const int StatusHistoryRetentionCount = 60;

    // Module discovery / 模块发现
    public const string GitHubSearchApiUrl =
        "https://api.github.com/search/repositories?q=shuruk-module+in:topics&per_page=100";
    public const string GitHubApiBase = "https://api.github.com/repos/";
    public const string DefaultModuleVersion = "1.0.0";
    public const string DiscoveryCacheFileName = "discovery_cache.json";
    public const string ManifestFileName = "manifest.json";

    // Search debounce / 搜索防抖
    public const int SearchDebounceMs = 300;

    // Animation durations / 动画持续时间
    public const int RestartInfoBarShowMs = 250;
    public const int RestartInfoBarHideMs = 200;

    // Byte size formatting / 字节单位格式化
    public const int ByteUnitBase = 1024;

    // Navigation tags / 导航标签
    public static class NavTags
    {
        public const string Home = "home";
        public const string Modules = "modules";
        public const string Changelog = "changelog";
        public const string Monitor = "monitor";
        public const string Settings = "settings";
    }

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
        public const string ZhCn = "zh-cn";
        public const string EnUs = "en-us";
        public const string ZhPrefix = "zh";
    }

    // Configuration keys / 配置键
    public static class ConfigKeys
    {
        public const string AppTheme = "AppTheme";
        public const string AppThemeModule = "ShuRuk.UI";
        public const string AppLanguage = "app.language";
    }
}