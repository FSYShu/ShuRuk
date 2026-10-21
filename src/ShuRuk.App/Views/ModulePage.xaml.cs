using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.App;
using ShuRuk.App.Abstractions.Interfaces;
using ShuRuk.App.Helpers;

namespace ShuRuk.App.Views;

public sealed partial class ModulePage : Page
{
    private int _previousIndex = 0;
    // Whether an inner detail page is active / 是否处于内嵌详情页
    private bool _isShowingDetail = false;
    // SubBar index to return to from detail / 从详情页返回的目标 SubBar 索引
    private int _detailReturnIndex = 0;

    public string TabBarA11yName => LocalizationHelper.GetString("String_ModulePageTabBarA11y");
    public string InstalledTabA11yName => LocalizationHelper.GetString("ModulePageInstalledTab.Content");
    public string DiscoverTabA11yName => LocalizationHelper.GetString("ModulePageDiscoverTab.Content");
    public string SortRatingText => LocalizationHelper.GetString("DiscoverSortRating.Content");
    public string SortDownloadsText => LocalizationHelper.GetString("DiscoverSortDownloads.Content");
    public string SortUpdatedText => LocalizationHelper.GetString("DiscoverSortUpdated.Content");

    // Constructor: enable cache, wire events / 构造函数:启用缓存、挂载事件
    public ModulePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        InitializeComponent();
        Loaded += ModulePage_Loaded;
        ContentFrame.Navigated += ContentFrame_Navigated;

    }

    // Apply localization and navigate to first tab on load / 加载时应用本地化并导航到首个标签
    private void ModulePage_Loaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);

        SearchBox.PlaceholderText = LocalizationHelper.GetString("String_SearchModules");

        if (ContentFrame.Content is null && SubBar.SelectedItem is SelectorBarItem first)
        {
            var index = SubBar.Items.IndexOf(first);
            if (index >= 0) NavigateToIndex(index, animate: false);
        }
    }

    // SubBar tab changed: navigate to selected index / SubBar标签变更:导航到选中索引
    private void SubBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selectedItem = sender.SelectedItem as SelectorBarItem;
        if (selectedItem is null) return;

        var currentIndex = sender.Items.IndexOf(selectedItem);
        if (currentIndex < 0) return;

        NavigateToIndex(currentIndex, animate: true);
    }

    // Pattern adapted from the official WinUI Gallery SelectorBarPage sample:
    // Frame.Navigate(pageType, null, new SlideNavigationTransitionInfo { Effect = ... })
    // Navigate to installed/discover page with slide or drill-in / 导航到已安装/发现页,含滑动或钻取动画
    private void NavigateToIndex(int currentIndex, bool animate, bool useDrillIn = false)
    {
        Type? pageType = currentIndex switch
        {
            0 => typeof(ModuleInstalledPage),
            1 => typeof(ModuleDiscoverPage),
            _ => null
        };
        if (pageType is null) return;
        if (ContentFrame.CurrentSourcePageType == pageType) return;

        NavigationTransitionInfo? transition = null;
        if (animate)
        {
            if (useDrillIn)
            {
                // DrillIn for detail→list back navigation / 钻取动画用于详情→列表返回
                transition = new DrillInNavigationTransitionInfo();
            }
            else
            {
                // Slide for tab switching / 滑动动画用于标签切换
                var effect = currentIndex - _previousIndex > 0
                    ? SlideNavigationTransitionEffect.FromRight
                    : SlideNavigationTransitionEffect.FromLeft;
                transition = new SlideNavigationTransitionInfo { Effect = effect };
            }
        }

        ContentFrame.Navigate(pageType, null, transition);
        _previousIndex = currentIndex;
        // Clear search on tab switch (not detail back) / 切换标签时清空搜索(详情返回不清空)
        if (!useDrillIn)
            SearchBox.Text = string.Empty;
    }

    // Frame navigated: update detail state, top bar, search / Frame导航完成:更新详情状态、顶栏、搜索
    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Track detail navigation for hierarchical back / 跟踪详情页导航以支持层级返回
        bool isDetail = e.Content is ModuleDetailPage;
        if (isDetail)
        {
            _isShowingDetail = true;
            _detailReturnIndex = _previousIndex;
        }
        else
        {
            _isShowingDetail = false;
        }

        // Hide top bar (selector + search) on detail page / 详情页隐藏顶栏（选择器+搜索）
        TopBar.Visibility = isDetail ? Visibility.Collapsed : Visibility.Visible;

        // Show sort button only on discover page / 仅发现页显示排序按钮
        SortButton.Visibility = (e.Content is ModuleDiscoverPage && !isDetail) ? Visibility.Visible : Visibility.Collapsed;

        // Update MainWindow back button state / 更新主窗口返回按钮状态
        UpdateMainWindowBackButton();

        // Re-apply the current search keyword to the page that just loaded.
        if (e.Content is ISearchablePage searchable)
        {
            searchable.ApplyFilter(SearchBox.Text);
        }
    }

    // Refresh MainWindow TitleBar back button enabled state / 刷新主窗口标题栏返回按钮启用状态
    private void UpdateMainWindowBackButton()
    {
        ((MainWindow)App.MainWindowStatic).UpdateBackButtonState();
    }

    // Search box text changed: apply filter to current page / 搜索框文本变更:对当前页应用过滤
    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (ContentFrame.Content is ISearchablePage searchable)
        {
            searchable.ApplyFilter(sender.Text);
        }
    }

    // Switch SubBar to the discover tab / 切换SubBar到发现标签
    public void SwitchToDiscoverTab()
    {
        var discoverItem = SubBar.Items.OfType<SelectorBarItem>().ElementAtOrDefault(1);
        if (discoverItem is not null)
        {
            SubBar.SelectedItem = discoverItem;
        }
    }

    // Sort menu item clicked: apply sort to discover page / 排序菜单项点击:对发现页应用排序
    private void SortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        SortButton.Content = item.Text;
        if (ContentFrame.Content is ModuleDiscoverPage discover)
        {
            discover.ApplySort(item.Tag?.ToString() ?? AppConstants.DiscoverSort.Rating);
        }
    }

    // Whether an inner detail page is active for hierarchical back / 是否处于内嵌详情页以支持层级返回
    public bool CanGoBackInner => _isShowingDetail;

    // Navigate back from detail to the originating SubBar page / 从详情页返回至来源 SubBar 页
    public void GoBackInner()
    {
        if (!_isShowingDetail) return;
        // Use DrillIn for consistent back animation / 用 DrillIn 保持返回动画一致
        NavigateToIndex(_detailReturnIndex, animate: true, useDrillIn: true);
    }
}
