using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.App.ViewModels;
using ShuRuk.Runtime.Abstractions.Models;
using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Abstractions.Services;

namespace ShuRuk.App.Views;

public sealed partial class InstalledPage : Page, ISearchablePage
{
    private readonly List<ModuleCardViewModel> _allCards = new();
    private string _currentKeyword = string.Empty;
    private ITestModeService? _testModeService;

    public InstalledPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);


        // Subscribe to test mode changes / 订阅测试模式变化
        _testModeService = AppContextAccessor.Current.Services.GetService<ITestModeService>();
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged += OnTestModeChanged;
            _testModeService.FillDataChanged += OnTestModeChanged;
        }

        // Always reload to reflect current test mode / 总是重新加载以反映当前测试模式
        _ = LoadInstalledAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged -= OnTestModeChanged;
            _testModeService.FillDataChanged -= OnTestModeChanged;
        }
    }

    // Reload modules when test mode toggles / 测试模式切换时重新加载模块
    private async void OnTestModeChanged(object? sender, bool enabled)
    {
        _allCards.Clear();
        await LoadInstalledAsync();
    }


    private async Task LoadInstalledAsync()
    {
        _allCards.Clear();

        // Use test data when test mode is enabled / 测试模式开启时使用测试数据
        var testModeService = AppContextAccessor.Current.Services.GetService<ITestModeService>();
        if (testModeService?.IsEnabled == true)
        {
            LoadTestModules();
            ApplyFilter(_currentKeyword);
            return;
        }

        if (AppContextAccessor.Current.Services.GetService<IModuleManager>() is IModuleManager manager)
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
                    SourceBadge = manifest.Source == ModuleSource.BuiltIn
                        ? LocalizationHelper.GetString("String_ModuleDetailSourceBuiltIn")
                        : LocalizationHelper.GetString("String_ModuleDetailSourceExternal"),
                    StateText = state.ToString(),
                    Source = manifest.Source,
                    State = state,
                    Version = manifest.Version
                });
            }
        }

        ApplyFilter(_currentKeyword);
    }

    // Load test modules for test mode / 测试模式下加载测试模块
    private void LoadTestModules()
    {
        // Fill data flag: full scrollable list or empty state / 填充标志: 足量可滚动列表或空状态
        var testModeService = AppContextAccessor.Current.Services.GetService<ITestModeService>();
        if (testModeService?.IsFillDataEnabled != true) return;

        foreach (var info in TestDataProvider.GetFullTestModules())
        {
            _allCards.Add(new ModuleCardViewModel
            {
                Name = info.Name,
                DisplayName = info.DisplayName,
                Description = info.Description,
                SourceBadge = info.Source == ModuleSource.BuiltIn
                    ? LocalizationHelper.GetString("String_ModuleDetailSourceBuiltIn")
                    : LocalizationHelper.GetString("String_ModuleDetailSourceExternal"),
                StateText = info.State.ToString(),
                Source = info.Source,
                State = info.State,
                Version = info.Version
            });
        }
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
            // Drill-in transition for list→detail navigation / 钻取动画用于列表→详情导航
            var transition = new DrillInNavigationTransitionInfo();
            Frame.Navigate(typeof(ModuleDetailPage), card, transition);
        }
    }

    private void EmptyDiscoverButton_Click(object sender, RoutedEventArgs e)
    {
        FindParentModulePage()?.SwitchToDiscoverTab();
    }

    private ModulePage? FindParentModulePage()
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        // Walk up: Page 閿?Frame 閿?Grid 閿?ModulePage
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
