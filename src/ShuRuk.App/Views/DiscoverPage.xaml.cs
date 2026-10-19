using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
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
    public string LastUpdatedText { get; init; } = string.Empty;
    public string CategoryText { get; init; } = string.Empty;
    public bool IsInstalled { get; init; }

    // Star rating glyph / 星级评分字形 (linear/outline style)
    public string StarGlyph => "\uE735";
    // Download icon glyph / 下载图标字形 (linear/outline style)
    public string DownloadGlyph => "\uE896";
}

public sealed partial class DiscoverPage : Page, ISearchablePage
{
    private string _currentKeyword = string.Empty;
    private DispatcherQueueTimer? _searchDebounceTimer;
    private ITestModeService? _testModeService;
    private bool _isLoaded;

    public string SortRatingText => LocalizationHelper.GetString("DiscoverSortRating.Content");
    public string SortDownloadsText => LocalizationHelper.GetString("DiscoverSortDownloads.Content");
    public string SortUpdatedText => LocalizationHelper.GetString("DiscoverSortUpdated.Content");
    public string SortA11yName => LocalizationHelper.GetString("String_DiscoverSortA11y");

    public DiscoverPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);

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

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged -= OnTestModeChanged;
            _testModeService.FillDataChanged -= OnTestModeChanged;
        }
    }

    private async void OnTestModeChanged(object? sender, bool enabled)
    {
        DispatcherQueue.TryEnqueue(() => _ = LoadDiscoverAsync(_currentKeyword, CurrentSortBy()));
    }

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

    private async void OnSearchDebounce(DispatcherQueueTimer sender, object args)
    {
        try
        {
            await LoadDiscoverAsync(_currentKeyword, CurrentSortBy());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnSearchDebounce error: {ex}");
        }
    }

    private string? CurrentSortBy() =>
        (SortComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

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
            LastUpdatedText = e.UpdatedAt.ToString("yyyy-MM-dd"),
            CategoryText = e.Category ?? string.Empty,
            IsInstalled = e.Installed
        };
    }

    private async Task LoadDiscoverAsync(string? keyword = null, string? sortBy = null)
    {
        // Test mode handling / 测试模式处理
        if (_testModeService?.IsEnabled == true)
        {
            if (_testModeService.IsFillDataEnabled)
            {
                var cards = TestDataProvider.GetFullDiscoverEntries()
                    .Select(ToCardViewModel).ToList();
                DiscoverGridView.ItemsSource = cards;
                DiscoverOfflineNotice.IsOpen = false;
                EmptyState.Visibility = Visibility.Collapsed;
            }
            else
            {
                DiscoverGridView.ItemsSource = new List<DiscoverCardViewModel>();
                DiscoverOfflineNotice.IsOpen = false;
                EmptyState.Visibility = Visibility.Visible;
            }
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
            EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            var cached = await discovery.GetCachedEntriesAsync();
            var cards = cached.Select(ToCardViewModel).ToList();

            DiscoverGridView.ItemsSource = cards;
            DiscoverOfflineNotice.IsOpen = true;
            EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ignore selection changed during initial load / 忽略初始加载期间的选择变化
        if (!_isLoaded) return;
        // Cancel any pending debounce and re-search immediately with the new sort.
        _searchDebounceTimer?.Stop();
        try
        {
            await LoadDiscoverAsync(_currentKeyword, CurrentSortBy());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SortComboBox_SelectionChanged error: {ex}");
        }
    }

    private async void DiscoverGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DiscoverCardViewModel card) return;

        var discovery = AppContextAccessor.Current.Services.GetRequiredService<IModuleDiscoveryService>();
        if (discovery is null) return;

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
}
