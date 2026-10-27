using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.App;
using ShuRuk.App.Abstractions.Interfaces;
using ShuRuk.App.Helpers;

using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Abstractions.Models;
using ShuRuk.Runtime.Abstractions.Services;

namespace ShuRuk.App.Views;

public class DiscoverCardViewModel
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string? Description { get; init; }
    public required string DownloadsText { get; init; }
    public required string RatingText { get; init; }
    public required ModuleDiscoveryEntry Entry { get; init; }

    // Extended display properties / 扩展显示属性
    public string Version { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string LastUpdatedText { get; init; } = string.Empty;
    public string CategoryText { get; init; } = string.Empty;
    public bool IsInstalled { get; init; }

    // Star rating glyph / 星级评分字形 (linear/outline style)
    public string StarGlyph => "\uE735";
    // Download icon glyph / 下载图标字形 (linear/outline style)
    public string DownloadGlyph => "\uE896";
}

public sealed partial class ModuleDiscoverPage : Page, ISearchablePage
{
    private string _currentKeyword = string.Empty;
    private DispatcherQueueTimer? _searchDebounceTimer;
    private ITestModeService? _testModeService;
    private bool _isLoaded;

    public string SortRatingText => LocalizationHelper.GetString("DiscoverSortRating.Content");
    public string SortDownloadsText => LocalizationHelper.GetString("DiscoverSortDownloads.Content");
    public string SortUpdatedText => LocalizationHelper.GetString("DiscoverSortUpdated.Content");
    public string SortA11yName => LocalizationHelper.GetString("String_DiscoverSortA11y");

    // Constructor: init and wire load/unload / 构造函数:初始化并挂载加载/卸载事件
    public ModuleDiscoverPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    // Apply localization, subscribe test mode, load entries / 应用本地化、订阅测试模式、加载条目
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);
        LoadingText.Text = LocalizationHelper.GetString("String_DiscoverLoading");

        // Subscribe to test mode changes / 订阅测试模式变化
        _testModeService = AppContextAccessor.Current.Services.GetService<ITestModeService>();
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged += OnTestModeChanged;
            _testModeService.FillDataChanged += OnTestModeChanged;
        }

        await LoadDiscoverAsync(_currentKeyword, CurrentSortBy());
        _isLoaded = true;
    }

    // Unsubscribe test mode events on unload / 卸载时取消订阅测试模式事件
    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _searchDebounceTimer?.Stop();
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged -= OnTestModeChanged;
            _testModeService.FillDataChanged -= OnTestModeChanged;
            _testModeService = null;
        }
    }

    // Test mode changed: reload entries on UI thread / 测试模式变更:在UI线程重新加载条目
    private async void OnTestModeChanged(object? sender, bool enabled)
    {
        DispatcherQueue.TryEnqueue(() => _ = LoadDiscoverAsync(_currentKeyword, CurrentSortBy()));
    }

    // Apply search keyword with debounce timer / 应用搜索关键词(含防抖计时器)
    public void ApplyFilter(string keyword)
    {
        _currentKeyword = keyword ?? string.Empty;

        // Debounce network-bound search to avoid firing on every keystroke.
        if (_searchDebounceTimer is null)
        {
            _searchDebounceTimer = DispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(AppConstants.SearchDebounceMs);
            _searchDebounceTimer.IsRepeating = false;
            _searchDebounceTimer.Tick += OnSearchDebounce;
        }
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    // Debounce tick: reload entries with current keyword/sort / 防抖触发:用当前关键词/排序重新加载
    private async void OnSearchDebounce(DispatcherQueueTimer sender, object args)
    {
        try
        {
            await LoadDiscoverAsync(_currentKeyword, CurrentSortBy());
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash("OnSearchDebounce", ex);
        }
    }

    private string _currentSortTag = "rating";

    // Get current sort tag / 获取当前排序标签
    private string? CurrentSortBy() => _currentSortTag;

    // Apply sort from external caller (ModulePage) / 外部调用排序
    public async void ApplySort(string tag)
    {
        _currentSortTag = tag;
        if (!_isLoaded) return;
        _searchDebounceTimer?.Stop();
        try
        {
            await LoadDiscoverAsync(_currentKeyword, CurrentSortBy());
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash("ApplySort", ex);
        }
    }

    // Convert discovery entry to card view model / 将发现条目转换为卡片视图模型
    private static DiscoverCardViewModel ToCardViewModel(ModuleDiscoveryEntry e)
    {
        return new DiscoverCardViewModel
        {
            Name = e.Name,
            DisplayName = e.DisplayName,
            Description = e.Description,
            DownloadsText = e.Downloads >= 1000
                ? $"{e.Downloads / 1000.0:F1}k"
                : e.Downloads.ToString(),
            RatingText = $"{e.Rating:F1}",
            Entry = e,
            Version = e.Version,
            Author = e.Author ?? string.Empty,
            LastUpdatedText = e.UpdatedAt.ToString("yyyy-MM-dd"),
            CategoryText = e.Category ?? string.Empty,
            IsInstalled = e.Installed
        };
    }

    // Load discover entries from API or test data with offline fallback / 从API或测试数据加载发现条目,含离线回退
    private async Task LoadDiscoverAsync(string? keyword = null, string? sortBy = null)
    {
        // Show loading state for async fetch / 异步获取时显示加载状态
        var isTestMode = _testModeService?.IsEnabled == true;
        if (!isTestMode)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            DiscoverGridView.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
        }

        // Test mode handling / 测试模式处理
        if (isTestMode)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            DiscoverGridView.Visibility = Visibility.Visible;
            if (_testModeService!.IsFillDataEnabled)
            {
                var cards = TestDataProvider.GetFullDiscoverEntries()
                    .Select(ToCardViewModel)
                    .Where(c => string.IsNullOrWhiteSpace(keyword)
                        || c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || c.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || (c.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
                DiscoverGridView.ItemsSource = cards;
                DiscoverOfflineNotice.IsOpen = false;
            }
            else
            {
                DiscoverGridView.ItemsSource = new List<DiscoverCardViewModel>();
                DiscoverOfflineNotice.IsOpen = false;
            }
            UpdateEmptyStateVisibility();
            return;
        }

        var discovery = AppContextAccessor.Current.Services.GetService<IModuleDiscoveryService>();
        if (discovery is null) return;

        try
        {
            var entries = await discovery.SearchModulesAsync(keyword, sortBy: sortBy);
            var cards = entries.Select(ToCardViewModel).ToList();

            DiscoverGridView.ItemsSource = cards;
            DiscoverOfflineNotice.IsOpen = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            DiscoverGridView.Visibility = Visibility.Visible;
            UpdateEmptyStateVisibility();
        }
        catch
        {
            var cached = await discovery.GetCachedEntriesAsync();
            var cards = cached.Select(ToCardViewModel).ToList();

            DiscoverGridView.ItemsSource = cards;
            DiscoverOfflineNotice.IsOpen = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            DiscoverGridView.Visibility = Visibility.Visible;
            UpdateEmptyStateVisibility();
        }
    }

    // Update empty state visibility with fade-in / 更新空状态可见性(含淡入动画)
    private void UpdateEmptyStateVisibility()
    {
        var count = (DiscoverGridView.ItemsSource as System.Collections.IList)?.Count ?? 0;
        var shouldShow = count == 0 && LoadingOverlay.Visibility == Visibility.Collapsed;
        if (shouldShow && EmptyState.Visibility != Visibility.Visible)
        {
            EmptyState.Opacity = 0;
            EmptyState.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase()
            };
            Storyboard.SetTarget(fadeIn, EmptyState);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(fadeIn);
            sb.Begin();
        }
        else if (!shouldShow)
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }
    }


    // Item click: confirm install dialog and refresh / 条目点击:确认安装对话框并刷新
    private async void DiscoverGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DiscoverCardViewModel card) return;

        var discovery = AppContextAccessor.Current.Services.GetRequiredService<IModuleDiscoveryService>();


        var confirmDialog = new ContentDialog
        {
            Title = LocalizationHelper.GetStringFormatted("String_DiscoverDialogInstallConfirmTitleFormat", card.DisplayName),
            Content = LocalizationHelper.GetStringFormatted(
                "String_DiscoverDialogInstallConfirmContentFormat",
                card.DisplayName,
                card.Description ?? LocalizationHelper.GetString("String_NotAvailable"),
                card.Entry.Repository),
            PrimaryButtonText = LocalizationHelper.GetString("String_DiscoverDialogInstallPrimary"),
            CloseButtonText = LocalizationHelper.GetString("String_DiscoverDialogCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await discovery.InstallFromDiscoveryAsync(card.Entry);
                await LoadDiscoverAsync(_currentKeyword, CurrentSortBy());
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = LocalizationHelper.GetString("String_DiscoverDialogInstallFailedTitle"),
                    Content = ex.Message,
                    CloseButtonText = LocalizationHelper.GetString("String_Ok"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }
    }

    // Recompute max columns on resize so centered grid stays page-width-bound / 尺寸变化时重算列数,使居中网格仍受页面宽度约束
    private void DiscoverGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        GridViewLayoutHelper.UpdateMaxColumns(DiscoverGridView, AppConstants.ModuleCardWidth);
    }
}
