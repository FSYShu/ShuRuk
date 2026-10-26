using Microsoft.UI.Xaml;

namespace ShuRuk.Runtime.Abstractions.Interfaces;

// Theme service interface for decoupling UI from App / 主题服务接口，解耦UI与App层
public interface IThemeService
{
    string CurrentTheme { get; }
    List<(string Code, string DisplayName)> AvailableThemes { get; }
    event EventHandler<string>? ThemeChanged;

    Task SetThemeAsync(string theme);
    ElementTheme ToElementTheme();
}