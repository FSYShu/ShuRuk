using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using ShuRuk.Runtime.Services.Changelog;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Version info column width matches XAML ColumnDefinition / 版本信息列宽度与XAML ColumnDefinition一致
    private const double VersionColumnWidth = 100.0;
    // Max sticky overlays to keep before cleanup / 清理前保留的最大sticky覆盖层数量
    private const int MaxStickyOverlays = 20;

    // Per-version sticky overlays in Canvas outside ScrollViewer
    // / 每版本sticky覆盖层在ScrollViewer外的Canvas中
    // Each overlay is a ContentControl using StickyOverlayTemplate
    // / 每个覆盖层是使用StickyOverlayTemplate的ContentControl
    // Canvas is sibling of ScrollViewer, so overlays are not clipped by ScrollViewer
    // / Canvas与ScrollViewer同级,覆盖层不受ScrollViewer裁剪
    private readonly Dictionary<int, ContentControl> _stickyOverlays = new();

    // Update sticky overlay layers: create/update/hide per-version overlays
    // / 更新sticky覆盖层:创建/更新/隐藏每版本覆盖层
    // Called on scroll: positions overlays in Canvas to simulate per-version sticky
    // / 滚动时调用:在Canvas中定位覆盖层模拟每版本sticky
    // When overlay is active, hide original version panel in card to avoid double display
    // / 覆盖层激活时隐藏卡片内原始版本信息列避免双重显示
    private void UpdateStickyVersionLayers()
    {
        int count = VisualTreeHelper.GetChildrenCount(ChangelogRepeater);
        int newStickyIdx = -1;
        var activeIndices = new HashSet<int>();

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

                // Get item index for this card / 获取此卡片的条目索引
                int itemIdx = -1;
                try { itemIdx = ChangelogRepeater.GetElementIndex(card); } catch { }
                if (itemIdx < 0 || itemIdx >= _displayItems.Count) continue;

                // Find original version info panel in card / 查找卡片内原始版本信息列
                var versionPanel = FindVersionInfoPanel(card);

                // Card partially scrolled past top: show sticky overlay
                // / 卡片部分滚出顶部:显示sticky覆盖层
                if (cardTop < -FirstVisibleTolerance && cardBottom > FirstVisibleTolerance)
                {
                    activeIndices.Add(itemIdx);
                    if (newStickyIdx == -1) newStickyIdx = itemIdx;

                    // Hide original version panel to avoid double display
                    // / 隐藏原始版本信息列避免双重显示
                    if (versionPanel is not null && versionPanel.Visibility != Visibility.Collapsed)
                        versionPanel.Visibility = Visibility.Collapsed;

                    // Get or create overlay / 获取或创建覆盖层
                    if (!_stickyOverlays.TryGetValue(itemIdx, out var overlay))
                    {
                        overlay = new ContentControl
                        {
                            ContentTemplate = (DataTemplate)Resources["StickyOverlayTemplate"],
                            Content = _displayItems[itemIdx],
                        };
                        Canvas.SetLeft(overlay, 0);
                        StickyLayerContainer.Children.Add(overlay);
                        _stickyOverlays[itemIdx] = overlay;
                    }

                    // Ensure overlay is measured / 确保覆盖层已测量
                    double overlayHeight = overlay.ActualHeight;
                    if (overlayHeight <= 0)
                    {
                        overlay.Measure(new Size(VersionColumnWidth, double.PositiveInfinity));
                        overlay.Arrange(new Rect(0, 0, VersionColumnWidth, overlay.DesiredSize.Height));
                        overlayHeight = overlay.DesiredSize.Height;
                    }

                    // Push-up logic: pin to top, or push up when card bottom approaches
                    // / 推升逻辑:钉在顶部,或卡片底部接近时推升
                    double canvasTop;
                    if (cardBottom >= overlayHeight)
                    {
                        canvasTop = 0;  // Pin to viewport top / 钉在视口顶部
                    }
                    else
                    {
                        canvasTop = cardBottom - overlayHeight;  // Push up by deficit / 按差值推升
                    }

                    Canvas.SetTop(overlay, canvasTop);
                    overlay.Visibility = Visibility.Visible;

                    ChangelogCacheService.NavLog(
                        $"Sticky: card[{itemIdx}] STICKY cardTop={cardTop:F1} " +
                        $"bottom={cardBottom:F1} overlayH={overlayHeight:F1} top={canvasTop:F1}");
                }
                else
                {
                    // Card fully visible or fully scrolled past: restore original version panel
                    // / 卡片完全可见或完全滚出:恢复原始版本信息列
                    if (versionPanel is not null && versionPanel.Visibility != Visibility.Visible)
                        versionPanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                ChangelogCacheService.NavLog($"Sticky: card[{i}] EXCEPTION: {ex.Message}");
            }
        }

        // Hide overlays for cards no longer partially scrolled / 隐藏不再部分滚出的卡片覆盖层
        foreach (var (idx, overlay) in _stickyOverlays)
        {
            if (!activeIndices.Contains(idx))
            {
                overlay.Visibility = Visibility.Collapsed;
            }
        }

        // Cleanup: remove overlays for items beyond display range / 清理:移除超出显示范围的覆盖层
        if (_stickyOverlays.Count > MaxStickyOverlays)
        {
            var toRemove = _stickyOverlays
                .Where(kv => kv.Key < 0 || kv.Key >= _displayItems.Count)
                .ToList();
            foreach (var (idx, overlay) in toRemove)
            {
                StickyLayerContainer.Children.Remove(overlay);
                _stickyOverlays.Remove(idx);
            }
        }

        _stickyEntryIndex = newStickyIdx;
    }

    // Clear all sticky overlays (e.g. on data change) / 清除所有sticky覆盖层(如数据变化时)
    private void ClearStickyOverlays()
    {
        foreach (var overlay in _stickyOverlays.Values)
        {
            StickyLayerContainer.Children.Remove(overlay);
        }
        _stickyOverlays.Clear();
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
}
