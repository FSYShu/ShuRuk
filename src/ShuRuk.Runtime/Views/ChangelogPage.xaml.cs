using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.Runtime.Core;
using ShuRuk.Runtime.Contracts;
using ShuRuk.Runtime.Controls;
using Windows.Foundation;
using ShuRuk.Runtime.Helpers;
using ShuRuk.Runtime.Models;
using ShuRuk.Runtime.Services.Changelog;
using ShuRuk.Runtime.Services.Markdown;
using ShuRuk.Runtime.ViewModels;
using System.Collections.ObjectModel;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // ViewModel for data state management / 数据状态管理ViewModel
    public ChangelogViewModel ViewModel { get; }

    // Data collection aliases (delegate to ViewModel) / 数据集合别名(委托到ViewModel)
    private List<ChangelogEntry> _allEntries => ViewModel.AllEntries;
    private List<ChangelogEntry> _sourceEntries => ViewModel.SourceEntries;
    private ObservableCollection<ChangelogEntry> _displayItems => ViewModel.DisplayItems;
    private string _filterTag { get => ViewModel.FilterTag; set => ViewModel.FilterTag = value; }
    private bool _showImages { get => ViewModel.ShowImages; set => ViewModel.ShowImages = value; }

    // Localized filter option text / 本地化过滤选项文本
    public string PageTitleText => LocalizationHelper.Runtime.GetString("RuntimeChangelogPageTitle");
    public string FilterAllText => LocalizationHelper.Runtime.GetString("RuntimeChangelogFilterAll");
    public string FilterExcludePrereleaseText =>
        LocalizationHelper.Runtime.GetString("RuntimeChangelogFilterExcludePrerelease");
    public string PrevVersionText => LocalizationHelper.Runtime.GetString("RuntimeChangelogPrevVersion");
    public string NextVersionText => LocalizationHelper.Runtime.GetString("RuntimeChangelogNextVersion");
    public string ShowImagesA11yName => LocalizationHelper.Runtime.GetString("RuntimeChangelogShowImages");
    public string RefreshA11yName => LocalizationHelper.Runtime.GetString("RuntimeChangelogRefresh");
    public string PrereleaseLabelText => LocalizationHelper.Runtime.GetString("RuntimeChangelogPrerelease");
    public string CollapseButtonText => LocalizationHelper.Runtime.GetString("RuntimeChangelogCollapse");
    public string CollapseTooltipText => LocalizationHelper.Runtime.GetString("RuntimeChangelogCollapseTooltip");

    public static readonly DependencyProperty EntriesProperty =
        DependencyProperty.Register(
            nameof(Entries), typeof(IEnumerable<ChangelogEntry>),
            typeof(ChangelogPage), new PropertyMetadata(null, OnEntriesChanged));

    public IEnumerable<ChangelogEntry> Entries
    {
        get => (IEnumerable<ChangelogEntry>)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    // Empty state title / 空状态标题
    public static readonly DependencyProperty EmptyTitleProperty =
        DependencyProperty.Register(
            nameof(EmptyTitle), typeof(string), typeof(ChangelogPage),
            new PropertyMetadata("No changelog"));

    public string EmptyTitle
    {
        get => (string)GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    // Empty state description / 空状态描述
    public static readonly DependencyProperty EmptyDescriptionProperty =
        DependencyProperty.Register(
            nameof(EmptyDescription), typeof(string), typeof(ChangelogPage),
            new PropertyMetadata("You're up to date."));

    public string EmptyDescription
    {
        get => (string)GetValue(EmptyDescriptionProperty);
        set => SetValue(EmptyDescriptionProperty, value);
    }

    // Loading state / 加载状态
    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(
            nameof(IsLoading), typeof(bool), typeof(ChangelogPage),
            new PropertyMetadata(false, OnIsLoadingChanged));

    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }

    // Loading text / 加载文字
    public static readonly DependencyProperty LoadingTextProperty =
        DependencyProperty.Register(
            nameof(LoadingText), typeof(string), typeof(ChangelogPage),
            new PropertyMetadata("", OnLoadingTextChanged));

    public string LoadingText
    {
        get => (string)GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }

    public ChangelogPage()
    {
        ViewModel = new ChangelogViewModel(DispatcherQueue);
        InitializeComponent();
        ChangelogRepeater.ItemsSource = ViewModel.DisplayItems;

        // Initial tooltip for image toggle / 图片切换初始提示
        ToolTipService.SetToolTip(ShowImagesToggle, LocalizationHelper.Runtime.GetString("RuntimeChangelogHideImages"));
        // NavigationView breaks theme propagation from root.RequestedTheme; set page theme directly
        // / NavigationView断链root.RequestedTheme传播,直接设置页面主题
        if (RuntimeServices.ThemeService is { } ts)
        {
            this.RequestedTheme = ts.ToElementTheme();
            ts.ThemeChanged += OnAppThemeChanged;
        }
        this.Unloaded += ChangelogPage_Unloaded;
        this.ActualThemeChanged += ChangelogPage_ActualThemeChanged;

        // Subscribe to ViewModel events / 订阅ViewModel事件
        ViewModel.DataChanged += OnViewModelDataChanged;
        ViewModel.ShowCacheInfoBarRequested += ShowCacheInfoBar;
    }

    // On ViewModel data change: clear card height cache and update UI
    // / ViewModel数据变化时:清卡片高度缓存并更新UI
    private void OnViewModelDataChanged()
    {
        _cardHeightCache.Clear();
        ClearStickyOverlays();  // Clear stale overlays on data change / 数据变化时清除过期覆盖层
        UpdateNavButtons();
        UpdateEmptyState();
    }

    // On app-internal theme change: set page RequestedTheme directly (bypasses NavigationView propagation break)
    // / app内主题切换:直接设置页面RequestedTheme(绕过NavigationView传播断链)
    private void OnAppThemeChanged(object? sender, string theme)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            this.RequestedTheme = theme switch
            {
                "dark" => ElementTheme.Dark,
                "light" => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        });
    }

    // Unsubscribe to prevent leak / 取消订阅防止泄漏
    private bool _isUnloaded;
    private readonly HashSet<DispatcherTimer> _activeTimers = [];

    private DispatcherTimer CreateTimer(int intervalMs, Action onTick)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _activeTimers.Add(timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _activeTimers.Remove(timer);
            if (!_isUnloaded) onTick();
        };
        timer.Start();
        return timer;
    }

    private void ChangelogPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        ViewModel.CleanupTimers();
        if (RuntimeServices.ThemeService is { } ts)
            ts.ThemeChanged -= OnAppThemeChanged;
        this.Unloaded -= ChangelogPage_Unloaded;
        this.ActualThemeChanged -= ChangelogPage_ActualThemeChanged;
        foreach (var t in _activeTimers) t.Stop();
        _activeTimers.Clear();
        _navSettleTimer?.Stop();
        MarkdownStyler.CancelAllTimers();
    }

    // Whether current theme is dark / 当前是否深色主题
    private bool IsDark => this.ActualTheme == ElementTheme.Dark;

    // Cache MarkdownTextBlock.TextProperty via reflection (not public)
    // / 反射缓存MarkdownTextBlock.TextProperty(非public)
    private static readonly DependencyProperty? _mtbTextProperty =
        typeof(CommunityToolkit.WinUI.Controls.MarkdownTextBlock)
            .GetField("TextProperty",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as DependencyProperty;

    // On theme change: re-apply theme brushes and force re-render every MarkdownTextBlock
    // / 主题变化时重新应用主题画刷并强制重渲染所有MarkdownTextBlock
    private void ChangelogPage_ActualThemeChanged(FrameworkElement sender, object e)
        => MarkdownStyler.ReapplyTheme(ChangelogRepeater, IsDark, DispatcherQueue);
}
