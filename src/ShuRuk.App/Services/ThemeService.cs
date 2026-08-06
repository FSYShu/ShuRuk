using Microsoft.UI.Xaml;
using ShuRuk.App.Helpers;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.App.Services;

public class ThemeService
{
    private const string ConfigKey = AppConstants.ConfigKeys.AppTheme;
    private const string ConfigModule = AppConstants.ConfigKeys.AppThemeModule;
    private readonly IConfigurationService _configService;
    private string _currentTheme = AppConstants.Themes.System;

    public string CurrentTheme => _currentTheme;

    public event EventHandler<string>? ThemeChanged;

    public readonly List<(string Code, string DisplayName)> AvailableThemes =
    [
        (AppConstants.Themes.System, "Follow system"),
        (AppConstants.Themes.Light, "Light"),
        (AppConstants.Themes.Dark, "Dark")
    ];

    public ThemeService(IConfigurationService configService)
    {
        _configService = configService;
    }

    public async Task InitializeAsync()
    {
        var saved = await _configService.GetValueAsync<string>(ConfigKey, ConfigModule);
        if (!string.IsNullOrEmpty(saved) && AvailableThemes.Any(t => t.Code == saved))
        {
            _currentTheme = saved;
        }
    }

    public async Task SetThemeAsync(string theme)
    {
        if (_currentTheme == theme) return;
        _currentTheme = theme;
        await _configService.SetValueAsync(ConfigKey, theme, ConfigModule);
        ThemeChanged?.Invoke(this, theme);
    }

    public ElementTheme ToElementTheme()
    {
        return _currentTheme switch
        {
            AppConstants.Themes.Light => ElementTheme.Light,
            AppConstants.Themes.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}