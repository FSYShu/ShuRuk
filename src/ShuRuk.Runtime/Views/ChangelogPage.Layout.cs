using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.Runtime.Controls;
using ShuRuk.Runtime.Helpers;
using ShuRuk.Runtime.Models;

using ShuRuk.Runtime.ViewModels;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Cache of rendered card heights by index / 卡片渲染高度缓存(按索引)
    // Fixes PREV nav drift: prev card virtualized → ActualHeight is unrendered height
    // / 修复PREV导航偏移:prev卡片虚拟化回收后ActualHeight为未渲染高度
    private readonly Dictionary<int, double> _cardHeightCache = [];
    // Entries with a pending overflow measure; prevents layout-cycle re-entrancy
    // / 待测量溢出条目集合;防止布局循环重入
    private readonly HashSet<ChangelogEntry> _pendingOverflowMeasure = [];

    // Currently expanded history card entry (only one at a time)
    // / 当前展开的 history 卡片条目(同一时间仅一个)
    private ChangelogEntry? _expandedHistoryEntry;
    // True while a history card's expander animation is in progress;
    // prevents auto-collapse from misfiring during the ~300ms expand animation
    // / history 卡片展开动画进行中为 true;防止 ~300ms 展开动画期间自动收起误触发
    private bool _isExpandingHistory;

    private static void OnEntriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChangelogPage page)
        {
            // Store source entries then apply filter / 存储原始条目后应用过滤
            page._cardHeightCache.Clear();
            page.ViewModel.SourceEntries.Clear();
            page.ViewModel.SourceEntries.AddRange(page.Entries ?? []);
            // Parse segments for each entry, skip if already parsed (e.g. cache reuse)
            // / 为每个条目解析段,已解析则跳过(如缓存复用)
            foreach (var entry in page.ViewModel.SourceEntries)
            {
                // Fallback ReleaseName to Version for old cache without name / 旧缓存无name时回退到版本号
                if (string.IsNullOrWhiteSpace(entry.ReleaseName)) entry.ReleaseName = entry.Version;
                // Always re-parse to ensure correct segment order (text/image alternating)
                // / 始终重新解析确保正确的段顺序(文字图片交替)
                entry.Segments = ChangelogSegment.Parse(entry.Notes);
                entry.InitializeVisibleSegments();
                // Subscribe to PropertyChanged for IsExpanded changes
                // (Expander Expanding/Collapsed events don't fire with TwoWay binding)
                // / 订阅PropertyChanged监听IsExpanded变化
                // (Expander的Expanding/Collapsed事件在TwoWay绑定下不触发)
                entry.PropertyChanged += page.OnEntryPropertyChanged;
            }
            page.ViewModel.ApplyFilter();
            // Assign card modes and load all items at once / 分配卡片模式并一次性加载全部条目
            page.ViewModel.AssignCardModes();
            page.ViewModel.LoadAll();
            // Refresh localized properties after data load (localization may load after bindings)
            // / 数据加载后刷新本地化属性(本地化可能在绑定后加载)
            foreach (var entry in page.ViewModel.SourceEntries)
                entry.RefreshLocalization();
            // Reset StackLayout to clear polluted firstRealizedItemOffset from navigation transition
            // / 重置StackLayout清除导航过渡污染的firstRealizedItemOffset
            page.ChangelogRepeater.Layout = new StackLayout();
            // Sync DropDownButton content with restored filter tag / 同步DropDownButton显示与恢复的过滤标签
            page.FilterButton.Content = page._filterTag == ChangelogViewModel.FilterTagExcludePrerelease
                ? page.FilterExcludePrereleaseText : page.FilterAllText;
            page.UpdateNavButtons();
            // Hide error state only when data successfully loaded / 仅成功加载数据时隐藏错误状态
            if (page.ViewModel.SourceEntries.Count > 0) FadeOut(page.ErrorState);

            // Force layout next frame: ItemsRepeater virtualization may skip realization
            // / 下一帧强制布局:ItemsRepeater虚拟化可能跳过实现
            page.DispatcherQueue.TryEnqueue(() =>
            {
                page.ForceFullLayout();
                page.ChangelogScrollViewer.ChangeView(null, 0, null, true);
                page.ChangelogRepeater.InvalidateMeasure();
                page.ChangelogScrollViewer.UpdateLayout();
                page.ChangelogScrollViewer.ChangeView(null, 0, null, true);
                page.ForceFullLayout();
                try
                {
                    int checkCount = VisualTreeHelper.GetChildrenCount(page.ChangelogRepeater);
                    if (checkCount > 0
                        && VisualTreeHelper.GetChild(page.ChangelogRepeater, 0) is FrameworkElement checkCard)
                    {
                        var checkT = checkCard.TransformToVisual(page.ChangelogScrollViewer);
                        var checkPos = checkT.TransformPoint(new Windows.Foundation.Point(0, 0));
                        double vw = page.ChangelogScrollViewer.ViewportWidth;
                        double vh = page.ChangelogScrollViewer.ViewportHeight;
                        if ((Math.Abs(checkPos.X) > vw || Math.Abs(checkPos.Y) > vh) && page._displayItems.Count > 0)
                        {
                            var items = page._displayItems;
                            page.ChangelogRepeater.ItemsSource = null;
                            page.ChangelogRepeater.Layout = new StackLayout();
                            page.ChangelogRepeater.ItemsSource = items;
                            page.ForceFullLayout();
                            page.ChangelogScrollViewer.ChangeView(null, 0, null, true);
                            page.ForceFullLayout();
                        }
                    }
                }
                catch { }
                if (page.CardsLayoutIncomplete() && page._displayItems.Count > 0)
                {
                    var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    int attempts = 0;
                    const int maxAttempts = 10;
                    poll.Tick += (_, _) =>
                    {
                        attempts++;
                        if (!page.CardsLayoutIncomplete() || attempts >= maxAttempts)
                        {
                            poll.Stop();
                            if (page.CardsLayoutIncomplete() && page._displayItems.Count > 0)
                            {
                                var items = page._displayItems;
                                page.ChangelogRepeater.ItemsSource = null;
                                page.ChangelogRepeater.Layout = new StackLayout();
                                page.ChangelogRepeater.ItemsSource = items;
                                page.ForceFullLayout();
                                if (page.CardsLayoutIncomplete() && page._displayItems.Count > 0)
                                {
                                    page.ChangelogScrollViewer.ChangeView(null, 0, null, true);
                                    page.ForceFullLayout();
                                }
                            }
                            page.EnsureCardsVisible();
                            return;
                        }
                        if (attempts <= 2)
                            page.ForceFullLayout();
                        else
                        {
                            page.ChangelogRepeater.InvalidateMeasure();
                            page.ChangelogScrollViewer.UpdateLayout();
                        }
                    };
                    poll.Start();
                }
                page.EnsureCardsVisible();
            });
        }
    }

    // Check whether ContentPanel and realized cards have non-zero size
    // / 检查ContentPanel和已实现卡片是否有非零尺寸
    private bool CardsLayoutIncomplete()
    {
        if (ContentPanel.ActualWidth == 0 || ContentPanel.ActualHeight == 0) return true;
        int count = VisualTreeHelper.GetChildrenCount(ChangelogRepeater);
        if (count == 0) return true;
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is FrameworkElement fe
                && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                return false;
        }
        return true;
    }

    // Force full layout pass on ScrollViewer + ContentPanel + ItemsRepeater
    // / 强制ScrollViewer+ContentPanel+ItemsRepeater完整布局
    private void ForceFullLayout()
    {
        ChangelogScrollViewer.InvalidateMeasure();
        ChangelogScrollViewer.InvalidateArrange();
        ContentPanel.InvalidateMeasure();
        ContentPanel.InvalidateArrange();
        ChangelogRepeater.InvalidateMeasure();
        ChangelogRepeater.InvalidateArrange();
        ChangelogScrollViewer.UpdateLayout();
    }

    // Track rendered card height for nav offset calculation / 追踪卡片渲染高度供导航偏移计算
    private void CardGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        try
        {
            int idx = ChangelogRepeater.GetElementIndex(element);
            if (idx >= 0) _cardHeightCache[idx] = e.NewSize.Height;
            // Cache collapsed history card height for nav optimization (fixed height, no need to re-measure)
            // / 缓存折叠 history 卡片高度供导航优化(高度固定,无需重新测量)
            if (idx >= 0 && idx < _displayItems.Count
                && _displayItems[idx].CardMode == CardMode.HistoryVersion && !_displayItems[idx].IsExpanded
                && _collapsedOtherCardHeight == 0)
                _collapsedOtherCardHeight = e.NewSize.Height;
            // Re-evaluate floating collapse button when expanded card height changes (markdown async render)
            // / 展开卡片高度变化时重新评估浮动收起按钮(markdown异步渲染)
            if (idx == _lastExpandedIndex && _lastExpandedEntry is not null && _lastExpandedEntry.IsExpanded)
                UpdateCollapseButton();
        }
        catch { }
    }

    // Content (ItemsRepeater) size changed: re-measure overflow after async markdown render
    // / 内容尺寸变化:异步markdown渲染后重新测量溢出
    private void CardContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement content) return;
        // Find CardContentGrid ancestor to compare content height vs MaxHeight
        // / 查找CardContentGrid祖先比较内容高度与MaxHeight
        DependencyObject? node = content;
        while (node is not null)
        {
            if (node is Grid g && g.Name == "CardContentGrid")
            {
                ScheduleOverflowMeasureFromGrid(g, content);
                return;
            }
            node = VisualTreeHelper.GetParent(node);
        }
    }

    // Resolve ChangelogEntry from a card-internal element via repeater index
    // / 通过repeater索引从卡片内部元素解析ChangelogEntry
    private ChangelogEntry? GetEntryFromCardElement(DependencyObject element)
    {
        // Walk up to find direct child of ChangelogRepeater (card root)
        // / 向上查找ChangelogRepeater的直接子元素(卡片根)
        DependencyObject? node = element;
        while (node is not null)
        {
            var parent = VisualTreeHelper.GetParent(node);
            if (parent == ChangelogRepeater && node is FrameworkElement fe)
            {
                try
                {
                    int idx = ChangelogRepeater.GetElementIndex(fe);
                    if (idx >= 0 && idx < _displayItems.Count) return _displayItems[idx];
                }
                catch { }
                return null;
            }
            node = parent;
        }
        return null;
    }

    // Defer overflow measurement: measure content's natural height vs CardContentGrid MaxHeight
    // / 延迟溢出测量:测量内容自然高度与CardContentGrid MaxHeight比较
    // content.ActualHeight is constrained by parent MaxHeight, so we use Measure
    // with infinite height to get the natural unconstrained height
    // / content.ActualHeight受父级MaxHeight约束,故用Measure无限高度获取自然高度
    private void ScheduleOverflowMeasureFromGrid(Grid cardGrid, FrameworkElement content)
    {
        ChangelogEntry? entry = GetEntryFromCardElement(cardGrid);
        if (entry is null) return;
        if (entry.IsExpanded) return;
        if (entry.CardMode == CardMode.HistoryVersion) return;
        if (!_pendingOverflowMeasure.Add(entry)) return;
        var entryRef = entry;
        var cardGridRef = cardGrid;
        var contentRef = content;
        cardGrid.DispatcherQueue.TryEnqueue(() =>
        {
            _pendingOverflowMeasure.Remove(entryRef);
            if (entryRef.IsExpanded) return;
            if (entryRef.CardMode == CardMode.HistoryVersion) return;
            // Measure content with infinite height to get natural unconstrained height
            // / 用无限高度测量内容获取自然不受约束的高度
            double availableWidth = cardGridRef.ActualWidth > 0
                ? cardGridRef.ActualWidth - cardGridRef.Padding.Left - cardGridRef.Padding.Right
                : double.PositiveInfinity;
            contentRef.Measure(new Windows.Foundation.Size(availableWidth, double.PositiveInfinity));
            double naturalHeight = contentRef.DesiredSize.Height;
            entryRef.MeasuredOverflow = naturalHeight > ChangelogEntry.CollapsedMaxHeight + 1;
        });
    }

    // Card appear: no slide-in animation to avoid Storyboard GC pressure during scroll
    // / 卡片出现:无滑入动画避免滚动时Storyboard GC压力
    private void CardGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        // Track height changes for nav offset cache / 追踪高度变化供导航偏移缓存
        element.SizeChanged += CardGrid_SizeChanged;

        int cardIdx = -1;
        try { cardIdx = ChangelogRepeater.GetElementIndex(element); } catch { }

        // Schedule overflow check via CardContentGrid / 通过CardContentGrid调度溢出检查
        try
        {
            if (cardIdx >= 0 && cardIdx < _displayItems.Count
                && UiTreeHelper.FindDescendantByName(element, "CardContentGrid") is Grid cardGrid
                && UiTreeHelper.FindDescendantByName(cardGrid, "FeaturedContentRepeater") is ItemsRepeater content)
                ScheduleOverflowMeasureFromGrid(cardGrid, content);
        }
        catch { }

        // Virtualization recovery: if a re-realized history card has IsExpanded=true
        // but is not the tracked expanded entry, collapse it to prevent dual expansion
        // / 虚拟化恢复:重新实现的 history 卡片 IsExpanded=true 但不是追踪的展开条目时,收起它
        if (cardIdx >= 0 && cardIdx < _displayItems.Count)
        {
            var loadedEntry = _displayItems[cardIdx];
            if (loadedEntry.CardMode == CardMode.HistoryVersion && loadedEntry.IsExpanded
                && !ReferenceEquals(loadedEntry, _expandedHistoryEntry))
            {
                loadedEntry.IsExpanded = false;
            }
        }

        // Ensure visible immediately — no animation during scroll / 滚动时立即可见——无动画
        if (!_isLoadingOverlayActive)
        {
            element.Opacity = 1;
            if (element.RenderTransform is TranslateTransform tt) tt.Y = 0;
        }
    }

    // Ensure all realized cards are visible after layout fixes / 布局修复后确保所有已实现卡片可见
    private void EnsureCardsVisible()
    {
        if (_isLoadingOverlayActive) return;
        int count = VisualTreeHelper.GetChildrenCount(ChangelogRepeater);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is FrameworkElement fe && fe.Opacity < 1)
            {
                fe.Opacity = 1;
                if (fe.RenderTransform is TranslateTransform tt) tt.Y = 0;
            }
        }
    }

    // Refresh all MarqueeText controls inside a card element / 刷新卡片内所有跑马灯控件
    private static void RefreshMarqueeInCard(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is MarqueeText mt) mt.Refresh();
            else RefreshMarqueeInCard(child);
        }
    }

    // Fade in element with opacity + upward float (CubicEase EaseOut)
    // / 淡入元素:透明度+上浮(CubicEase EaseOut)
    private static void FadeIn(FrameworkElement element)
    {
        if (element.Visibility == Visibility.Visible && element.Opacity >= 1) return;
        element.Opacity = 0;
        element.Visibility = Visibility.Visible;
        var tt = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        tt.Y = PanelSlideDistance;
        element.RenderTransform = tt;
        var sb = new Storyboard();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacityAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(StateAnimMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacityAnim, element);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");
        sb.Children.Add(opacityAnim);
        var yAnim = new DoubleAnimation
        {
            From = PanelSlideDistance,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(StateAnimMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(yAnim, tt);
        Storyboard.SetTargetProperty(yAnim, "Y");
        sb.Children.Add(yAnim);
        sb.Begin();
    }

    // Fade out element then collapse (CubicEase EaseIn)
    // / 淡出元素后折叠(CubicEase EaseIn)
    private static void FadeOut(FrameworkElement element)
    {
        if (element.Visibility == Visibility.Collapsed) return;
        var anim = new DoubleAnimation
        {
            From = element.Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(StateAnimMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(anim, element);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (_, _) => element.Visibility = Visibility.Collapsed;
        sb.Begin();
    }
}
