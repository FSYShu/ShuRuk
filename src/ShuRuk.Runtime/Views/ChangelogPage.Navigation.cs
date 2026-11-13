using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.Runtime.Models;
using ShuRuk.Runtime.Services.Changelog;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Navigation re-entrancy guard / 导航重入保护
    private bool _isNavigating;
    // Scroll animation in progress (programmatic nav) / 编程导航滚动动画进行中
    private bool _isScrollAnimating;
    // Target vertical offset for current nav animation / 当前导航动画目标垂直偏移
    private double _navTargetOffset;
    // Offset tolerance to treat scroll as settled (px) / 视为滚动稳定的偏移容差(像素)
    private const double NavSettleTolerance = 1.5;
    // UI update queued to next frame (dedup) / 已排队到下一帧的UI更新(去重)
    private bool _uiUpdateQueued;
    // Track current sticky entry index / 追踪当前sticky条目索引
    private int _stickyEntryIndex = -1;

    // Tolerance for treating item as visible (px), absorbs sub-pixel drift
    // / 视为可见的容差(像素),吸收亚像素漂移
    private const double FirstVisibleTolerance = 2.0;
    // Card bottom margin in XAML (spacing between cards) / XAML中卡片底部边距(卡片间距)
    private const double CardSpacing = 10.0;
    // Navigation settle fallback timer delay (ms) / 导航稳定兜底定时器延迟(毫秒)
    private const int NavSettleDelayMs = 300;
    // Active navigation settle timer / 当前导航稳定定时器
    private DispatcherTimer? _navSettleTimer;

    // Poll interval for PREV height stability check / PREV高度稳定性轮询间隔
    private const int PrevPollIntervalMs = 50;
    // Max polls before proceeding with current height / 最大轮询次数(超时则用当前高度)
    private const int PrevPollMaxCount = 8;

    // Debounce delay after last SizeChanged before treating render as stable
    // / 最后一次SizeChanged后的防抖延迟(视为渲染稳定)
    private const int RenderDebounceMs = 80;
    // Max wait for render stability before proceeding with current height
    // / 渲染稳定最大等待时间(超时则用当前高度)
    private const int RenderMaxWaitMs = 500;

    // Load more when scrolled near bottom / 滚动接近底部时加载更多
    private void ChangelogScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        // Detect scroll animation end: non-intermediate AND offset reached target
        // / 检测滚动动画结束:非中间帧且偏移已达目标
        if (_isNavigating && _isScrollAnimating && !e.IsIntermediate &&
            Math.Abs(sv.VerticalOffset - _navTargetOffset) <= NavSettleTolerance)
        {
            ChangelogCacheService.NavLog(
                $"ViewChanged: ANIM END offset={sv.VerticalOffset:F1} " +
                $"target={_navTargetOffset:F1} -> defer FinishNavigation");
            _isScrollAnimating = false;
            _navSettleTimer?.Stop();
            // Defer to next frame: FinishNavigation touches layout,
            // / 延迟到下一帧:FinishNavigation触发布局
            // calling synchronously in ViewChanged callback causes LayoutCycleException
            // / 在ViewChanged回调中同步调用会触发LayoutCycleException
            DispatcherQueue.TryEnqueue(FinishNavigation);
        }
        // Skip all updates during programmatic navigation
        // / 编程导航期间跳过所有更新
        if (!_isNavigating)
        {
            // Sync sticky update: positions Canvas overlays, no ScrollViewer layout cycle
            // / 同步更新sticky:定位Canvas覆盖层,无ScrollViewer布局循环
            UpdateStickyVersionLayers();

            if (!_uiUpdateQueued)
            {
                // Defer nav/collapse updates: they touch Visibility/UpdateLayout,
                // / 延迟nav/collapse更新:它们修改Visibility/UpdateLayout
                // running synchronously in the layout-driven ViewChanged callback causes LayoutCycleException
                // / 在布局驱动的ViewChanged回调中同步执行会触发LayoutCycleException
                _uiUpdateQueued = true;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _uiUpdateQueued = false;
                    UpdateNavButtons();
                    UpdateCollapseButton();
                });
            }
        }
        // Pre-load threshold: 1200px ahead (increased from 600px) with BatchSize=2 for smoother scrolling
        // / 预加载阈值:提前1200px(从600px增加)配合BatchSize=2更流畅滚动
        if (!_isLoadingMore &&
            sv.VerticalOffset >= sv.ScrollableHeight - 1200 &&
            _displayItems.Count < _allEntries.Count)
        {
            LoadMore();
        }
    }

    // Quick nav: scroll to previous version entry / 快速跳转:滚动到上一个版本条目
    private void PrevVersionButton_Click(object sender, RoutedEventArgs e)
    {
        int idx = FindFirstVisibleIndex();
        ChangelogCacheService.NavLog(
            $"PREV click: firstVisible={idx} count={_displayItems.Count} " +
            $"offset={ChangelogScrollViewer.VerticalOffset:F1}");
        if (idx > 0) ScrollToItem(idx - 1);
    }

    // Quick nav: scroll to next version entry / 快速跳转:滚动到下一个版本条目
    private void NextVersionButton_Click(object sender, RoutedEventArgs e)
    {
        int idx = FindFirstVisibleIndex();
        ChangelogCacheService.NavLog(
            $"NEXT click: firstVisible={idx} count={_displayItems.Count} " +
            $"offset={ChangelogScrollViewer.VerticalOffset:F1}");
        if (idx >= 0 && idx < _displayItems.Count - 1) ScrollToItem(idx + 1);
    }

    // Find first visible item index, full range search with hint
    // / 查找第一个可见项索引,带提示的全范围搜索
    private int FindFirstVisibleIndex()
    {
        if (_displayItems.Count == 0) return -1;

        // Hint: start near last known sticky position / 提示:从已知sticky位置附近开始
        int hint = Math.Clamp(_stickyEntryIndex, 0, _displayItems.Count - 1);
        int visibleIdx = -1;

        // Phase 1a: search forward from hint / 第一阶段a:从提示位置向前搜索
        for (int i = hint; i < _displayItems.Count; i++)
        {
            double? top = GetItemTop(i);
            if (top is null) continue;
            if (top.Value >= -FirstVisibleTolerance)
            {
                visibleIdx = i;
                ChangelogCacheService.NavLog(
                    $"  FFV P1a: hint={hint} i={i} top={top.Value:F1} -> visibleIdx={i}");
                break;
            }
        }

        // Phase 1b: search backward if not found forward / 第一阶段b:向前未找到则向后搜索
        if (visibleIdx == -1)
        {
            for (int i = hint - 1; i >= 0; i--)
            {
                double? top = GetItemTop(i);
                if (top is null) continue;
                if (top.Value >= -FirstVisibleTolerance)
                {
                    visibleIdx = i;
                    ChangelogCacheService.NavLog(
                        $"  FFV P1b: hint={hint} i={i} top={top.Value:F1} -> visibleIdx={i}");
                    break;
                }
            }
        }

        if (visibleIdx == -1) { ChangelogCacheService.NavLog($"  FFV: hint={hint} -> -1 (none found)"); return -1; }

        // Phase 2: scan backwards to find earliest visible item / 第二阶段:向前扫描找最早可见项
        // Top positions are monotonically increasing, stop at first negative
        // / 顶部位置单调递增,遇到负值即停止
        for (int i = visibleIdx - 1; i >= 0; i--)
        {
            double? top = GetItemTop(i);
            if (top is null) continue;
            if (top.Value >= -FirstVisibleTolerance) visibleIdx = i;
            else
            {
                ChangelogCacheService.NavLog(
                    $"  FFV P2: stop at i={i} top={top.Value:F1} final visibleIdx={visibleIdx}");
                break;
            }
        }

        ChangelogCacheService.NavLog($"  FFV result: {visibleIdx} (hint={hint} stickyEntry={_stickyEntryIndex})");
        return visibleIdx;
    }

    // Get item's top position relative to ScrollViewer viewport / 获取item相对ScrollViewer视口的顶部位置
    private double? GetItemTop(int index)
    {
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(index);
            if (el is not FrameworkElement fe) return null;
            // Skip unlaid-out elements to avoid TransformToVisual throwing
            // / 跳过未布局元素避免TransformToVisual抛异常
            if (fe.ActualHeight == 0) return null;
            var t = fe.TransformToVisual(ChangelogScrollViewer);
            var pos = t.TransformPoint(new Windows.Foundation.Point(0, 0));
            return pos.Y;
        }
        catch { return null; }
    }

    // Scroll so previous card's bottom aligns with viewport top (first card → page top)
    // / 滚动使上一卡片底部对齐视口顶部(首卡片→页面顶部)
    private void ScrollToItem(int index)
    {
        if (index < 0 || index >= _displayItems.Count)
        {
            ChangelogCacheService.NavLog(
                $"ScrollToItem SKIP: index={index} count={_displayItems.Count}");
            return;
        }
        if (_isNavigating)
        {
            ChangelogCacheService.NavLog(
                $"ScrollToItem BLOCKED by _isNavigating: index={index}");
            return;
        }

        ChangelogCacheService.NavLog(
            $"ScrollToItem START: index={index} " +
            $"curOffset={ChangelogScrollViewer.VerticalOffset:F1} " +
            $"scrollable={ChangelogScrollViewer.ScrollableHeight:F1}");

        // Ensure target item is loaded (sync mode for immediate availability)
        // / 确保目标条目已加载(同步模式立即可用)
        while (_displayItems.Count <= index && _displayItems.Count < _allEntries.Count)
            ViewModel.LoadMore(false);

        _isNavigating = true;
        _isScrollAnimating = true;

        PrevVersionButton.IsEnabled = false;
        NextVersionButton.IsEnabled = false;

        // Calculate target offset from firstVisible (accurate) + accumulated heights
        // / 从firstVisible(准确)累加高度计算目标偏移
        DispatcherQueue.TryEnqueue(() =>
        {
            if (index == 0) { ExecuteScrollTo(0); return; }

            int firstVisible = FindFirstVisibleIndex();
            if (firstVisible < 0)
            {
                ChangelogCacheService.NavLog($"  firstVisible=-1, fallback");
                CalculateAndScrollTo(index);
                return;
            }

            double? firstTop = GetItemTop(firstVisible);
            if (firstTop is null)
            {
                ChangelogCacheService.NavLog($"  firstTop null, fallback");
                CalculateAndScrollTo(index);
                return;
            }

            double curOffset = ChangelogScrollViewer.VerticalOffset;
            double firstAbs = curOffset + firstTop.Value;

            if (index > firstVisible)
            {
                // NEXT: accumulate heights forward from firstVisible / 向下从firstVisible累加高度
                double accum = firstAbs;
                for (int i = firstVisible; i < index; i++)
                {
                    double h = GetRenderedCardHeight(i);
                    accum += h + CardSpacing;
                    ChangelogCacheService.NavLog($"  NEXT accum: i={i} h={h:F1} accum={accum:F1}");
                }
                double targetOffset = accum - CardSpacing;
                ChangelogCacheService.NavLog(
                    $"  NEXT calc: firstVisible={firstVisible} " +
                    $"firstTop={firstTop.Value:F1} " +
                    $"h[firstVisible]={GetRenderedCardHeight(firstVisible):F1} " +
                    $"targetOffset={targetOffset:F1}");
                ExecuteScrollTo(targetOffset);
            }
            else if (index < firstVisible)
            {
                // PREV: poll target card ActualHeight until stable, then calculate
                // / PREV:轮询目标卡片ActualHeight直到稳定再计算
                PrevPollAndScroll(firstVisible, index, firstAbs, firstTop.Value);
            }
            else
            {
                // index == firstVisible, no scroll needed / 目标即当前,无需滚动
                FinishNavigation();
            }
        });
    }

    // Get rendered card height from ActualHeight (accurate for visible cards)
    // / 从ActualHeight获取已渲染卡片高度(可见卡片准确)
    private double GetRenderedCardHeight(int index)
    {
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(index);
            if (el is FrameworkElement fe) return fe.ActualHeight;
        }
        catch { }
        return 0;
    }

    // Get cached card height, fall back to ActualHeight if realized
    // / 获取缓存卡片高度,未缓存则尝试ActualHeight
    private double GetCachedCardHeight(int index)
    {
        if (_cardHeightCache.TryGetValue(index, out double h) && h > 0) return h;
        return GetRenderedCardHeight(index);
    }

    // PREV: poll target card ActualHeight until stable, then scroll
    // / PREV:轮询目标卡片ActualHeight直到稳定再滚动
    private void PrevPollAndScroll(int firstVisible, int target, double firstAbs, double firstTop)
    {
        FrameworkElement? targetEl = null;
        try { targetEl = ChangelogRepeater.GetOrCreateElement(target) as FrameworkElement; } catch { }
        if (targetEl is null)
        {
            ChangelogCacheService.NavLog($"  PREV poll: targetEl null, fallback to GetItemTop");
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
            else
            {
                stableCount = 0;
                ChangelogCacheService.NavLog($"  PREV poll: h {prevHeight:F1} -> {curHeight:F1}");
            }
            prevHeight = curHeight;

            if (stableCount >= 2 || pollCount >= PrevPollMaxCount)
            {
                pollTimer.Stop();
                if (!_isNavigating) return;

                // Calculate from firstVisible + stable target height / 用firstVisible和稳定的target高度计算
                double accum = firstAbs;
                for (int i = firstVisible - 1; i >= target; i--)
                {
                    double h = (i == target) ? curHeight : GetCachedCardHeight(i);
                    accum -= h + CardSpacing;
                }
                double targetOffset = accum - CardSpacing;
                ChangelogCacheService.NavLog(
                    $"  PREV calc (poll): polls={pollCount} stable={stableCount} " +
                    $"h={curHeight:F1} firstTop={firstTop:F1} " +
                    $"targetOffset={targetOffset:F1}");
                ExecuteScrollTo(targetOffset);
            }
        };
        pollTimer.Start();
        ChangelogCacheService.NavLog($"  PREV poll start: target={target} initial h={prevHeight:F1}");
    }

    // Wait for target card's SizeChanged to settle, then scroll / 等待目标卡片SizeChanged稳定后再滚动
    private void WaitForRenderStableAndScroll(FrameworkElement element, int index)
    {
        var debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenderDebounceMs) };
        var maxTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenderMaxWaitMs) };
        bool completed = false;

        SizeChangedEventHandler handler = null!;
        handler = (s, e) =>
        {
            ChangelogCacheService.NavLog($"  SizeChanged: height={e.NewSize.Height:F1} -> reset debounce");
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
            ChangelogCacheService.NavLog($"  render stable (debounce {RenderDebounceMs}ms), proceed");
            CalculateAndScrollTo(index);
        };

        maxTimer.Tick += (_, _) =>
        {
            if (completed) return;
            completed = true;
            maxTimer.Stop();
            debounceTimer.Stop();
            element.SizeChanged -= handler;
            ChangelogCacheService.NavLog($"  render max timeout {RenderMaxWaitMs}ms, proceed");
            CalculateAndScrollTo(index);
        };

        element.SizeChanged += handler;
        debounceTimer.Start();
        maxTimer.Start();
        ChangelogCacheService.NavLog(
            $"  pre-realized index={index}, waiting for SizeChanged " +
            $"debounce ({RenderDebounceMs}ms) or timeout ({RenderMaxWaitMs}ms)");
    }

    // Calculate target offset from current card position and scroll / 从当前卡片位置计算目标偏移并滚动
    private void CalculateAndScrollTo(int index)
    {
        if (!_isNavigating) return;
        ChangelogRepeater.UpdateLayout();
        double? targetTop = GetItemTop(index);
        if (targetTop is null)
        {
            ChangelogCacheService.NavLog($"  targetTop null after wait");
            FinishNavigation();
            return;
        }
        double targetOffset = ChangelogScrollViewer.VerticalOffset + targetTop.Value - CardSpacing;
        ChangelogCacheService.NavLog(
            $"  calc (fallback): targetTop={targetTop.Value:F1} " +
            $"curOffset={ChangelogScrollViewer.VerticalOffset:F1} " +
            $"targetOffset={targetOffset:F1}");
        ExecuteScrollTo(targetOffset);
    }

    // Execute scroll to target offset with animation and fallback / 执行滚动到目标偏移(带动画和兜底)
    private void ExecuteScrollTo(double targetOffset)
    {
        // Clamp to valid scroll range / 限制到有效滚动范围
        targetOffset = Math.Clamp(targetOffset, 0, ChangelogScrollViewer.ScrollableHeight);
        _navTargetOffset = targetOffset;
        ChangelogCacheService.NavLog(
            $"  ChangeView: targetOffset={targetOffset:F1} (clamped) " +
            $"scrollable={ChangelogScrollViewer.ScrollableHeight:F1}");
        // Animated scroll / 带动画滚动
        ChangelogScrollViewer.ChangeView(null, targetOffset, null, false);

        // No-animation case: offset already at target, finish immediately
        // / 无动画情况:偏移已在目标处,立即完成
        if (Math.Abs(ChangelogScrollViewer.VerticalOffset - targetOffset) <= NavSettleTolerance)
        {
            ChangelogCacheService.NavLog($"  no-animation (already at target), FinishNavigation");
            _isScrollAnimating = false;
            FinishNavigation();
            return;
        }

        // Fallback timer in case ViewChanged never fires final frame
        // / 兜底定时器防止ViewChanged未触发最终帧
        _navSettleTimer?.Stop();
        _navSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(NavSettleDelayMs) };
        _navSettleTimer.Tick += OnNavSettleTimerTick;
        _navSettleTimer.Start();

    }

    // Navigation settle timer callback (fallback) / 导航稳定定时器回调(兜底)
    private void OnNavSettleTimerTick(object? sender, object e)
    {
        _navSettleTimer?.Stop();
        _isScrollAnimating = false;
        FinishNavigation();
    }

    // Complete navigation: restore state and update UI (idempotent) / 完成导航:恢复状态并更新UI(幂等)
    private void FinishNavigation()
    {
        if (!_isNavigating) { ChangelogCacheService.NavLog($"FinishNavigation SKIP (not navigating)"); return; }
        double curOffset = ChangelogScrollViewer.VerticalOffset;
        ChangelogCacheService.NavLog(
            $"FinishNavigation: curOffset={curOffset:F1} " +
            $"target={_navTargetOffset:F1} drift={curOffset - _navTargetOffset:F1}");
        _isNavigating = false;
        _isScrollAnimating = false;

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
        int idx = FindFirstVisibleIndex();
        PrevVersionButton.IsEnabled = idx > 0;
        NextVersionButton.IsEnabled = idx >= 0 && idx < _displayItems.Count - 1;
    }
}
