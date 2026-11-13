using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ShuRuk.Runtime.Services.Markdown;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Configure theme before first render to avoid default-color flash on table heading
    // / 首次渲染前配置主题避免表头默认色闪烁
    private void MarkdownTextBlock_Loading(FrameworkElement sender, object e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
            MarkdownStyler.OnLoading(mtb, IsDark, DispatcherQueue);
    }

    // Subscribe to theme changes after load / 加载后订阅主题变化
    private void MarkdownTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
        {
            mtb.ActualThemeChanged += OnMtbActualThemeChanged;
            MarkdownStyler.OnLoaded(mtb, IsDark, DispatcherQueue);
            mtb.DataContextChanged += OnMtbDataContextChanged;
            // Listen for Text DP changes: container recycle re-binds Text, triggers markdown re-render
            // / 监听Text DP变化:容器回收重新绑定Text,触发markdown重渲染
            // More reliable than SizeChanged (fires even if rendered size is unchanged)
            // / 比SizeChanged更可靠(渲染尺寸不变也触发)
            if (_mtbTextProperty is not null)
                mtb.RegisterPropertyChangedCallback(_mtbTextProperty, OnMtbTextChanged);
        }
    }

    // On MarkdownTextBlock theme change: re-apply theme brushes + force re-render
    // / MarkdownTextBlock主题变化时:重新应用主题画刷+强制重渲染
    private void OnMtbActualThemeChanged(FrameworkElement sender, object e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
            MarkdownStyler.OnThemeChanged(mtb, IsDark, DispatcherQueue);
    }

    // On container recycling: re-apply styles for new data / 容器回收时:为新数据重新应用样式
    private void OnMtbDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
            MarkdownStyler.OnDataContextChanged(mtb, IsDark, DispatcherQueue);
    }

    // On Text DP changed (container recycle re-binds Text): debounced style re-application
    // / Text DP变化(容器回收重新绑定Text)时:防抖重新应用样式
    // Primary mechanism for catching recycled MTBs: Text change is the root cause of re-render
    // / 捕获回收MTB的主要机制:Text变化是重渲染的根本原因
    private void OnMtbTextChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
            MarkdownStyler.OnSizeChanged(mtb, IsDark, DispatcherQueue);
    }

    // On MarkdownTextBlock size change: debounced style re-application / 尺寸变化时:防抖重新应用样式
    private void MarkdownTextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
            MarkdownStyler.OnSizeChanged(mtb, IsDark, DispatcherQueue);
    }

    // Find ProgressRing sibling in same Grid / 在同一Grid中查找ProgressRing
    private static ProgressRing? FindLoadingRing(Image img)
    {
        if (img.Parent is not Grid grid) return null;
        foreach (var child in grid.Children)
            if (child is ProgressRing ring) return ring;
        return null;
    }

    // Image loaded: MaxWidth handled by data-layer binding / 图片加载完成:MaxWidth由数据层绑定处理
    private void Image_ImageOpened(object sender, RoutedEventArgs e)
    {
    }

    // Image failed: no action (LoadingVisibility handled by data layer)
    // / 图片加载失败:无需操作(LoadingVisibility由数据层处理)
    private void Image_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
    }
}
