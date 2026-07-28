using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.Host.Pages;

public sealed partial class InstalledPage : Page, ISearchablePage
{
    private readonly List<ModuleCardViewModel> _allCards = new();
    private string _currentKeyword = string.Empty;

    public InstalledPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);
        if (_allCards.Count == 0)
        {
            await LoadInstalledAsync();
        }
    }

    private async Task LoadInstalledAsync()
    {
        _allCards.Clear();

        if (App.Services.GetService<IModuleManager>() is IModuleManager manager)
        {
            foreach (var manifest in manager.GetInstalledModules())
            {
                ModuleState state;
                try
                {
                    state = await manager.GetModuleStateAsync(manifest.Name);
                }
                catch
                {
                    state = ModuleState.Discovered;
                }
                _allCards.Add(new ModuleCardViewModel
                {
                    Name = manifest.Name,
                    DisplayName = manifest.DisplayName,
                    Description = manifest.Description,
                    SourceBadge = manifest.Source == ModuleSource.BuiltIn ? "Built-in" : "External",
                    StateText = state.ToString(),
                    Source = manifest.Source,
                    State = state
                });
            }
        }

        ApplyFilter(_currentKeyword);
    }

    public void ApplyFilter(string keyword)
    {
        _currentKeyword = keyword ?? string.Empty;
        List<ModuleCardViewModel> filtered;

        if (string.IsNullOrWhiteSpace(_currentKeyword))
        {
            filtered = _allCards.ToList();
        }
        else
        {
            filtered = _allCards
                .Where(c =>
                    c.DisplayName.Contains(_currentKeyword, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(_currentKeyword, StringComparison.OrdinalIgnoreCase) ||
                    c.Description.Contains(_currentKeyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        InstalledGridView.ItemsSource = filtered;
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InstalledGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ModuleCardViewModel card)
        {
            Frame.Navigate(typeof(ModuleDetailPage), card);
        }
    }

    private void EmptyDiscoverButton_Click(object sender, RoutedEventArgs e)
    {
        FindParentModulePage()?.SwitchToDiscoverTab();
    }

    private ModulePage? FindParentModulePage()
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        // Walk up: Page → Frame → Grid → ModulePage
        while (parent is not null)
        {
            if (parent is Frame frame)
            {
                parent = VisualTreeHelper.GetParent(frame);
                continue;
            }
            if (parent is Grid grid)
            {
                parent = VisualTreeHelper.GetParent(grid);
                continue;
            }
            if (parent is ModulePage modulePage)
                return modulePage;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
