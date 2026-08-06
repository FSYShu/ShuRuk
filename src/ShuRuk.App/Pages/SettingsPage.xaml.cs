using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.App.Services;
using ShuRuk.Contracts.Interfaces;


namespace ShuRuk.App.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly LanguageService _languageService;
    private readonly ThemeService _themeService;
    private readonly ITestModeService _testModeService;
    private bool _initializing = true;

    public string LanguageSectionHeader => LocalizationHelper.GetString("String_SettingsSectionLanguage");
    public string LanguageHeader => LocalizationHelper.GetString("String_SettingsLanguageHeader");
    public string LanguageDescription => LocalizationHelper.GetString("String_SettingsLanguageDescription");
    public string ThemeHeader => LocalizationHelper.GetString("String_SettingsThemeHeader");
    public string ThemeDescription => LocalizationHelper.GetString("String_SettingsThemeDescription");
    public string TestModeHeader => LocalizationHelper.GetString("String_SettingsTestModeHeader");
    public string TestModeDescription => LocalizationHelper.GetString("String_SettingsTestModeDescription");
    public string RestartButtonText => LocalizationHelper.GetString("SettingsRestartButton.Content");
    public string AboutSectionHeader => LocalizationHelper.GetString("String_SettingsSectionAbout");
    public string AppNameHeader => AppContextAccessor.Current.AppTitle;
    public string AppSubtitle => AppContextAccessor.Current.AppSubtitle;
    public string AppVersionDescription => LocalizationHelper.GetString("String_SettingsAppVersionFormat")
        .Replace("{0}", AppContextAccessor.Current.AppVersion);

    public SettingsPage()
    {
        InitializeComponent();
        _languageService = AppContextAccessor.Current.Services.GetRequiredService<LanguageService>();
        _themeService = AppContextAccessor.Current.Services.GetRequiredService<ThemeService>();
        _testModeService = App.Services.GetRequiredService<ITestModeService>();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _initializing = true;

        // Language / 语言
        PopulateLanguageComboBox(_languageService.CurrentLanguage);

        // Theme / 主题
        ThemeComboBox.Items.Clear();
        int selectedThemeIndex = 0;
        for (int i = 0; i < _themeService.AvailableThemes.Count; i++)
        {
            var theme = _themeService.AvailableThemes[i];
            var displayName = theme.Code switch
            {
                AppConstants.Themes.System => LocalizationHelper.GetString("String_ThemeFollowSystem"),
                AppConstants.Themes.Light => LocalizationHelper.GetString("String_ThemeLight"),
                AppConstants.Themes.Dark => LocalizationHelper.GetString("String_ThemeDark"),
                _ => theme.DisplayName
            };
            ThemeComboBox.Items.Add(displayName);
            if (theme.Code == _themeService.CurrentTheme)
                selectedThemeIndex = i;
        }
        ThemeComboBox.SelectedIndex = selectedThemeIndex;

        // TestMode / 测试模式
        TestModeToggle.IsOn = _testModeService.IsEnabled;

        _initializing = false;

        if (_languageService.IsRestartPending)
        {
            ShowRestartInfoBar();
        }
    }

    // Shared language combo box population / 共享的语言组合框填充逻辑
    private void PopulateLanguageComboBox(string selectedCode)
    {
        LanguageComboBox.Items.Clear();
        int selectedIdx = 0;
        for (int i = 0; i < _languageService.AvailableLanguages.Count; i++)
        {
            var lang = _languageService.AvailableLanguages[i];
            var displayName = lang.Code == AppConstants.Languages.System
                ? LocalizationHelper.GetString("String_LangFollowSystem")
                : lang.DisplayName;
            LanguageComboBox.Items.Add(displayName);
            if (lang.Code == selectedCode)
                selectedIdx = i;
        }
        LanguageComboBox.SelectedIndex = selectedIdx;
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;

        try
        {
            var index = LanguageComboBox.SelectedIndex;
            if (index < 0 || index >= _languageService.AvailableLanguages.Count) return;

            var code = _languageService.AvailableLanguages[index].Code;
            var newEffective = _languageService.ResolveEffectiveLanguage(code);
            var currentEffective = _languageService.ResolveEffectiveLanguage(_languageService.ActiveLanguage);

            if (newEffective == currentEffective)
            {
                await _languageService.SetLanguageAsync(code);
                HideRestartInfoBar();
                RefreshLanguageComboBox(code);
                return;
            }

            await _languageService.SetLanguageForRestartAsync(code);
            ShowRestartInfoBar();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LanguageComboBox_SelectionChanged: {ex}");
        }
    }

    private void RefreshLanguageComboBox(string selectedCode)
    {
        _initializing = true;
        PopulateLanguageComboBox(selectedCode);
        _initializing = false;
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;

        try
        {
            var index = ThemeComboBox.SelectedIndex;
            if (index < 0 || index >= _themeService.AvailableThemes.Count) return;

            var code = _themeService.AvailableThemes[index].Code;
            await _themeService.SetThemeAsync(code);
            AppContextAccessor.Current.ApplyTheme(_themeService.CurrentTheme);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ThemeComboBox_SelectionChanged: {ex}");
        }
    }

    private async void TestModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        await _testModeService.SetEnabledAsync(TestModeToggle.IsOn);
    }

    private void ShowRestartInfoBar()
    {
        RestartInfoBar.Title = LocalizationHelper.GetString("SettingsLanguageRestartTitle.Text");
        RestartInfoBar.Message = LocalizationHelper.GetString("SettingsLanguageRestartHint.Text");
        RestartInfoBar.Visibility = Visibility.Visible;
        RestartInfoBar.Opacity = 0;
        RestartInfoBar.IsOpen = true;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(AppConstants.RestartInfoBarShowMs),
            EasingFunction = new QuadraticEase()
        };
        Storyboard.SetTarget(anim, RestartInfoBar);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void HideRestartInfoBar()
    {
        var anim = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(AppConstants.RestartInfoBarHideMs),
            EasingFunction = new QuadraticEase()
        };
        Storyboard.SetTarget(anim, RestartInfoBar);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (s, e) =>
        {
            RestartInfoBar.IsOpen = false;
            RestartInfoBar.Visibility = Visibility.Collapsed;
        };
        sb.Begin();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        try
        {
            if (_languageService.IsRestartPending)
            {
                await _languageService.RevertPendingLanguageChangeAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnNavigatedFrom: {ex}");
        }

        Unloaded += SettingsPage_Unloaded;
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= SettingsPage_Unloaded;
        HideRestartInfoBar();
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        AppContextAccessor.Current.Restart();
    }
}
