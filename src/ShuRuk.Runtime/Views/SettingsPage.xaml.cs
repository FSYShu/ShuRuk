using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.Runtime.Abstractions;
using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Helpers;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ShuRuk.Runtime.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ILanguageService _languageService;
    private readonly IThemeService _themeService;
    private readonly ITestModeService _testModeService;
    private readonly IAppContext _appContext;
    private bool _initializing = true;

    public string PageTitleText => RuntimeLocalizationHelper.GetString("String_SettingsPageTitle");
    public string LanguageSectionHeader => RuntimeLocalizationHelper.GetString("String_SettingsSectionLanguage");
    public string LanguageHeader => RuntimeLocalizationHelper.GetString("String_SettingsLanguageHeader");
    public string LanguageDescription => RuntimeLocalizationHelper.GetString("String_SettingsLanguageDescription");
    public string ThemeHeader => RuntimeLocalizationHelper.GetString("String_SettingsThemeHeader");
    public string ThemeDescription => RuntimeLocalizationHelper.GetString("String_SettingsThemeDescription");
    public string TestModeHeader => RuntimeLocalizationHelper.GetString("String_SettingsTestModeHeader");
    public string TestModeDescription => RuntimeLocalizationHelper.GetString("String_SettingsTestModeDescription");
    public string TestModeFillHeader => RuntimeLocalizationHelper.GetString("String_SettingsTestModeFillHeader");
    public string TestModeFillDescription => RuntimeLocalizationHelper.GetString("String_SettingsTestModeFillDescription");
    public string TestModeFillEmpty => RuntimeLocalizationHelper.GetString("String_SettingsTestModeFillEmpty");
    public string TestModeFillFull => RuntimeLocalizationHelper.GetString("String_SettingsTestModeFillFull");
    public string ChangelogRepoHeader => RuntimeLocalizationHelper.GetString("String_SettingsChangelogRepoHeader");
    public string ChangelogRepoDescription => RuntimeLocalizationHelper.GetString("String_SettingsChangelogRepoDescription");
    public string ChangelogSectionHeader => RuntimeLocalizationHelper.GetString("String_SettingsChangelogSectionHeader");
    public string ChangelogSectionDescription => RuntimeLocalizationHelper.GetString("String_SettingsChangelogSectionDescription");

    public string RestartButtonText => RuntimeLocalizationHelper.GetString("SettingsRestartButton.Content");
    public string AboutSectionHeader => RuntimeLocalizationHelper.GetString("String_SettingsSectionAbout");
    public string AppNameHeader => _appContext.AppTitle;
    public string AppSubtitle => _appContext.AppSubtitle;
    public string AppVersionDescription => RuntimeLocalizationHelper.GetString("String_SettingsAppVersionFormat")
        .Replace("{0}", _appContext.AppVersion);
    public string SdkVersionDescription => RuntimeLocalizationHelper.GetString("String_SettingsSdkVersionFormat")
        .Replace("{0}", _sdkVersion ?? "unknown");
    public string SdkVersionHeader => RuntimeLocalizationHelper.GetString("String_SettingsSdkVersionHeader");
    public string ToolkitVersionDescription => RuntimeLocalizationHelper.GetString("String_SettingsToolkitVersionFormat")
        .Replace("{0}", _toolkitVersion ?? "unknown");
    public string ToolkitVersionHeader => RuntimeLocalizationHelper.GetString("String_SettingsToolkitVersionHeader");

    // Read NuGet package versions from .deps.json at runtime / 运行时从 deps.json 读取 NuGet 包版本
    private static readonly string? _sdkVersion = TryGetPackageVersion("Microsoft.WindowsAppSDK");
    private static readonly string? _toolkitVersion = TryGetPackageVersion("CommunityToolkit.WinUI.Controls.SettingsControls");

    private static string? TryGetPackageVersion(string packageName)
    {
        try
        {
            var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "ShuRuk";
            var depsPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.deps.json");
            if (!File.Exists(depsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(depsPath));
            var libraries = doc.RootElement.GetProperty("libraries");
            foreach (var lib in libraries.EnumerateObject())
            {
                var slashIndex = lib.Name.IndexOf('/');
                if (slashIndex > 0 && lib.Name.AsSpan(0, slashIndex).SequenceEqual(packageName))
                    return lib.Name[(slashIndex + 1)..];
            }
        }
        catch { }
        return null;
    }

    public SettingsPage()
    {
        InitializeComponent();
        _languageService = RuntimeServices.LanguageService!;
        _themeService = RuntimeServices.ThemeService!;
        _testModeService = RuntimeServices.TestModeService!;
        _appContext = RuntimeServices.AppContext!;
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
                RuntimeConstants.Themes.System => RuntimeLocalizationHelper.GetString("String_ThemeFollowSystem"),
                RuntimeConstants.Themes.Light => RuntimeLocalizationHelper.GetString("String_ThemeLight"),
                RuntimeConstants.Themes.Dark => RuntimeLocalizationHelper.GetString("String_ThemeDark"),
                _ => theme.DisplayName
            };
            ThemeComboBox.Items.Add(displayName);
            if (theme.Code == _themeService.CurrentTheme)
                selectedThemeIndex = i;
        }
        ThemeComboBox.SelectedIndex = selectedThemeIndex;

        // TestMode / 测试模式
        TestModeToggle.IsOn = _testModeService.IsEnabled;
        TestModeFillCombo.SelectedIndex = _testModeService.IsFillDataEnabled ? 1 : 0;
        ChangelogRepoTextBox.Text = _testModeService.ChangelogRepoUrl;
        ChangelogSectionTextBox.Text = _testModeService.ChangelogSectionFilter;

        UpdateFillCardVisibility();

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
            var displayName = lang.Code == RuntimeConstants.Languages.System
                ? RuntimeLocalizationHelper.GetString("String_LangFollowSystem")
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
            _appContext.ApplyTheme(_themeService.CurrentTheme);
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
        UpdateFillCardVisibility();
    }

    private async void TestModeFillCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        // Index 1 = fill data, 0 = empty state / 索引1=填充数据,0=空状态
        await _testModeService.SetFillDataEnabledAsync(TestModeFillCombo.SelectedIndex == 1);
    }

    private async void ChangelogRepoTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        await _testModeService.SetChangelogRepoUrlAsync(ChangelogRepoTextBox.Text);
    }

    private async void ChangelogSectionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        await _testModeService.SetChangelogSectionFilterAsync(ChangelogSectionTextBox.Text);
    }


    // Expand/collapse test mode expander based on toggle state / 根据开关状态展开/折叠测试模式
    private void UpdateFillCardVisibility()
    {
        TestModeExpander.IsExpanded = TestModeToggle.IsOn;
    }



    private void ShowRestartInfoBar()
    {
        RestartInfoBar.Title = RuntimeLocalizationHelper.GetString("SettingsLanguageRestartTitle.Text");
        RestartInfoBar.Message = RuntimeLocalizationHelper.GetString("SettingsLanguageRestartHint.Text");
        RestartInfoBar.Visibility = Visibility.Visible;
        RestartInfoBar.Opacity = 0;
        RestartInfoBar.IsOpen = true;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(RuntimeConstants.RestartInfoBarShowMs),
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
            Duration = TimeSpan.FromMilliseconds(RuntimeConstants.RestartInfoBarHideMs),
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
        _appContext.Restart();
    }
}