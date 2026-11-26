using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using ShuRuk.Runtime.Controls;
using ShuRuk.Runtime.Models;


namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Version info column width matches XAML ColumnDefinition / 版本信息列宽度与XAML ColumnDefinition一致
    private const double VersionColumnWidth = 100.0;
    private const int MaxStickyOverlays = 20;

    // Per-version sticky overlays in Canvas outside ScrollViewer
    // / 每版本sticky覆盖层在ScrollViewer外的Canvas中
    private readonly Dictionary<int, ContentControl> _stickyOverlays = new();
    // Cached version info panel per card index / 每卡片版本信息面板缓存
    private readonly Dictionary<int, FrameworkElement> _versionPanelCache = new();
    // Reusable set to avoid per-frame GC allocation / 可重用集合避免每帧GC分配
    private readonly HashSet<int> _activeIndices = new();
    // "History versions" section overlay: shown when scrolling through history cards
    // / "历史版本"区域覆盖层:滚动history卡片时显示
    private ContentControl? _historySectionOverlay;
    // Index of the first history card / 第一个history卡片的索引
    private int _firstHistoryIndex = -1;
    // Last sticky overlay index for MarqueeText scroll state transfer
    // / 上次sticky覆盖层索引,用于跑马灯滚动状态传递
    private int _lastStickyIdx = -1;
    // Cached overlay height (all overlays use same template, height is identical)
    // / 缓存覆盖层高度(所有覆盖层用相同模板,高度一致)
    private double _cachedOverlayHeight;
    // Re-entrancy guard for UpdateStickyVersionLayers to prevent layout cycles
    // / UpdateStickyVersionLayers重入保护防止布局循环
    private bool _isUpdatingStickyLayers;

    // Lightweight sticky position update for intermediate scroll frames
    // / 中间滚动帧的轻量sticky位置更新
    private void UpdateStickyPositionLight()
    {
        // Only trigger full update when expanded card is virtualized (not realized).
        // If the card is realized but scrolled out of view, keep it expanded —
        // the user may scroll back to see it.
        // / 仅当展开卡片被虚拟化(未实现)时触发完整更新。
        // 卡片已实现但滚出视口时保持展开——用户可能滚回查看。
        if (_expandedHistoryEntry is not null && !_isExpandingHistory)
        {
            int expandedIdx = _displayItems.IndexOf(_expandedHistoryEntry);
            if (expandedIdx >= 0 && expandedIdx != _stickyEntryIndex)
            {
                bool expandedRealized = false;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(ChangelogRepeater); i++)
                {
                    if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is not FrameworkElement fe) continue;
                    try
                    {
                        int idx = ChangelogRepeater.GetElementIndex(fe);
                        if (idx == expandedIdx)
                        {
                            expandedRealized = true;
                            break;
                        }
                    }
                    catch { }
                }
                if (!expandedRealized)
                {
                    UpdateStickyVersionLayers();
                    return;
                }
            }
        }

        if (_stickyEntryIndex < 0 || !_stickyOverlays.TryGetValue(_stickyEntryIndex, out var overlay))
        {
            UpdateStickyVersionLayers();
            return;
        }

        FrameworkElement? card = null;
        try
        {
            card = ChangelogRepeater.GetOrCreateElement(_stickyEntryIndex) as FrameworkElement;
        }
        catch { }
        if (card is null) { UpdateStickyVersionLayers(); return; }

        try
        {
            var transform = card.TransformToVisual(ChangelogScrollViewer);
            var pos = transform.TransformPoint(new Point(0, 0));
            double cardTop = pos.Y;
            double cardBottom = cardTop + card.ActualHeight;

            if (cardTop >= -FirstVisibleTolerance || cardBottom <= FirstVisibleTolerance)
            {
                UpdateStickyVersionLayers();
                return;
            }

            double overlayHeight = overlay.ActualHeight;
            if (overlayHeight <= 0) { UpdateStickyVersionLayers(); return; }

        // Compute history clamp bottom: when an expanded card exists, use the bottom
        // of the card above it; otherwise use last visible history card bottom.
        // This value is shared by per-card overlay and history section overlay.
        // / 计算历史触底值:有展开卡片时用展开卡片上方卡片底部,
        // 否则用最后一个可见历史卡片底部。此值由per-card覆盖层和历史区域覆盖层共用。
        double historyClampBottom = double.NaN;
        if (_expandedHistoryEntry is not null)
        {
            int expandedIdx = _displayItems.IndexOf(_expandedHistoryEntry);
            if (expandedIdx > 0)
            {
                int childCount = VisualTreeHelper.GetChildrenCount(ChangelogRepeater);
                for (int i = 0; i < childCount; i++)
                {
                    if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is not FrameworkElement aboveCard) continue;
                    try
                    {
                        int aboveIdx = ChangelogRepeater.GetElementIndex(aboveCard);
                        if (aboveIdx == expandedIdx - 1)
                        {
                            var aboveT = aboveCard.TransformToVisual(ChangelogScrollViewer);
                            var aboveP = aboveT.TransformPoint(new Point(0, 0));
                            historyClampBottom = aboveP.Y + aboveCard.ActualHeight;
                            break;
                        }
                    }
                    catch { }
                }
                if (double.IsNaN(historyClampBottom))
                {
                    try
                    {
                        if (ChangelogRepeater.GetOrCreateElement(expandedIdx) is FrameworkElement expCard)
                        {
                            var expT = expCard.TransformToVisual(ChangelogScrollViewer);
                            var expP = expT.TransformPoint(new Point(0, 0));
                            historyClampBottom = expP.Y - CardSpacing;
                        }
                    }
                    catch { }
                }
            }
        }
        if (double.IsNaN(historyClampBottom))
        {
            double regionBottom = double.NaN;
            int childCount = VisualTreeHelper.GetChildrenCount(ChangelogRepeater);
            for (int i = childCount - 1; i >= 0; i--)
            {
                if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is not FrameworkElement lastCard) continue;
                try
                {
                    int lastIdx = ChangelogRepeater.GetElementIndex(lastCard);
                    if (lastIdx < 0 || lastIdx >= _displayItems.Count) continue;
                    if (_displayItems[lastIdx].CardMode != CardMode.HistoryVersion) continue;
                    var lastT = lastCard.TransformToVisual(ChangelogScrollViewer);
                    var lastP = lastT.TransformPoint(new Point(0, 0));
                    regionBottom = lastP.Y + lastCard.ActualHeight;
                    break;
                }
                catch { }
            }
            historyClampBottom = !double.IsNaN(regionBottom)
                ? regionBottom
                : ChangelogScrollViewer.ScrollableHeight
                    + ChangelogScrollViewer.ViewportHeight - ChangelogScrollViewer.VerticalOffset;
        }

        // Position per-card overlay: collapsed history cards bottom-clamp at
        // historyClampBottom when an expanded card exists, instead of always
        // pinning to top. This makes the marquee move up with the page when
        // scrolling toward the expanded card.
        // / 定位per-card覆盖层:折叠历史卡片在有展开卡片时触底到historyClampBottom,
        // 而非始终固定顶部。使跑马灯向展开卡片滚动时随页面上移。
        double canvasTop;
        bool isCollapsedHistory = _stickyEntryIndex >= 0 && _stickyEntryIndex < _displayItems.Count
            && _displayItems[_stickyEntryIndex].CardMode == CardMode.HistoryVersion
            && !_displayItems[_stickyEntryIndex].IsExpanded;
        if (isCollapsedHistory && _expandedHistoryEntry is not null && !double.IsNaN(historyClampBottom))
            canvasTop = Math.Min(0, historyClampBottom - overlayHeight);
        else if (isCollapsedHistory)
            canvasTop = 0;
        else
            canvasTop = cardBottom >= overlayHeight ? 0 : cardBottom - overlayHeight;
        Canvas.SetTop(overlay, canvasTop);

        // Update history section overlay position using shared historyClampBottom
        // / 用共用的historyClampBottom更新历史区域覆盖层位置
        if (_historySectionOverlay is not null && _historySectionOverlay.Visibility == Visibility.Visible)
        {
            double histOverlayHeight = _historySectionOverlay.ActualHeight;
            if (histOverlayHeight > 0)
            {
                double histCanvasTop = Math.Min(0, historyClampBottom - histOverlayHeight);
                Canvas.SetTop(_historySectionOverlay, histCanvasTop);
            }
        }

        }
        catch
        {
            UpdateStickyVersionLayers();
        }
    }

    // Full sticky overlay update: create/update/hide per-version overlays
    // / 完整sticky覆盖层更新:创建/更新/隐藏每版本覆盖层
    private void UpdateStickyVersionLayers()
    {
        // Re-entrancy guard: prevent layout cycle when Measure/Arrange or
        // SizeChanged triggers another UpdateStickyVersionLayers call
        // / 重入保护:防止Measure/Arrange或SizeChanged触发另一次UpdateStickyVersionLayers
        if (_isUpdatingStickyLayers) return;
        _isUpdatingStickyLayers = true;
        try
        {
        UpdateStickyVersionLayersCore();
        }
        finally
        {
            _isUpdatingStickyLayers = false;
        }
    }

    private void UpdateStickyVersionLayersCore()
    {
        int count = VisualTreeHelper.GetChildrenCount(ChangelogRepeater);
        int newStickyIdx = -1;
        _activeIndices.Clear();

        // Determine first history card index / 确定第一个history卡片索引
        _firstHistoryIndex = -1;
        for (int i = 0; i < _displayItems.Count; i++)
        {
            if (_displayItems[i].CardMode == CardMode.HistoryVersion)
            {
                _firstHistoryIndex = i;
                break;
            }
        }

        bool anyHistoryCardVisible = false;
        double lastHistoryCardBottom = double.NaN;
        // Top position of expanded history card, for bottom-clamp of history overlay
        // / 展开history卡片顶部位置,用于历史覆盖层触底
        double expandedHistoryCardTop = double.NaN;
        // Bottom of the card directly above the expanded history card
        // / 展开history卡片正上方卡片的底部位置
        double aboveExpandedCardBottom = double.NaN;

        // Whether the expanded history card's element is still realized (in visual tree)
        // / 展开的 history 卡片元素是否仍已实现(在可视树中)
        bool expandedHistoryCardRealized = false;
        // Map of card index to card bottom, for reliable aboveExpandedCardBottom lookup
        // / 卡片索引到卡片底部的映射,用于可靠查找aboveExpandedCardBottom
        var cardBottomMap = new Dictionary<int, double>();
        int expandedHistoryIdx = -1;
        // First visible card for scroll position restoration after auto-collapse
        // / 第一个可见卡片(用于自动收起后恢复滚动位置)
        int firstVisibleRestoreIdx = -1;
        double firstVisibleRestoreTop = 0;

        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is not FrameworkElement card) continue;

            try
            {
                var transform = card.TransformToVisual(ChangelogScrollViewer);
                var pos = transform.TransformPoint(new Point(0, 0));
                double cardTop = pos.Y;
                double cardHeight = card.ActualHeight;
                double cardBottom = cardTop + cardHeight;

                int itemIdx = -1;
                try { itemIdx = ChangelogRepeater.GetElementIndex(card); } catch { }
                if (itemIdx < 0 || itemIdx >= _displayItems.Count) continue;

                cardBottomMap[itemIdx] = cardBottom;

                // Record first visible card for scroll restoration after auto-collapse
                // / 记录第一个可见卡片用于自动收起后恢复滚动位置
                if (firstVisibleRestoreIdx == -1 && cardTop >= -FirstVisibleTolerance && cardBottom > 0)
                {
                    firstVisibleRestoreIdx = itemIdx;
                    firstVisibleRestoreTop = cardTop;
                }

                var entry = _displayItems[itemIdx];

                // Track expanded history card even if not visible in viewport.
                // expandedHistoryCardRealized = element exists in visual tree (not virtualized).
                // / 追踪展开的 history 卡片(即使不在视口内)。
                // expandedHistoryCardRealized = 元素在可视树中(未被虚拟化)。
                if (entry.CardMode == CardMode.HistoryVersion && entry.IsExpanded)
                {
                    expandedHistoryIdx = itemIdx;
                    expandedHistoryCardRealized = true;
                    expandedHistoryCardTop = cardTop;
                }

                // Track if any history card is visible / 追踪是否有history卡片可见
                if (entry.CardMode == CardMode.HistoryVersion && cardBottom > 0 && cardTop < ChangelogScrollViewer.ViewportHeight)
                {
                    anyHistoryCardVisible = true;
                    lastHistoryCardBottom = cardBottom;
                }

                // Validate cached version panel: if card was virtualized and re-created,
                // the cached reference points to the old (recycled) element — re-find it.
                // / 验证缓存的版本面板:卡片虚拟化回收后缓存引用失效,需重新查找
                FrameworkElement? versionPanel = null;
                if (_versionPanelCache.TryGetValue(itemIdx, out var cachedPanel)
                    && cachedPanel is not null
                    && VisualTreeHelper.GetParent(cachedPanel) == card)
                {
                    versionPanel = cachedPanel;
                }
                if (versionPanel is null)
                {
                    versionPanel = FindVersionInfoPanel(card);
                    if (versionPanel is not null) _versionPanelCache[itemIdx] = versionPanel;
                }

                // Card partially scrolled past top: show sticky overlay
                // / 卡片部分滚出顶部:显示sticky覆盖层
                if (cardTop < -FirstVisibleTolerance && cardBottom > FirstVisibleTolerance)
                {
                    _activeIndices.Add(itemIdx);
                    if (newStickyIdx == -1) newStickyIdx = itemIdx;

                    // Hide original version panel to avoid double display
                    // / 隐藏原始版本信息列避免双重显示
                    if (versionPanel is not null && versionPanel.Visibility != Visibility.Collapsed)
                        versionPanel.Visibility = Visibility.Collapsed;

                    // Get or create overlay / 获取或创建覆盖层
                    if (!_stickyOverlays.TryGetValue(itemIdx, out var overlay))
                    {
                        // Use "history versions" template for collapsed history cards,
                        // / 折叠history卡片用"历史版本"模板,
                        // standard template for featured or expanded history cards
                        // / featured或展开history卡片用标准模板
                        var templateKey = (entry.CardMode == CardMode.HistoryVersion && !entry.IsExpanded)
                            ? "StickyHistoryOverlayTemplate" : "StickyOverlayTemplate";
                        overlay = new ContentControl
                        {
                            ContentTemplate = (DataTemplate)Resources[templateKey],
                            Content = entry,
                        };
                        Canvas.SetLeft(overlay, 0);
                        StickyLayerContainer.Children.Add(overlay);
                        _stickyOverlays[itemIdx] = overlay;
                    }
                    else
                    {
                        // Update template if expand state changed / 展开状态变化时更新模板
                        var desiredKey = (entry.CardMode == CardMode.HistoryVersion && !entry.IsExpanded)
                            ? "StickyHistoryOverlayTemplate" : "StickyOverlayTemplate";
                        var currentKey = overlay.ContentTemplate == (DataTemplate)Resources["StickyHistoryOverlayTemplate"]
                            ? "StickyHistoryOverlayTemplate" : "StickyOverlayTemplate";
                        if (desiredKey != currentKey)
                        {
                            // Get scroll progress from old overlay for seamless transition
                            // / 从旧覆盖层获取滚动进度实现无缝切换
                            double oldProgress = GetOverlayMarqueeProgress(overlay);

                            StickyLayerContainer.Children.Remove(overlay);
                            _stickyOverlays.Remove(itemIdx);
                            overlay = new ContentControl
                            {
                                ContentTemplate = (DataTemplate)Resources[desiredKey],
                                Content = entry,
                                Opacity = 0,
                            };
                            Canvas.SetLeft(overlay, 0);
                            StickyLayerContainer.Children.Add(overlay);
                            _stickyOverlays[itemIdx] = overlay;

                            // Transfer scroll progress: if old MarqueeText had progress,
                            // restart new overlay's MarqueeText from same progress
                            // / 传递滚动进度:旧跑马灯有进度时,新覆盖层从相同进度重启
                            if (oldProgress > 0)
                            {
                                ScheduleSkipPause(overlay, oldProgress);
                            }

                            // Fade in new overlay for smooth template transition
                            // / 淡入新覆盖层实现模板切换过渡
                            var fadeAnim = new DoubleAnimation
                            {
                                From = 0,
                                To = 1,
                                Duration = TimeSpan.FromMilliseconds(300),
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                            };
                            Storyboard.SetTarget(fadeAnim, overlay);
                            Storyboard.SetTargetProperty(fadeAnim, "Opacity");
                            var fadeSb = new Storyboard();
                            fadeSb.Children.Add(fadeAnim);
                            fadeSb.Begin();
                        }
                    }

                    double overlayHeight = overlay.ActualHeight;
                    if (overlayHeight <= 0)
                    {
                        // Use cached height instead of manual Measure/Arrange which
                        // can cause LayoutCycleException during scroll events
                        // / 用缓存高度替代手动Measure/Arrange,后者在滚动事件中会导致LayoutCycleException
                        overlayHeight = _cachedOverlayHeight;
                    }
                    else if (_cachedOverlayHeight <= 0)
                    {
                        _cachedOverlayHeight = overlayHeight;
                    }

                    double canvasTop;
                    // Collapsed history cards: skip position in first pass.
                    // Position is set in the second pass (bottom-clamp logic) to avoid
                    // double-set: first pass → 0, second pass → clamp value, which causes
                    // the overlay to jump back and forth (position ping-pong).
                    // / 折叠history卡片:第一遍跳过位置设置。
                    // 位置在第二遍(触底逻辑)统一设置,避免双重设置导致来回跳。
                    if (entry.CardMode == CardMode.HistoryVersion && !entry.IsExpanded)
                    {
                        // Position deferred to second pass / 位置延迟到第二遍设置
                    }
                    else if (cardBottom >= overlayHeight)
                    {
                        canvasTop = 0;
                        AnimateOverlayTop(overlay, canvasTop);
                    }
                    else
                    {
                        canvasTop = cardBottom - overlayHeight;
                        AnimateOverlayTop(overlay, canvasTop);
                    }
                    overlay.Visibility = Visibility.Visible;
                }
                else
                {
                    if (versionPanel is not null && versionPanel.Visibility != Visibility.Visible)
                        versionPanel.Visibility = Visibility.Visible;
                }

            }
            catch { }
        }

        // Lookup aboveExpandedCardBottom from map (more reliable than GetOrCreateElement)
        // / 从映射中查找aboveExpandedCardBottom(比GetOrCreateElement更可靠)
        if (expandedHistoryIdx > 0 && cardBottomMap.TryGetValue(expandedHistoryIdx - 1, out aboveExpandedCardBottom))
        {
            // aboveExpandedCardBottom set from map / 已从映射中获取
        }
        else if (!double.IsNaN(expandedHistoryCardTop))
        {
            // Above card not realized: approximate with expanded card top minus spacing
            // / 上方卡片未实现:用展开卡片顶部减间距近似
            aboveExpandedCardBottom = expandedHistoryCardTop - CardSpacing;
        }

        // Second pass: set position for ALL collapsed history card overlays.
        // When an expanded card exists, bottom-clamp at aboveExpandedCardBottom
        // so the marquee moves up with the page instead of staying pinned at top.
        // When no expanded card exists, pin to top (canvasTop=0).
        // This is the ONLY place collapsed history card positions are set, avoiding
        // the double-set (first pass → 0, second pass → clamp) that caused ping-pong.
        // / 第二遍:设置所有折叠历史卡片覆盖层的位置。
        // 有展开卡片时触底到aboveExpandedCardBottom,使跑马灯随页面上移;
        // 无展开卡片时固定顶部(canvasTop=0)。
        // 这是折叠历史卡片位置的唯一设置点,避免双重设置导致来回跳。
        foreach (var idx in _activeIndices)
        {
            if (idx < 0 || idx >= _displayItems.Count) continue;
            var activeEntry = _displayItems[idx];
            if (activeEntry.CardMode != CardMode.HistoryVersion || activeEntry.IsExpanded) continue;
            if (!_stickyOverlays.TryGetValue(idx, out var activeOverlay)) continue;
            double activeOverlayHeight = activeOverlay.ActualHeight;
            if (activeOverlayHeight <= 0) continue;

            double clampedTop;
            if (_expandedHistoryEntry is not null && !double.IsNaN(aboveExpandedCardBottom))
                clampedTop = Math.Min(0, aboveExpandedCardBottom - activeOverlayHeight);
            else
                clampedTop = 0;

            AnimateOverlayTop(activeOverlay, clampedTop);
        }

        // Auto-collapse expanded history card only when virtualized (element recycled).
        // If the card is realized but scrolled out of view, keep it expanded —
        // the user may scroll back to see it.
        // / 展开 history 卡片仅在被虚拟化(元素回收)时自动收起。
        // 卡片已实现但滚出视口时保持展开——用户可能滚回查看。
        // Skip during expand animation to prevent misfire / 展开动画期间跳过防止误触发
        if (_expandedHistoryEntry is not null && !expandedHistoryCardRealized && !_isExpandingHistory)
        {
            double currentOffset = ChangelogScrollViewer.VerticalOffset;
            var entryToCollapse = _expandedHistoryEntry;
            entryToCollapse.IsExpanded = false;
            _expandedHistoryEntry = null;
            if (ReferenceEquals(_lastExpandedEntry, entryToCollapse))
            {
                _lastExpandedEntry = null;
                _lastExpandedIndex = -1;
            }
            SetFloatingCollapseVisible(false);
            // Defer UpdateLayout + scroll restoration to next frame to avoid
            // LayoutCycleException from synchronous layout during scroll event
            // / 将UpdateLayout+滚动恢复延迟到下一帧,避免滚动事件中同步布局导致LayoutCycleException
            int restoreIdx = firstVisibleRestoreIdx;
            double restoreTop = firstVisibleRestoreTop;
            double savedOffset = currentOffset;
            DispatcherQueue.TryEnqueue(() =>
            {
                ChangelogRepeater.UpdateLayout();
                if (restoreIdx >= 0)
                {
                    try
                    {
                        if (ChangelogRepeater.GetOrCreateElement(restoreIdx) is FrameworkElement restoreFe)
                        {
                            var restoreT = restoreFe.TransformToVisual(ChangelogScrollViewer);
                            var restoreP = restoreT.TransformPoint(new Point(0, 0));
                            double newTop = restoreP.Y;
                            if (Math.Abs(newTop - restoreTop) > 1)
                            {
                                double targetOffset = ChangelogScrollViewer.VerticalOffset + newTop - restoreTop;
                                targetOffset = Math.Clamp(targetOffset, 0, ChangelogScrollViewer.ScrollableHeight);
                                ChangelogScrollViewer.ChangeView(null, targetOffset, null, true);
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    double newScrollable = ChangelogScrollViewer.ScrollableHeight;
                    double clampedOffset = Math.Clamp(savedOffset, 0, newScrollable);
                    if (Math.Abs(clampedOffset - ChangelogScrollViewer.VerticalOffset) > 1)
                        ChangelogScrollViewer.ChangeView(null, clampedOffset, null, true);
                }
            });
        }

        // Collapse any expanded history card not in viewport (beyond _expandedHistoryEntry)
        // / 收起所有不在视口内的已展开 history 卡片(超出 _expandedHistoryEntry 范围)
        // Skip during expand animation to prevent misfire / 展开动画期间跳过防止误触发
        if (!_isExpandingHistory)
        {
            for (int i = 0; i < _displayItems.Count; i++)
            {
                var item = _displayItems[i];
                if (item.CardMode != CardMode.HistoryVersion || !item.IsExpanded) continue;
                if (ReferenceEquals(item, _expandedHistoryEntry)) continue;
                bool inViewport = false;
                for (int j = 0; j < count; j++)
                {
                    if (VisualTreeHelper.GetChild(ChangelogRepeater, j) is not FrameworkElement checkCard) continue;
                    try
                    {
                        int checkIdx = ChangelogRepeater.GetElementIndex(checkCard);
                        if (checkIdx == i)
                        {
                            var checkT = checkCard.TransformToVisual(ChangelogScrollViewer);
                            var checkP = checkT.TransformPoint(new Point(0, 0));
                            if (checkP.Y + checkCard.ActualHeight > 0 && checkP.Y < ChangelogScrollViewer.ViewportHeight)
                                inViewport = true;
                            break;
                        }
                    }
                    catch { }
                }
                if (!inViewport)
                    item.IsExpanded = false;
            }
        }

        // Show "history versions" section overlay when scrolling through history cards
        // and no specific card is pinned (expanded history card has its own overlay)
        // / 滚动 history 卡片时显示"历史版本"区域覆盖层
        // 没有特定卡片被钉住时(展开的 history 卡片有自己的覆盖层)
        if (anyHistoryCardVisible && _firstHistoryIndex >= 0 && newStickyIdx == -1)
        {
            EnsureHistorySectionOverlay();
            if (_historySectionOverlay is not null)
            {
                double overlayHeight = _historySectionOverlay.ActualHeight;
                if (overlayHeight <= 0)
                {
                    // Use cached height instead of manual Measure/Arrange
                    // / 用缓存高度替代手动Measure/Arrange
                    overlayHeight = _cachedOverlayHeight;
                }
                else if (_cachedOverlayHeight <= 0)
                {
                    _cachedOverlayHeight = overlayHeight;
                }
                // Bottom-clamp: when a history card is expanded, the history overlay
                // should clamp to the bottom of the card ABOVE the expanded card,
                // not the expanded card itself (which has its own sticky overlay).
                // When no card is expanded, use last visible history card's bottom.
                // / 触底:当 history 卡片展开时,历史覆盖层应触到展开卡片上方卡片的底部,
                // 而非展开卡片本身(展开卡片有自己的 sticky 覆盖层)。
                // 无展开卡片时,用最后一个可见 history 卡片底部。
                double regionBottom = double.NaN;
                if (!double.IsNaN(aboveExpandedCardBottom))
                {
                    regionBottom = aboveExpandedCardBottom;
                }
                else
                {
                    for (int i = count - 1; i >= 0; i--)
                    {
                        if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is not FrameworkElement lastCard) continue;
                        try
                        {
                            int lastIdx = ChangelogRepeater.GetElementIndex(lastCard);
                            if (lastIdx < 0 || lastIdx >= _displayItems.Count) continue;
                            if (_displayItems[lastIdx].CardMode != CardMode.HistoryVersion) continue;
                            var lastT = lastCard.TransformToVisual(ChangelogScrollViewer);
                            var lastP = lastT.TransformPoint(new Point(0, 0));
                            regionBottom = lastP.Y + lastCard.ActualHeight;
                            break;
                        }
                        catch { }
                    }
                }
                double clampBottom = !double.IsNaN(regionBottom)
                    ? regionBottom
                    : ChangelogScrollViewer.ScrollableHeight
                        + ChangelogScrollViewer.ViewportHeight - ChangelogScrollViewer.VerticalOffset;
                double canvasTop = Math.Min(0, clampBottom - overlayHeight);
                // Use SetTop (no animation): _historySectionOverlay position is maintained
                // by UpdateStickyPositionLight during intermediate scroll frames, so
                // animating here would cause a jump from the light-updated position
                // / 用SetTop(无动画):_historySectionOverlay位置由UpdateStickyPositionLight在中间帧维护,
                // 此处动画会从轻量更新的位置跳变
                Canvas.SetTop(_historySectionOverlay, canvasTop);
                if (_historySectionOverlay.Visibility != Visibility.Visible)
                    _historySectionOverlay.Visibility = Visibility.Visible;

                // Hide version panels of visible history cards to avoid double display
                // / 隐藏可见 history 卡片的版本面板避免双重显示
                for (int i = 0; i < count; i++)
                {
                    if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is not FrameworkElement card2) continue;
                    try
                    {
                        int idx2 = ChangelogRepeater.GetElementIndex(card2);
                        if (idx2 < 0 || idx2 >= _displayItems.Count) continue;
                        if (_displayItems[idx2].CardMode != CardMode.HistoryVersion) continue;
                        if (_displayItems[idx2].IsExpanded) continue;
                        if (!_versionPanelCache.TryGetValue(idx2, out var vp2)) continue;
                        var t2 = card2.TransformToVisual(ChangelogScrollViewer);
                        var p2 = t2.TransformPoint(new Point(0, 0));
                        if (p2.Y + card2.ActualHeight <= 0 || p2.Y >= ChangelogScrollViewer.ViewportHeight) continue;
                        if (vp2.Visibility != Visibility.Collapsed)
                            vp2.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                }
            }
        }
        else if (_historySectionOverlay is not null)
        {
            _historySectionOverlay.Visibility = Visibility.Collapsed;
        }

        // Hide overlays for cards no longer partially scrolled / 隐藏不再部分滚出的卡片覆盖层
        foreach (var (idx, overlay) in _stickyOverlays)
        {
            if (!_activeIndices.Contains(idx))
                overlay.Visibility = Visibility.Collapsed;
        }

        // Transfer MarqueeText scroll progress when sticky overlay switches to a different card
        // / sticky覆盖层切换到不同卡片时传递跑马灯滚动进度
        if (newStickyIdx >= 0 && newStickyIdx != _lastStickyIdx && _lastStickyIdx >= 0)
        {
            if (_stickyOverlays.TryGetValue(_lastStickyIdx, out var oldOverlay))
            {
                double oldProgress = GetOverlayMarqueeProgress(oldOverlay);
                if (oldProgress > 0 && _stickyOverlays.TryGetValue(newStickyIdx, out var newOverlay))
                {
                    ScheduleSkipPause(newOverlay, oldProgress);
                }
            }
        }
        _lastStickyIdx = newStickyIdx;

        // Cleanup: remove overlays for items beyond display range / 清理:移除超出显示范围的覆盖层
        if (_stickyOverlays.Count > MaxStickyOverlays)
        {
            List<int>? keysToRemove = null;
            foreach (var idx in _stickyOverlays.Keys)
            {
                if (idx < 0 || idx >= _displayItems.Count)
                {
                    keysToRemove ??= new List<int>();
                    keysToRemove.Add(idx);
                }
            }
            if (keysToRemove is not null)
            {
                foreach (var idx in keysToRemove)
                {
                    StickyLayerContainer.Children.Remove(_stickyOverlays[idx]);
                    _stickyOverlays.Remove(idx);
                }
            }
        }

        _stickyEntryIndex = newStickyIdx;
    }

    // Create the "history versions" section overlay if not yet created
    // / 创建"历史版本"区域覆盖层(如尚未创建)
    private void EnsureHistorySectionOverlay()
    {
        if (_historySectionOverlay is not null) return;
        // Use first history entry as data context for the "History versions" label
        // / 用第一个 history 条目作为"历史版本"标签的数据上下文
        if (_firstHistoryIndex < 0 || _firstHistoryIndex >= _displayItems.Count) return;
        _historySectionOverlay = new ContentControl
        {
            ContentTemplate = (DataTemplate)Resources["StickyHistoryOverlayTemplate"],
            Content = _displayItems[_firstHistoryIndex],
        };
        Canvas.SetLeft(_historySectionOverlay, 0);
        StickyLayerContainer.Children.Add(_historySectionOverlay);
    }

    // Clear all sticky overlays (e.g. on data change) / 清除所有sticky覆盖层(如数据变化时)
    private void ClearStickyOverlays()
    {
        foreach (var overlay in _stickyOverlays.Values)
            StickyLayerContainer.Children.Remove(overlay);
        _stickyOverlays.Clear();
        _versionPanelCache.Clear();
        if (_historySectionOverlay is not null)
        {
            StickyLayerContainer.Children.Remove(_historySectionOverlay);
            _historySectionOverlay = null;
        }
    }

    // Find version info StackPanel in card template / 查找卡片模板中的版本信息StackPanel
    private static FrameworkElement? FindVersionInfoPanel(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is StackPanel sp) return sp;
        }
        return null;
    }

    // Track active Canvas.Top animation per overlay to stop before starting new one
    // / 追踪每个overlay的活跃Canvas.Top动画,开始新动画前停止旧动画
    private readonly Dictionary<ContentControl, Storyboard> _overlayTopAnims = new();

    // Animate Canvas.Top for smooth overlay position transitions
    // / 动画化Canvas.Top实现覆盖层位置平滑过渡
    private void AnimateOverlayTop(ContentControl overlay, double targetTop)
    {
        double currentTop = Canvas.GetTop(overlay);
        if (Math.Abs(currentTop - targetTop) < 0.5)
        {
            Canvas.SetTop(overlay, targetTop);
            StopOverlayTopAnim(overlay);
            return;
        }
        // Stop previous animation before starting new one to avoid conflicts
        // / 开始新动画前停止旧动画避免冲突
        StopOverlayTopAnim(overlay);

        var topAnim = new DoubleAnimation
        {
            From = currentTop,
            To = targetTop,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(topAnim, overlay);
        Storyboard.SetTargetProperty(topAnim, "(Canvas.Top)");
        var sb = new Storyboard();
        sb.Children.Add(topAnim);
        sb.Completed += (_, _) => _overlayTopAnims.Remove(overlay);
        _overlayTopAnims[overlay] = sb;
        sb.Begin();
    }

    // Stop active Canvas.Top animation for an overlay
    // / 停止overlay的活跃Canvas.Top动画
    private void StopOverlayTopAnim(ContentControl overlay)
    {
        if (_overlayTopAnims.TryGetValue(overlay, out var oldSb))
        {
            oldSb.Stop();
            _overlayTopAnims.Remove(overlay);
        }
    }

    // Get overlay's MarqueeText scroll progress (0 = at start, 1 = fully scrolled)
    // / 获取覆盖层跑马灯滚动进度(0=起始,1=完全滚出)
    private static double GetOverlayMarqueeProgress(ContentControl overlay)
    {
        var marquee = FindMarqueeInOverlay(overlay);
        return marquee?.GetScrollProgress() ?? 0;
    }

    // Check if overlay's MarqueeText is actively scrolling (past initial pause)
    // / 检查覆盖层的跑马灯是否正在滚动(已过初始停顿)
    private static bool IsOverlayMarqueeScrolling(ContentControl overlay)
    {
        var marquee = FindMarqueeInOverlay(overlay);
        return marquee is not null && marquee.IsScrolling;
    }

    // Find MarqueeText control inside overlay's visual tree
    // / 在覆盖层可视树中查找MarqueeText控件
    private static MarqueeText? FindMarqueeInOverlay(DependencyObject root)
    {
        if (root is MarqueeText mt) return mt;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindMarqueeInOverlay(child);
            if (result is not null) return result;
        }
        return null;
    }

    // Schedule restart-from-progress on overlay's MarqueeText after it loads
    // / 覆盖层加载后调度从进度重启跑马灯
    private void ScheduleSkipPause(ContentControl overlay, double progress, int retryCount = 0)
    {
        // Force template application so MarqueeText exists in visual tree
        // / 强制应用模板使MarqueeText存在于可视树中
        overlay.ApplyTemplate();

        var marquee = FindMarqueeInOverlay(overlay);
        if (marquee is not null)
        {
            marquee.RestartFromProgress(progress);
            return;
        }

        // MarqueeText not yet in visual tree: retry on next frame (up to 5 attempts).
        // ContentControl template application may take several frames to complete.
        // / MarqueeText尚未在可视树中:下一帧重试(最多5次)。
        // ContentControl模板应用可能需要数帧才能完成。
        if (retryCount < 5)
        {
            DispatcherQueue.TryEnqueue(() => ScheduleSkipPause(overlay, progress, retryCount + 1));
        }
    }
}
