using Microsoft.UI.Xaml;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.Host.Services;

public class ThemeService
{
    private const string ConfigKey = "AppTheme";
    private const string ConfigModule = "ShuRuk.Host";
    private readonly IConfigurationService _configService;
    private string _currentTheme = "system";

    public string CurrentTheme => _currentTheme;

    public event EventHandler<string>? ThemeChanged;

    public readonly List<(string Code, string DisplayName)> AvailableThemes =
    [
        ("system", "Follow system"),
        ("light", "Light"),
        ("dark", "Dark")
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
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}