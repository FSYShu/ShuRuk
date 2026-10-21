using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.Runtime.Abstractions;

/// <summary>
/// Static service locator for Runtime to access App-injected providers.
/// / Runtime访问App注入提供者的静态服务定位器。
/// </summary>
public static class RuntimeServices
{
    public static IChangelogDataProvider? ChangelogDataProvider { get; set; }
    public static ILanguageService? LanguageService { get; set; }
    public static IThemeService? ThemeService { get; set; }
    public static ITestModeService? TestModeService { get; set; }
    public static IAppContext? AppContext { get; set; }
}