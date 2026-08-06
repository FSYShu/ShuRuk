using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ShuRuk.App.Helpers;

/// <summary>
/// Shared UI tree traversal helpers / 共享的UI树遍历辅助方法。
/// Consolidates the visual & logical tree enumeration previously duplicated
/// 整合原先重复分布在 MainWindow 与 LocalizationHelper 中的可视/逻辑树枚举逻辑。
/// </summary>
public static class UiTreeHelper
{
    /// <summary>
    /// Enumerate all descendants of <paramref name="parent"/> in the visual tree
    /// 枚举可视树中 <paramref name="parent"/> 的所有后代元素。
    /// </summary>
    public static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var grandChild in EnumerateVisualChildren<T>(child)) yield return grandChild;
        }
    }

    /// <summary>
    /// Enumerate all descendants of <paramref name="parent"/> in the logical tree
    /// 枚举逻辑树中 <paramref name="parent"/> 的所有后代元素。
    /// Walks Panel / Border / ContentControl / ScrollViewer / SelectorBar / ItemsControl
    /// 覆盖 Panel、Border、ContentControl、ScrollViewer、SelectorBar、ItemsControl 等容器。
    /// </summary>
    public static IEnumerable<T> EnumerateLogicalChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        // Panel (Grid, StackPanel, etc.) - check first since it's most common
        // Panel（Grid、StackPanel 等）优先检查，因其最常见
        if (parent is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is T t) yield return t;
                foreach (var grandChild in EnumerateLogicalChildren<T>(child)) yield return grandChild;
            }
        }
        else if (parent is Border border && border.Child is DependencyObject borderChild)
        {
            if (borderChild is T t) yield return t;
            foreach (var grandChild in EnumerateLogicalChildren<T>(borderChild)) yield return grandChild;
        }
        else if (parent is ContentControl cc && cc.Content is DependencyObject content)
        {
            if (content is T t) yield return t;
            foreach (var grandChild in EnumerateLogicalChildren<T>(content)) yield return grandChild;
        }
        else if (parent is ScrollViewer sv && sv.Content is DependencyObject svContent)
        {
            if (svContent is T t) yield return t;
            foreach (var grandChild in EnumerateLogicalChildren<T>(svContent)) yield return grandChild;
        }
        else if (parent is SelectorBar sb)
        {
            foreach (var item in sb.Items)
            {
                if (item is T t) yield return t;
                foreach (var grandChild in EnumerateLogicalChildren<T>(item)) yield return grandChild;
            }
        }
        else if (parent is ItemsControl ic)
        {
            foreach (var item in ic.Items)
            {
                if (item is DependencyObject dObj)
                {
                    if (dObj is T t) yield return t;
                    foreach (var grandChild in EnumerateLogicalChildren<T>(dObj)) yield return grandChild;
                }
            }
        }
    }
}