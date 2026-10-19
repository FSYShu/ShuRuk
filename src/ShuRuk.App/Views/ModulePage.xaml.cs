using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.App;
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

    public ModulePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        InitializeComponent();
        Loaded += ModulePage_Loaded;
        ContentFrame.Navigated += ContentFrame_Navigated;
    }

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
    private void NavigateToIndex(int currentIndex, bool animate, bool useDrillIn = false)
    {
        Type? pageType = currentIndex switch
        {
            0 => typeof(InstalledPage),
            1 => typeof(DiscoverPage),
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
    }

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
        if (App.MainWindowStatic is MainWindow mainWindow)
        {
            mainWindow.UpdateBackButtonState();
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (ContentFrame.Content is ISearchablePage searchable)
        {
            searchable.ApplyFilter(sender.Text);
        }
    }

    public void SwitchToDiscoverTab()
    {
        var discoverItem = SubBar.Items.OfType<SelectorBarItem>().ElementAtOrDefault(1);
        if (discoverItem is not null)
        {
            SubBar.SelectedItem = discoverItem;
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
