using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.Runtime.Controls;
using ShuRuk.Runtime.Helpers;
using ShuRuk.Runtime.Models;
using ShuRuk.Runtime.Services.Changelog;
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

    // Load more items: split-frame add spreads MarkdownTextBlock render cost across frames
    // / 加载更多:分帧添加分散MarkdownTextBlock渲染开销
    // (LoadMore logic is in ViewModel; this wraps with re-entrancy guard)
    // / (LoadMore逻辑在ViewModel中;此处包装防重入)
    private bool _isLoadingMore;

    private void LoadMore(bool defer = true)
    {
        if (_isLoadingMore) return;
        _isLoadingMore = true;
        ViewModel.LoadMore(defer);
        // Clear flag after short delay to allow batch rendering / 短暂延迟后清除标志允许批次渲染
        CreateTimer(50, () => _isLoadingMore = false);
    }

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
                if (entry.Segments is null || entry.Segments.Count == 0)
                {
                    entry.Segments = ChangelogSegment.Parse(entry.Notes);
                    entry.InitializeVisibleSegments();
                }
            }
            page.ViewModel.ApplyFilter();
            // Reset StackLayout to clear polluted firstRealizedItemOffset from navigation transition
            // / 重置StackLayout清除导航过渡污染的firstRealizedItemOffset
            // StackLayout caches the offset; InvalidateArrange/re-attach ItemsSource do NOT reset it,
            // / StackLayout缓存该偏移;InvalidateArrange/re-attach ItemsSource均不重置,
            // leaving cards arranged at large negative Y (-11000+) off-screen after transition ends.
            // / 致过渡结束后卡片被排到巨大负Y(-11000+)视口外.
            page.ChangelogRepeater.Layout = new StackLayout();
            // Sync DropDownButton content with restored filter tag / 同步DropDownButton显示与恢复的过滤标签
            page.FilterButton.Content = page._filterTag == ChangelogViewModel.FilterTagExcludePrerelease
                ? page.FilterExcludePrereleaseText : page.FilterAllText;
            page.UpdateNavButtons();
            // Hide error state only when data successfully loaded / 仅成功加载数据时隐藏错误状态
            if (page.ViewModel.SourceEntries.Count > 0) FadeOut(page.ErrorState);
            ChangelogCacheService.NavLog(
                $"OnEntriesChanged: display={page._displayItems.Count} " +
                $"scrollH={page.ChangelogScrollViewer.ActualHeight:F1} " +
                $"viewportH={page.ChangelogScrollViewer.ViewportHeight:F1} " +
                $"repeaterH={page.ChangelogRepeater.ActualHeight:F1} " +
                $"children={VisualTreeHelper.GetChildrenCount(page.ChangelogRepeater)}");
            // Force layout next frame: ItemsRepeater virtualization may skip realization
            // / 下一帧强制布局:ItemsRepeater虚拟化可能跳过实现
            // when ScrollViewer ViewportHeight not yet settled on sync Entries assignment
            // / 同步赋值Entries时ScrollViewer视口高度未稳定
            // During SlideNavigationTransition the RealizationRect may stay empty even though ActualHeight>0,
            // / 导航滑动过渡期间即使ActualHeight>0,RealizationRect可能仍为空,
            // so a single InvalidateMeasure is insufficient — poll until children appear or timeout.
            // / 故单次InvalidateMeasure不够——需轮询直到子元素出现或超时
            page.DispatcherQueue.TryEnqueue(() =>
            {
                page.ForceFullLayout();
                // Force scroll to top: InvalidateArrange may leave cards at wrong positions
                // / 强制滚动到顶部:InvalidateArrange可能使卡片位置错误
                page.ChangelogScrollViewer.ChangeView(null, 0, null, true);
                // Light layout nudge after sync ChangeView (was full ForceFullLayout)
                // / 同步ChangeView后轻量布局推动(原为完整ForceFullLayout)
                page.ChangelogRepeater.InvalidateMeasure();
                page.ChangelogScrollViewer.UpdateLayout();
                // Force scroll to top: InvalidateArrange may leave cards at wrong positions
                // / 强制滚动到顶部:InvalidateArrange可能使卡片位置错误
                page.ChangelogScrollViewer.ChangeView(null, 0, null, true);
                page.ForceFullLayout();
                // Check if first card is positioned off-screen; if so, StackLayout internal state is corrupt
                // / 检查首卡片是否在视口外;若是,StackLayout内部状态已损坏
                // Re-attach ItemsSource to force ItemsRepeater to reset its layout state
                // / 重新绑定ItemsSource以强制ItemsRepeater重置布局状态
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
                    const int maxAttempts = 10; // 50ms * 10 = 500ms / 轮询500ms
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
                        // Only full ForceFullLayout on first 2 ticks; lighter invalidation otherwise
                        // / 仅前2次完整ForceFullLayout;其余轻量失效
                        // Reduces UI thread contention: most ticks just nudge layout, not force full pass
                        // / 减少UI线程争用:多数tick仅推动布局非强制完整pass
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
                // Fallback: ensure cards visible after layout fixes / 兜底:布局修复后确保卡片可见
                page.EnsureCardsVisible();
            });
        }
    }

    // Check whether ContentPanel and realized cards have non-zero size
    // / 检查ContentPanel和已实现卡片是否有非零尺寸
    // Navigation transition may skip Arrange on ContentPanel itself (not just cards),
    // / 导航过渡可能跳过ContentPanel本身的Arrange(不只是卡片),
    // leaving ContentPanel with BoundingRectangle 0,0,0,0 even though ScrollViewer is laid out.
    // / 导致ContentPanel BoundingRectangle=0,0,0,0即使ScrollViewer已布局。
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
            // Re-evaluate floating collapse button when expanded card height changes (markdown async render)
            // / 展开卡片高度变化时重新评估浮动收起按钮(markdown异步渲染)
            if (idx == _lastExpandedIndex && _lastExpandedEntry is not null && _lastExpandedEntry.IsExpanded)
                UpdateCollapseButton();
        }
        catch { }
    }

    // Measure card content overflow via ScrollViewer extent vs viewport
    // / 通过ScrollViewer范围与视口比较测量卡片内容溢出
    private void CardScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        ScheduleOverflowMeasure(sv);
    }

    // Content (ItemsRepeater) size changed: re-measure after async markdown render
    // / 内容尺寸变化:异步markdown渲染后重新测量
    private void CardContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement content) return;

        // Walk up visual tree to find enclosing ScrollViewer / 向上遍历可视化树查找包裹的ScrollViewer
        DependencyObject? node = content;
        ScrollViewer? sv = null;
        while (node is not null)
        {
            if (node is ScrollViewer found) { sv = found; break; }
            node = VisualTreeHelper.GetParent(node);
        }
        if (sv is null) return;
        ScheduleOverflowMeasure(sv);
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

    // Defer measurement to next layout pass so ExtentHeight reflects final render
    // / 延迟到下一布局帧测量,确保ExtentHeight反映最终渲染
    private void ScheduleOverflowMeasure(ScrollViewer sv, ChangelogEntry? knownEntry = null)
    {
        ChangelogEntry? entry = knownEntry ?? sv.DataContext as ChangelogEntry ?? GetEntryFromCardElement(sv);
        if (entry is null) return;
        if (entry.IsExpanded) return;
        if (!_pendingOverflowMeasure.Add(entry)) return;
        var entryRef = entry;
        sv.DispatcherQueue.TryEnqueue(() =>
        {
            _pendingOverflowMeasure.Remove(entryRef);
            if (entryRef.IsExpanded) return;
            // Guard against virtualization container recycling: sv may now show a different card
            // / 防止虚拟化容器回收:sv可能已切换到其他卡片
            if (sv.DataContext is ChangelogEntry dc && !ReferenceEquals(dc, entryRef)) return;
            entryRef.MeasuredOverflow = sv.ExtentHeight > sv.ActualHeight + 1;
        });
    }

    // Card appear animation / 卡片出现动画
    private void CardGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        // Track height changes for nav offset cache / 追踪高度变化供导航偏移缓存
        element.SizeChanged += CardGrid_SizeChanged;

        // Fallback measure FIRST: resolve entry via repeater index and schedule overflow check
        // / 兜底测量优先:通过repeater索引解析条目并调度溢出检查
        // Must run before animation which may throw and block subsequent code
        // / 必须在动画前执行,动画可能抛异常阻断后续代码
        try
        {
            int cardIdx = ChangelogRepeater.GetElementIndex(element);
            var cardSv = UiTreeHelper.FindDescendant<ScrollViewer>(element);
            if (cardIdx >= 0 && cardIdx < _displayItems.Count && cardSv is ScrollViewer sv)
                ScheduleOverflowMeasure(sv, _displayItems[cardIdx]);
        }
        catch { }

        if (!_isLoadingOverlayActive)
        {
            if (_isNavigating)
            {
                // Navigation-induced realization (virtualization re-cycle): show immediately
                // / 导航触发的实现(虚拟化回收):立即显示
                // without slide-in, else RenderTransform.Y=16 pollutes TransformToVisual
                // / 否则RenderTransform.Y=16污染TransformToVisual
                // and shifts the card mid-version / 并使卡片停在版本中间
                element.Opacity = 1;
                if (element.RenderTransform is TranslateTransform tt) tt.Y = 0;
                ChangelogCacheService.NavLog($"CardGrid_Loaded: NAV mode, set Opacity=1 Y=0");
            }
            else
            {
                // Scroll-loaded card: animate directly / 滚动加载的卡片:直接动画
                ChangelogCacheService.NavLog($"CardGrid_Loaded: slide-in animation (Y={CardSlideDistance})");
                PlaySlideInAnimation(element, CardSlideDistance, SlideInAnimMs);
            }
        }
    }

    // Ensure all realized cards are visible after layout fixes / 布局修复后确保所有已实现卡片可见
    // Loading may set Opacity=0; if Loaded doesn't fire (re-attach timing), cards stay invisible
    // / Loading可能设Opacity=0;若Loaded未触发(re-attach时序),卡片保持不可见
    private void EnsureCardsVisible()
    {
        if (_isLoadingOverlayActive) return;
        int count = VisualTreeHelper.GetChildrenCount(ChangelogRepeater);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is FrameworkElement fe && fe.Opacity < 1)
                PlaySlideInAnimation(fe, CardSlideDistance, SlideInAnimMs);
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

    // Fade in element with opacity + upward float (CubicEase) / 淡入并向上浮入(CubicEase缓动)
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
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(yAnim, tt);
        Storyboard.SetTargetProperty(yAnim, "Y");
        sb.Children.Add(yAnim);
        sb.Begin();
    }

    // Fade out element then collapse (CubicEase EaseIn) / 淡出元素后折叠(CubicEase缓入)
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
