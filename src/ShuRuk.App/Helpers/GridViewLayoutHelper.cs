using Microsoft.UI.Xaml.Controls;

namespace ShuRuk.App.Helpers;

/// <summary>
/// Computes MaximumRowsOrColumns for centered ItemsWrapGrid / 为居中的 ItemsWrapGrid 计算最大列数。
/// WinUI 3 gives infinite available width when HorizontalAlignment=Center,
/// so column count must be clamped in code based on the ScrollViewer viewport.
/// WinUI 3 在 HorizontalAlignment=Center 时测量阶段收到无限宽度,
/// 故需在代码中依据 ScrollViewer 视口宽度限制列数。
/// </summary>
internal static class GridViewLayoutHelper
{
    /// <summary>
    /// Recompute max columns for the given GridView's ItemsWrapGrid / 重新计算指定 GridView 内 ItemsWrapGrid 的最大列数。
    /// Preserves scrollbar-induced margin differences by using the ScrollViewer viewport width.
    /// 使用 ScrollViewer 视口宽度,自然保留滚动条造成的两侧边距差值。
    /// </summary>
    public static void UpdateMaxColumns(GridView gridView, double itemWidth)
    {
        if (gridView.ItemsPanelRoot is not ItemsWrapGrid itemsWrapGrid) return;

        // Locate inner ScrollViewer to read real viewport width / 查找内部 ScrollViewer 以读取真实视口宽度
        var scrollViewer = FindScrollViewer(gridView);
        if (scrollViewer is null) return;

        var viewportWidth = scrollViewer.ViewportWidth;
        if (viewportWidth <= 0) return;

        // Available width minus GridView horizontal padding / 可用宽度减去 GridView 水平内边距
        var padding = gridView.Padding;
        var availableWidth = viewportWidth - padding.Left - padding.Right;
        if (availableWidth <= 0) return;

        var maxColumns = (int)Math.Floor(availableWidth / itemWidth);
        if (maxColumns < 1) maxColumns = 1;

        itemsWrapGrid.MaximumRowsOrColumns = maxColumns;
    }

    // Walk descendants to find the first ScrollViewer / 遍历后代查找第一个 ScrollViewer
    private static ScrollViewer? FindScrollViewer(Microsoft.UI.Xaml.DependencyObject root)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }
}