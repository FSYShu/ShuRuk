using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.Contracts.Interfaces;
using ShuRuk.Contracts.Models;

namespace ShuRuk.App.Pages;

public class DiscoverCardViewModel
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string? Description { get; init; }
    public required string DownloadsText { get; init; }
    public required string RatingText { get; init; }
    public required ModuleDiscoveryEntry Entry { get; init; }
}

public sealed partial class DiscoverPage : Page, ISearchablePage
{
    private string _currentKeyword = string.Empty;
    private DispatcherQueueTimer? _searchDebounceTimer;

    public string SortRatingText => LocalizationHelper.GetString("DiscoverSortRating.Content");
    public string SortDownloadsText => LocalizationHelper.GetString("DiscoverSortDownloads.Content");
    public string SortUpdatedText => LocalizationHelper.GetString("DiscoverSortUpdated.Content");
    public string SortA11yName => LocalizationHelper.GetString("String_DiscoverSortA11y");

    public DiscoverPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);
        if (DiscoverGridView.ItemsSource is null)
        {
            await LoadDiscoverAsync(_currentKeyword, CurrentSortBy());
        }
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
            DownloadsText = $"{LocalizationHelper.GetString("String_Downloads")} {e.Downloads}",
            RatingText = $"{LocalizationHelper.GetString("String_Rating")} {e.Rating:F1}",
            Entry = e
        };
    }

    private async Task LoadDiscoverAsync(string? keyword = null, string? sortBy = null)
    {
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
