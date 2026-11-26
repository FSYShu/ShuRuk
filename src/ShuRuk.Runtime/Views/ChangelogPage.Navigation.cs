using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.Runtime.Models;

using ShuRuk.Runtime.ViewModels;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Navigation re-entrancy guard / 导航重入保护
    private bool _isNavigating;
    private bool _isScrollAnimating;
    private double _navTargetOffset;
    private const double NavSettleTolerance = 1.5;
    private bool _uiUpdateQueued;
    private int _stickyEntryIndex = -1;
    // Pending expand after navigation completes (collapse-first navigation)
    // / 导航完成后待展开的条目(先收起再滚动导航)
    private ChangelogEntry? _pendingExpandEntry;
    private int _pendingExpandIndex = -1;

    private const double FirstVisibleTolerance = 2.0;
    private const double CardSpacing = 10.0;
    private const int NavSettleDelayMs = 300;
    private DispatcherTimer? _navSettleTimer;

    private const int PrevPollIntervalMs = 50;
    private const int PrevPollMaxCount = 8;

    private const int RenderDebounceMs = 80;
    private const int RenderMaxWaitMs = 500;

    // Scroll handler: sticky updates + navigation animation detection
    // / 滚动处理:sticky更新+导航动画检测
    private void ChangelogScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (_isNavigating && _isScrollAnimating && !e.IsIntermediate &&
            Math.Abs(sv.VerticalOffset - _navTargetOffset) <= NavSettleTolerance)
        {

            _isScrollAnimating = false;
            _navSettleTimer?.Stop();
            DispatcherQueue.TryEnqueue(FinishNavigation);
        }
        if (!_isNavigating)
        {
            if (e.IsIntermediate)
                UpdateStickyPositionLight();
            else
                UpdateStickyVersionLayers();

            if (!_uiUpdateQueued)
            {
                _uiUpdateQueued = true;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _uiUpdateQueued = false;
                    UpdateNavButtons();
                    UpdateCollapseButton();
                });
            }
        }
    }

    // Quick nav: scroll to previous version entry / 快速跳转:滚动到上一个版本条目
    private void PrevVersionButton_Click(object sender, RoutedEventArgs e)
    {
        int idx = FindFirstVisibleIndex();

        if (idx > 0) ScrollToItem(idx - 1);
    }

    // Quick nav: scroll to next version entry / 快速跳转:滚动到下一个版本条目
    private void NextVersionButton_Click(object sender, RoutedEventArgs e)
    {
        int idx = FindFirstVisibleIndex();

        if (idx >= 0 && idx < _displayItems.Count - 1) ScrollToItem(idx + 1);
    }

    // Find first visible item index, full range search with hint
    // / 查找第一个可见项索引,带提示的全范围搜索
    private int FindFirstVisibleIndex()
    {
        if (_displayItems.Count == 0) return -1;

        int hint = Math.Clamp(_stickyEntryIndex, 0, _displayItems.Count - 1);
        int visibleIdx = -1;

        for (int i = hint; i < _displayItems.Count; i++)
        {
            double? top = GetItemTop(i);
            if (top is null) continue;
            if (top.Value >= -FirstVisibleTolerance)
            {
                visibleIdx = i;
                break;
            }
        }

        if (visibleIdx == -1)
        {
            for (int i = hint - 1; i >= 0; i--)
            {
                double? top = GetItemTop(i);
                if (top is null) continue;
                if (top.Value >= -FirstVisibleTolerance)
                {
                    visibleIdx = i;
                    break;
                }
            }
        }

        if (visibleIdx == -1) return -1;

        for (int i = visibleIdx - 1; i >= 0; i--)
        {
            double? top = GetItemTop(i);
            if (top is null) continue;
            if (top.Value >= -FirstVisibleTolerance) visibleIdx = i;
            else break;
        }

        return visibleIdx;
    }

    // Get item's top position relative to ScrollViewer viewport / 获取item相对ScrollViewer视口的顶部位置
    private double? GetItemTop(int index)
    {
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(index);
            if (el is not FrameworkElement fe) return null;
            if (fe.ActualHeight == 0) return null;
            var t = fe.TransformToVisual(ChangelogScrollViewer);
            var pos = t.TransformPoint(new Windows.Foundation.Point(0, 0));
            return pos.Y;
        }
        catch { return null; }
    }

    // Scroll so previous card's bottom aligns with viewport top (first card → page top)
    // / 滚动使上一卡片底部对齐视口顶部(首卡片→页面顶部)
    // For history cards: collapse current expanded first, scroll to target, then expand after settle
    // / history 卡片:先收起当前展开的,滚动到目标,稳定后再展开
    private void ScrollToItem(int index)
    {
        if (index < 0 || index >= _displayItems.Count)
        {
            return;
        }
        if (_isNavigating)
        {
            return;
        }

        var targetEntry = _displayItems[index];

        // Collapse current expanded history card first (before scrolling) for accurate position
        // / 先收起当前展开的 history 卡片(滚动前)以确保位置准确
        _pendingExpandEntry = null;
        _pendingExpandIndex = -1;
        bool collapsedHistoryForNav = false;

        if (targetEntry.CardMode == CardMode.HistoryVersion && !targetEntry.IsExpanded)
        {
            // Collapse ALL expanded history cards (mutual exclusivity)
            // / 收起所有已展开的 history 卡片(互斥)
            bool anyCollapsed = false;
            foreach (var item in _displayItems)
            {
                if (item.CardMode == CardMode.HistoryVersion && item.IsExpanded && !ReferenceEquals(item, targetEntry))
                {
                    item.IsExpanded = false;
                    anyCollapsed = true;
                }
            }
            if (anyCollapsed) collapsedHistoryForNav = true;
            // Don't expand target yet — expand after scroll completes
            // / 暂不展开目标——滚动完成后再展开
            _pendingExpandEntry = targetEntry;
            _pendingExpandIndex = index;
        }

        // Navigation to featured card: collapse all history cards
        // / 导航到 featured 卡片:收起所有 history 卡片
        if (targetEntry.CardMode == CardMode.Featured && _expandedHistoryEntry is not null)
        {
            CollapseAllOtherCards();
            collapsedHistoryForNav = true;
        }

        _isNavigating = true;
        _isScrollAnimating = true;

        PrevVersionButton.IsEnabled = false;
        NextVersionButton.IsEnabled = false;

        DispatcherQueueHandler scrollCalculation = () =>
        {
            // Let layout stabilize after collapsing / 收起后让布局稳定
            ChangelogRepeater.UpdateLayout();

            if (index == 0) { ExecuteScrollTo(0); return; }

            int firstVisible = FindFirstVisibleIndex();
            if (firstVisible < 0)
            {
                CalculateAndScrollTo(index);
                return;
            }

            double? firstTop = GetItemTop(firstVisible);
            if (firstTop is null)
            {
                CalculateAndScrollTo(index);
                return;
            }

            double curOffset = ChangelogScrollViewer.VerticalOffset;
            double firstAbs = curOffset + firstTop.Value;

            if (index > firstVisible)
            {
                double accum = firstAbs;
                for (int i = firstVisible; i < index; i++)
                {
                    double h = GetRenderedCardHeight(i);
                    accum += h + CardSpacing;
                }
                double targetOffset = accum - CardSpacing;
                ExecuteScrollTo(targetOffset);
            }
            else if (index < firstVisible)
            {
                PrevPollAndScroll(firstVisible, index, firstAbs, firstTop.Value);
            }
            else
            {
                FinishNavigation();
            }
        };

        // Wait for Expander collapse animation to complete before measuring positions
        // / 等待Expander收起动画完成后再测量位置
        if (collapsedHistoryForNav)
        {
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            delayTimer.Tick += (_, _) => { delayTimer.Stop(); scrollCalculation(); };
            delayTimer.Start();
        }
        else
        {
            DispatcherQueue.TryEnqueue(scrollCalculation);
        }
    }

    // Cached height of collapsed history cards (fixed: Expander header + padding + margin)
    // / 折叠 history 卡片缓存高度(固定:Expander header + padding + margin)
    private double _collapsedOtherCardHeight;

    private double GetRenderedCardHeight(int index)
    {
        // Collapsed history cards have fixed height: use cached value to avoid expensive GetOrCreateElement
        // / 折叠 history 卡片高度固定:使用缓存值避免昂贵的 GetOrCreateElement
        if (index >= 0 && index < _displayItems.Count
            && _displayItems[index].CardMode == CardMode.HistoryVersion && !_displayItems[index].IsExpanded
            && _collapsedOtherCardHeight > 0)
            return _collapsedOtherCardHeight;

        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(index);
            if (el is FrameworkElement fe) return fe.ActualHeight;
        }
        catch { }
        return 0;
    }

    private double GetCachedCardHeight(int index)
    {
        if (_cardHeightCache.TryGetValue(index, out double h) && h > 0) return h;
        return GetRenderedCardHeight(index);
    }

    private void PrevPollAndScroll(int firstVisible, int target, double firstAbs, double firstTop)
    {
        FrameworkElement? targetEl = null;
        try { targetEl = ChangelogRepeater.GetOrCreateElement(target) as FrameworkElement; } catch { }
        if (targetEl is null)
        {
            CalculateAndScrollTo(target);
            return;
        }

        ChangelogRepeater.UpdateLayout();
        double prevHeight = targetEl.ActualHeight;
        var pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PrevPollIntervalMs) };
        int stableCount = 0;
        int pollCount = 0;

        pollTimer.Tick += (_, _) =>
        {
            pollCount++;
            double curHeight = targetEl.ActualHeight;

            if (curHeight <= 0) { stableCount = 0; }
            else if (Math.Abs(curHeight - prevHeight) < 1.0) { stableCount++; }
            else { stableCount = 0; }
            prevHeight = curHeight;

            if (stableCount >= 2 || pollCount >= PrevPollMaxCount)
            {
                pollTimer.Stop();
                if (!_isNavigating) return;

                double accum = firstAbs;
                for (int i = firstVisible - 1; i >= target; i--)
                {
                    double h = (i == target) ? curHeight : GetCachedCardHeight(i);
                    accum -= h + CardSpacing;
                }
                double targetOffset = accum - CardSpacing;
                ExecuteScrollTo(targetOffset);
            }
        };
        pollTimer.Start();
    }

    private void WaitForRenderStableAndScroll(FrameworkElement element, int index)
    {
        var debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenderDebounceMs) };
        var maxTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenderMaxWaitMs) };
        bool completed = false;

        SizeChangedEventHandler handler = null!;
        handler = (s, e) =>
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        };

        debounceTimer.Tick += (_, _) =>
        {
            if (completed) return;
            completed = true;
            debounceTimer.Stop();
            maxTimer.Stop();
            element.SizeChanged -= handler;
            CalculateAndScrollTo(index);
        };

        maxTimer.Tick += (_, _) =>
        {
            if (completed) return;
            completed = true;
            maxTimer.Stop();
            debounceTimer.Stop();
            element.SizeChanged -= handler;
            CalculateAndScrollTo(index);
        };

        element.SizeChanged += handler;
        debounceTimer.Start();
        maxTimer.Start();
    }

    private void CalculateAndScrollTo(int index)
    {
        if (!_isNavigating) return;
        ChangelogRepeater.UpdateLayout();
        double? targetTop = GetItemTop(index);
        if (targetTop is null)
        {
            FinishNavigation();
            return;
        }
        double targetOffset = ChangelogScrollViewer.VerticalOffset + targetTop.Value - CardSpacing;
        ExecuteScrollTo(targetOffset);
    }

    private void ExecuteScrollTo(double targetOffset)
    {
        targetOffset = Math.Clamp(targetOffset, 0, ChangelogScrollViewer.ScrollableHeight);
        _navTargetOffset = targetOffset;
        ChangelogScrollViewer.ChangeView(null, targetOffset, null, false);

        if (Math.Abs(ChangelogScrollViewer.VerticalOffset - targetOffset) <= NavSettleTolerance)
        {
            _isScrollAnimating = false;
            FinishNavigation();
            return;
        }

        _navSettleTimer?.Stop();
        _navSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(NavSettleDelayMs) };
        _navSettleTimer.Tick += OnNavSettleTimerTick;
        _navSettleTimer.Start();
    }

    private void OnNavSettleTimerTick(object? sender, object e)
    {
        _navSettleTimer?.Stop();
        _isScrollAnimating = false;
        FinishNavigation();
    }

    private void FinishNavigation()
    {
        if (!_isNavigating) return;
        _isNavigating = false;
        _isScrollAnimating = false;

        // Expand pending history card after scroll settles / 滚动稳定后展开待展开的 history 卡片
        if (_pendingExpandEntry is not null)
        {
            _pendingExpandEntry.IsExpanded = true;
            _expandedHistoryEntry = _pendingExpandEntry;
            _lastExpandedEntry = _pendingExpandEntry;
            _lastExpandedIndex = _pendingExpandIndex;
            _pendingExpandEntry = null;
            _pendingExpandIndex = -1;
        }

        UpdateStickyVersionLayers();
        UpdateNavButtons();
    }

    // Enable/disable nav buttons based on current position / 根据当前位置启用/禁用导航按钮
    private void UpdateNavButtons()
    {
        if (_displayItems.Count == 0)
        {
            PrevVersionButton.IsEnabled = false;
            NextVersionButton.IsEnabled = false;
            return;
        }
        int idx = _stickyEntryIndex >= 0 ? _stickyEntryIndex : FindFirstVisibleIndex();
        PrevVersionButton.IsEnabled = idx > 0;
        NextVersionButton.IsEnabled = idx >= 0 && idx < _displayItems.Count - 1;
    }
}
