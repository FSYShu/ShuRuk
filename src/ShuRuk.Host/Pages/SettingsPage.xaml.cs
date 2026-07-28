using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.Host.Services;
using System.Reflection;


namespace ShuRuk.Host.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly LanguageService _languageService;
    private readonly ThemeService _themeService;
    private bool _initializing = true;

    public string LanguageSectionHeader => LocalizationHelper.GetString("String_SettingsSectionLanguage");
    public string LanguageHeader => LocalizationHelper.GetString("String_SettingsLanguageHeader");
    public string LanguageDescription => LocalizationHelper.GetString("String_SettingsLanguageDescription");
    public string ThemeHeader => LocalizationHelper.GetString("String_SettingsThemeHeader");
    public string ThemeDescription => LocalizationHelper.GetString("String_SettingsThemeDescription");
    public string RestartButtonText => LocalizationHelper.GetString("SettingsRestartButton.Content");
    public string AboutSectionHeader => LocalizationHelper.GetString("String_SettingsSectionAbout");
    public string AppNameHeader => ((MainWindow)App.MainWindow).AppTitle;
    public string AppSubtitle => ((MainWindow)App.MainWindow).AppSubtitleText;
    public string AppVersionDescription => LocalizationHelper.GetString("String_SettingsAppVersionFormat")
        .Replace("{0}", GetAppVersion());

    public SettingsPage()
    {
        InitializeComponent();
        _languageService = App.Services.GetRequiredService<LanguageService>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        Loaded += OnPageLoaded;
    }

    private static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is not null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "1.0.0";
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _initializing = true;

        // Language / 语言
        LanguageComboBox.Items.Clear();
        int selectedLangIndex = 0;
        for (int i = 0; i < _languageService.AvailableLanguages.Count; i++)
        {
            var lang = _languageService.AvailableLanguages[i];
            var displayName = lang.Code == "system"
                ? LocalizationHelper.GetString("String_LangFollowSystem")
                : lang.DisplayName;
            LanguageComboBox.Items.Add(displayName);
            if (lang.Code == _languageService.CurrentLanguage)
                selectedLangIndex = i;
        }
        LanguageComboBox.SelectedIndex = selectedLangIndex;

        // Theme / 主题
        ThemeComboBox.Items.Clear();
        int selectedThemeIndex = 0;
        for (int i = 0; i < _themeService.AvailableThemes.Count; i++)
        {
            var theme = _themeService.AvailableThemes[i];
            var displayName = theme.Code switch
            {
                "system" => LocalizationHelper.GetString("String_ThemeFollowSystem"),
                "light" => LocalizationHelper.GetString("String_ThemeLight"),
                "dark" => LocalizationHelper.GetString("String_ThemeDark"),
                _ => theme.DisplayName
            };
            ThemeComboBox.Items.Add(displayName);
            if (theme.Code == _themeService.CurrentTheme)
                selectedThemeIndex = i;
        }
        ThemeComboBox.SelectedIndex = selectedThemeIndex;

        _initializing = false;

        if (_languageService.IsRestartPending)
        {
            ShowRestartInfoBar();
        }
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;

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

    private void RefreshLanguageComboBox(string selectedCode)
    {
        _initializing = true;
        LanguageComboBox.Items.Clear();
        int selectedIdx = 0;
        for (int i = 0; i < _languageService.AvailableLanguages.Count; i++)
        {
            var lang = _languageService.AvailableLanguages[i];
            var displayName = lang.Code == "system"
                ? LocalizationHelper.GetString("String_LangFollowSystem")
                : lang.DisplayName;
            LanguageComboBox.Items.Add(displayName);
            if (lang.Code == selectedCode)
                selectedIdx = i;
        }
        LanguageComboBox.SelectedIndex = selectedIdx;
        _initializing = false;
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;

        var index = ThemeComboBox.SelectedIndex;
        if (index < 0 || index >= _themeService.AvailableThemes.Count) return;

        var code = _themeService.AvailableThemes[index].Code;
        await _themeService.SetThemeAsync(code);
        ((MainWindow)App.MainWindow).ApplyTheme(_themeService.ToElementTheme());
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
            Duration = TimeSpan.FromMilliseconds(250),
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
            Duration = TimeSpan.FromMilliseconds(200),
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

        if (_languageService.IsRestartPending)
        {
            await _languageService.RevertPendingLanguageChangeAsync();
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
        App.MainWindow.Close();

        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)!
            });
        }

        Environment.Exit(0);
    }
}
