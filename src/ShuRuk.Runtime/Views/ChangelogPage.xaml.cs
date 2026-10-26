using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.Runtime.Abstractions;
using ShuRuk.Runtime.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // All entries and currently displayed subset / 全部条目和当前显示的子集
    private readonly List<ChangelogEntry> _allEntries = [];
    private readonly List<ChangelogEntry> _sourceEntries = [];
    private readonly ObservableCollection<ChangelogEntry> _displayItems = [];
    // Max items per incremental load / 每次增量加载的最大数量
    private const int BatchSize = 20;

    // Localized filter option text / 本地化过滤选项文本
    public string PageTitleText => RuntimeLocalizationHelper.GetString("RuntimeChangelogPageTitle");
    public string FilterAllText => RuntimeLocalizationHelper.GetString("RuntimeChangelogFilterAll");
    public string FilterExcludePrereleaseText => RuntimeLocalizationHelper.GetString("RuntimeChangelogFilterExcludePrerelease");
    public string PrevVersionText => RuntimeLocalizationHelper.GetString("RuntimeChangelogPrevVersion");
    public string NextVersionText => RuntimeLocalizationHelper.GetString("RuntimeChangelogNextVersion");

    public static readonly DependencyProperty EntriesProperty =
        DependencyProperty.Register(nameof(Entries), typeof(IEnumerable<ChangelogEntry>), typeof(ChangelogPage), new PropertyMetadata(null, OnEntriesChanged));

    public IEnumerable<ChangelogEntry> Entries { get => (IEnumerable<ChangelogEntry>)GetValue(EntriesProperty); set => SetValue(EntriesProperty, value); }

    // Empty state title / 空状态标题
    public static readonly DependencyProperty EmptyTitleProperty =
        DependencyProperty.Register(nameof(EmptyTitle), typeof(string), typeof(ChangelogPage), new PropertyMetadata("No changelog"));

    public string EmptyTitle { get => (string)GetValue(EmptyTitleProperty); set => SetValue(EmptyTitleProperty, value); }

    // Empty state description / 空状态描述
    public static readonly DependencyProperty EmptyDescriptionProperty =
        DependencyProperty.Register(nameof(EmptyDescription), typeof(string), typeof(ChangelogPage), new PropertyMetadata("You're up to date."));

    public string EmptyDescription { get => (string)GetValue(EmptyDescriptionProperty); set => SetValue(EmptyDescriptionProperty, value); }

    // Loading state / 加载状态
    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(ChangelogPage), new PropertyMetadata(false, OnIsLoadingChanged));

    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }

    // Loading text / 加载文字
    public static readonly DependencyProperty LoadingTextProperty =
        DependencyProperty.Register(nameof(LoadingText), typeof(string), typeof(ChangelogPage), new PropertyMetadata("", OnLoadingTextChanged));

    public string LoadingText { get => (string)GetValue(LoadingTextProperty); set => SetValue(LoadingTextProperty, value); }

    // Track hidden original version info panel / 追踪已隐藏的原始版本信息列
    private FrameworkElement? _hiddenVersionPanel;

    public ChangelogPage()
    {
        InitializeComponent();
        ChangelogRepeater.ItemsSource = _displayItems;
        StickyVersionText.SizeChanged += StickyVersionText_SizeChanged;
    }

    // Custom marquee: scroll text, pause at start and end / 自定义跑马灯:滚动文本,首尾停顿
    private void VersionText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        if (tb.RenderTransform is not TranslateTransform tt) return;
        tb.SizeChanged += VersionText_SizeChanged;
        // Immediate check in case SizeChanged already fired / 立即检查以防SizeChanged已触发
        UpdateMarquee(tb);
    }

    private void VersionText_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        UpdateMarquee(tb);
    }

    private void UpdateMarquee(TextBlock tb)
    {
        if (tb.RenderTransform is not TranslateTransform tt) return;
        if (string.IsNullOrEmpty(tb.Text)) return;
        // Use ActualWidth, fallback to detached probe / 用ActualWidth,后备为独立探测
        double textWidth = tb.ActualWidth;
        if (textWidth <= 0)
        {
            var probe = new TextBlock { Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight };
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            textWidth = probe.DesiredSize.Width;
        }
        if (textWidth <= 0) return; // Not laid out yet / 尚未布局
        const double containerWidth = 100;

        if (textWidth <= containerWidth)
        {
            // Text fits, stop marquee if running / 文本适配,停止跑马灯
            if (tb.Tag is Storyboard sb)
            {
                sb.Stop();
                tt.X = 0;
                tb.Tag = null;
            }
            return;
        }

        // Text exceeds container, start if not running / 文本超出容器,未运行则启动
        if (tb.Tag is not null) return;
        tb.Tag = StartMarquee(tt, textWidth);
    }

    private static Storyboard StartMarquee(TranslateTransform tt, double textWidth)
    {
        const double containerWidth = 100;
        // Min offset 25 so short text still scrolls visibly / 最小偏移25让短文本也明显滚动
        double offset = Math.Max(textWidth - containerWidth, 30);
        double scrollTime = offset / 30; // 30px per second / 每秒30像素
        double pauseTime = 2;
        double t = 0;

        var anim = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(pauseTime * 2 + scrollTime * 2)
        };

        // Pause at start -> scroll left -> pause at end -> scroll back / 开头停顿→左滚→末尾停顿→滚回
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)) });
        t += pauseTime;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)) });
        t += scrollTime;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = -offset, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)) });
        t += pauseTime;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = -offset, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)) });
        t += scrollTime;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)) });

        Storyboard.SetTarget(anim, tt);
        Storyboard.SetTargetProperty(anim, "X");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
        return sb;
    }

    // Re-entrancy guard for markdown config / markdown配置重入保护
    private static readonly HashSet<CommunityToolkit.WinUI.Controls.MarkdownTextBlock> _configuring = [];

    // Limit images to MarkdownTextBlock width / 限制图片宽度不超过MarkdownTextBlock
    private void MarkdownTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
        {
            mtb.SizeChanged += MarkdownTextBlock_SizeChanged;
            mtb.ActualThemeChanged += (_, _) => ConfigureMarkdownTheme(mtb, forceRender: true);
            ConfigureMarkdownTheme(mtb, forceRender: true);
            // Schedule image container height fix after render / 渲染后修正图片容器高度
            ScheduleImageHeightFix(mtb);
        }
    }

    // Fix image container heights after markdown render / markdown渲染后修正图片容器高度
    // ImageMaxWidth scales image correctly but container keeps original height / ImageMaxWidth正确缩放图片但容器保留原始高度
    private static void ScheduleImageHeightFix(CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
    {
        // Delay until after layout pass / 延迟到布局完成后
        mtb.DispatcherQueue.TryEnqueue(() => FixImageContainerHeights(mtb));
    }

    private static void FixImageContainerHeights(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Image img)
            {
                // Subscribe to ImageOpened if not already tagged / 订阅ImageOpened(未订阅时)
                if (img.Tag is not true)
                {
                    img.Tag = true;
                    img.ImageOpened += Image_ImageOpened;
                    img.ImageFailed += Image_ImageOpened;
                }
                // If already loaded, fix immediately / 已加载则立即修正
                if (img.ActualHeight > 0)
                    FixSingleImageContainer(img);
            }
            FixImageContainerHeights(child);
        }
    }

    // Set parent container Height to Auto so it shrinks to image / 父容器高度设为Auto使其收缩到图片
    private static void Image_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Image img)
        {
            FixSingleImageContainer(img);
            // Force re-measure up to MarkdownTextBlock / 向上强制重新测量到MarkdownTextBlock
            var p = VisualTreeHelper.GetParent(img);
            while (p is not null)
            {
                if (p is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
                {
                    mtb.InvalidateArrange();
                    break;
                }
                p = VisualTreeHelper.GetParent(p);
            }
        }
    }

    private static void FixSingleImageContainer(Image img)
    {
        // Clear Image's own Height so it auto-sizes from MaxWidth / 清除Image自身Height使其按MaxWidth自动计算
        if (!double.IsNaN(img.Height) && img.Height > 0)
            img.Height = double.NaN;
        if (img.MinHeight > 0)
            img.MinHeight = 0;

        // Walk up clearing fixed Height/MinHeight on containers / 向上清除容器的固定Height/MinHeight
        var parent = VisualTreeHelper.GetParent(img);
        while (parent is not null)
        {
            if (parent is FrameworkElement fe)
            {
                if (!double.IsNaN(fe.Height) && fe.Height > 0)
                    fe.Height = double.NaN;
                if (fe.MinHeight > 0)
                    fe.MinHeight = 0;
            }
            // Stop at MarkdownTextBlock boundary / 到MarkdownTextBlock边界停止
            if (parent is CommunityToolkit.WinUI.Controls.MarkdownTextBlock) break;
            parent = VisualTreeHelper.GetParent(parent);
        }
    }

    // Apply theme-aware colors to markdown config / 应用主题感知颜色到markdown配置
    private static void ConfigureMarkdownTheme(CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb, bool forceRender = false)
    {
        // Prevent re-entrant calls from SizeChanged feedback loop / 防止SizeChanged反馈循环重入
        if (_configuring.Contains(mtb)) return;
        _configuring.Add(mtb);
        try
        {
            var themes = mtb.Config.Themes;
            bool isDark = mtb.ActualTheme == ElementTheme.Dark;
            // Inline code foreground follows theme / inline code文字跟随主题
            var codeFg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, isDark ? (byte)0xE4 : (byte)0x1F, isDark ? (byte)0xE4 : (byte)0x1F, isDark ? (byte)0xE4 : (byte)0x1F));
            // Gray background for inline code & table header / 灰色背景
            var codeBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, isDark ? (byte)0x2C : (byte)0xF3, isDark ? (byte)0x2C : (byte)0xF3, isDark ? (byte)0x2C : (byte)0xF3));
            // Link: official dark blue (#0078D4 light, #4CC2FF dark) / 链接官方深蓝
            var linkFg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, isDark ? (byte)0x4C : (byte)0x00, isDark ? (byte)0xC2 : (byte)0x78, isDark ? (byte)0xFF : (byte)0xD4));

            themes.InlineCodeForeground = codeFg;
            themes.InlineCodeBackground = codeBg;
            themes.TableHeadingBackground = codeBg;
            themes.LinkForeground = linkFg;
            // Heading foreground follows theme / 标题前景色跟随主题
            themes.H1Foreground = codeFg;
            themes.H2Foreground = codeFg;
            themes.H3Foreground = codeFg;
            themes.H4Foreground = codeFg;
            themes.H5Foreground = codeFg;
            themes.H6Foreground = codeFg;
            // Image max width keeps aspect ratio / 图片最大宽度保持宽高比
            if (mtb.ActualWidth > 0)
                themes.ImageMaxWidth = mtb.ActualWidth;
            // Assign back in case Themes is a value type / 赋回以防Themes是值类型
            mtb.Config.Themes = themes;
            // Force re-render only when theme changes, not on size change / 仅主题变化时强制重渲染,尺寸变化时不需要
            if (forceRender)
            {
                var md = mtb.Text;
                if (!string.IsNullOrEmpty(md))
                {
                    mtb.Text = string.Empty;
                    mtb.Text = md;
                    // Fix image container heights after re-render / 重渲染后修正图片容器高度
                    ScheduleImageHeightFix(mtb);
                }
            }
        }
        finally
        {
            _configuring.Remove(mtb);
        }
    }

    private void MarkdownTextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb) return;
        if (mtb.ActualWidth <= 0) return;
        // Only update ImageMaxWidth without re-render / 仅更新ImageMaxWidth不重渲染
        var themes = mtb.Config.Themes;
        themes.ImageMaxWidth = mtb.ActualWidth;
        mtb.Config.Themes = themes;
        // Check for newly rendered images / 检查新渲染的图片
        FixImageContainerHeights(mtb);
    }

    // Fetch data on navigation via injected provider / 导航时通过注入的提供者获取数据
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Apply localized empty state text / 应用本地化空状态文字
        EmptyTitle = RuntimeLocalizationHelper.GetString("RuntimeChangelogEmptyTitle");
        EmptyDescription = RuntimeLocalizationHelper.GetString("RuntimeChangelogEmptyDescription");

        // Read navigation parameter / 读取导航参数
        var param = e.Parameter as ChangelogPageParameter;
        var repoUrl = param?.RepoUrl ?? "microsoft/PowerToys";
        var sectionFilter = param?.SectionFilter;

        var provider = RuntimeServices.ChangelogDataProvider;
        if (provider is not null && provider.IsAvailable)
        {
            IsLoading = true;
            LoadingText = RuntimeLocalizationHelper.GetString("RuntimeChangelogLoading");

            try
            {
                var entries = await provider.GetEntriesAsync(repoUrl, sectionFilter);
                IsLoading = false;
                Entries = entries;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ChangelogPage fetch failed: {ex}");
                IsLoading = false;
                Entries = [new ChangelogEntry
                {
                    Version = "Error",
                    Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    Notes = $"Failed to fetch: {ex.Message}"
                }];
            }
        }
        else
        {
            Entries = [];
        }
    }

    private static void OnEntriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChangelogPage page)
        {
            // Store source entries then apply filter / 存储原始条目后应用过滤
            page._sourceEntries.Clear();
            page._sourceEntries.AddRange(page.Entries ?? []);
            page.ApplyFilter();
            // Show filter and play appear animation / 显示过滤按钮并播放出现动画
            if (page._sourceEntries.Count > 0)
            {
                page.FilterBorder.Visibility = Visibility.Visible;
                page.NavButtonsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                page.NavButtonsPanel.Visibility = Visibility.Collapsed;
            }
            page.UpdateNavButtons();
            page.PlayAppearAnimation();
        }
    }

    // Fade-in + slide-up animation for content / 内容淡入+上滑动画
    private void PlayAppearAnimation()
    {
        var opacityAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, ContentPanel);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var slideAnim = new DoubleAnimation
        {
            From = 20,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideAnim, ContentPanel);
        Storyboard.SetTargetProperty(slideAnim, "(UIElement.RenderTransform).(TranslateTransform.Y)");

        var sb = new Storyboard();
        sb.Children.Add(opacityAnim);
        sb.Children.Add(slideAnim);
        sb.Begin();
    }

    // Current filter tag / 当前过滤标签
    private string _filterTag = "all";

    // Filter by tag (exclude pre-release if chosen) / 按标签过滤
    private void ApplyFilter()
    {
        _allEntries.Clear();
        if (_filterTag == "exclude-prerelease")
            _allEntries.AddRange(_sourceEntries.Where(x => !x.IsPrerelease));
        else
            _allEntries.AddRange(_sourceEntries);
        _displayItems.Clear();
        LoadMore();
        UpdateEmptyState();
    }

    private void FilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        _filterTag = item.Tag?.ToString() ?? "all";
        FilterButton.Content = item.Text;
        // Ignore during init before source data loaded / 初始化时源数据未加载则忽略
        if (_sourceEntries.Count == 0) return;
        ApplyFilter();
    }

    private void LoadMore()
    {
        var toLoad = Math.Min(BatchSize, _allEntries.Count - _displayItems.Count);
        for (int i = 0; i < toLoad; i++)
        {
            _displayItems.Add(_allEntries[_displayItems.Count]);
        }
    }

    // Navigation re-entrancy guard / 导航重入保护
    private bool _isNavigating;

    // Load more when scrolled near bottom / 滚动接近底部时加载更多
    private void ChangelogScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        // Skip all updates during programmatic navigation / 编程导航期间跳过所有更新
        if (!_isNavigating)
        {
            UpdateStickyVersionInfo();
            UpdateNavButtons();
        }
        if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && _displayItems.Count < _allEntries.Count)
        {
            LoadMore();
        }
    }

    // Track current sticky entry index / 追踪当前sticky条目索引
    private int _stickyEntryIndex = -1;

    // Sticky overlay: pin version info when card scrolls past top, push up by next / sticky覆盖层:卡片滚过顶部时固定版本信息,被下一项顶上去
    private void UpdateStickyVersionInfo()
    {
        if (_displayItems.Count == 0)
        {
            StickyVersionPanel.Visibility = Visibility.Collapsed;
            RestoreHiddenVersionPanel();
            _stickyEntryIndex = -1;
            return;
        }

        int firstVisibleIdx = FindFirstVisibleIndex();
        if (firstVisibleIdx <= 0)
        {
            StickyVersionPanel.Visibility = Visibility.Collapsed;
            RestoreHiddenVersionPanel();
            _stickyEntryIndex = -1;
            return;
        }

        double? firstVisibleTop = GetItemTop(firstVisibleIdx);
        if (firstVisibleTop is null) return;

        // Sticky shows the previous item which scrolled past top / sticky显示前一个已滚出视口的项
        int stickyIdx = firstVisibleIdx - 1;
        HideOriginalVersionInfo(stickyIdx);

        if (stickyIdx != _stickyEntryIndex)
        {
            // Entry changed, reset content + marquee / 条目变化,重置内容和跑马灯
            _stickyEntryIndex = stickyIdx;
            ShowSticky(_displayItems[stickyIdx], firstVisibleTop.Value);
        }
        else
        {
            // Same entry, only update push-up offset / 同一条目,仅更新顶上去偏移
            UpdateStickyPushUp(firstVisibleTop.Value);
        }
    }

    // Get item's top position relative to ScrollViewer viewport / 获取item相对ScrollViewer视口的顶部位置
    private double? GetItemTop(int index)
    {
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(index);
            if (el is not FrameworkElement fe) return null;
            var t = fe.TransformToVisual(ChangelogScrollViewer);
            var pos = t.TransformPoint(new Windows.Foundation.Point(0, 0));
            return pos.Y;
        }
        catch { return null; }
    }

    // Show sticky panel with entry and push-up offset / 显示sticky面板
    private void ShowSticky(ChangelogEntry entry, double firstVisibleTop)
    {
        // Stop previous marquee if running / 停止旧跑马灯
        if (StickyVersionText.Tag is Storyboard oldSb)
        {
            oldSb.Stop();
            if (StickyVersionText.RenderTransform is TranslateTransform oldTt) oldTt.X = 0;
            StickyVersionText.Tag = null;
        }

        StickyVersionText.Text = entry.Version;
        StickyDateText.Text = entry.Date;
        StickyPrereleaseBorder.Visibility = entry.IsPrerelease ? Visibility.Visible : Visibility.Collapsed;
        StickyVersionPanel.Visibility = Visibility.Visible;
        StickyVersionPanel.UpdateLayout();

        UpdateStickyPushUp(firstVisibleTop);

        // Start marquee after layout pass / 布局后启动跑马灯
        StickyVersionText.UpdateLayout();
        UpdateMarquee(StickyVersionText);
    }

    // Update push-up offset only, without resetting marquee / 仅更新顶上去偏移,不重置跑马灯
    private void UpdateStickyPushUp(double firstVisibleTop)
    {
        if (StickyVersionPanel.RenderTransform is TranslateTransform panelTt)
        {
            double panelHeight = StickyVersionPanel.ActualHeight;
            // Card spacing 10 + sticky margin 8 / 卡片间距10+sticky边距8
            double offsetY = Math.Min(0, firstVisibleTop - 10 - 8 - panelHeight);
            panelTt.Y = offsetY;
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

    // Find first visible item index, full range search with hint / 查找第一个可见项索引,带提示的全范围搜索
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
            if (top.Value >= 0) { visibleIdx = i; break; }
        }

        // Phase 1b: search backward if not found forward / 第一阶段b:向前未找到则向后搜索
        if (visibleIdx == -1)
        {
            for (int i = hint - 1; i >= 0; i--)
            {
                double? top = GetItemTop(i);
                if (top is null) continue;
                if (top.Value >= 0) { visibleIdx = i; break; }
            }
        }

        if (visibleIdx == -1) return -1;

        // Phase 2: scan backwards to find earliest visible item / 第二阶段:向前扫描找最早可见项
        // Top positions are monotonically increasing, stop at first negative / 顶部位置单调递增,遇到负值即停止
        for (int i = visibleIdx - 1; i >= 0; i--)
        {
            double? top = GetItemTop(i);
            if (top is null) continue;
            if (top.Value >= 0) visibleIdx = i;
            else break;
        }

        return visibleIdx;
    }

    // Navigation settle timer delay (ms) / 导航稳定定时器延迟(毫秒)
    private const int NavSettleDelayMs = 300;

    // Active navigation settle timer / 当前导航稳定定时器
    private DispatcherTimer? _navSettleTimer;

    // Scroll to item so its top aligns with viewport top / 滚动使条目顶部对齐视口顶部
    private void ScrollToItem(int index)
    {
        if (index < 0 || index >= _displayItems.Count) return;
        if (_isNavigating) return; // Prevent rapid clicks / 防止快速重复点击

        // Ensure target item is loaded / 确保目标条目已加载
        while (_displayItems.Count <= index && _displayItems.Count < _allEntries.Count)
            LoadMore();

        _isNavigating = true;
        PrevVersionButton.IsEnabled = false;
        NextVersionButton.IsEnabled = false;

        // Hide sticky to avoid overlap during jump / 跳转期间隐藏sticky避免重叠
        StickyVersionPanel.Visibility = Visibility.Collapsed;
        RestoreHiddenVersionPanel();
        _stickyEntryIndex = -1;

        // Defer position calc to after layout / 延迟到布局后计算位置
        DispatcherQueue.TryEnqueue(() =>
        {
            double? top = GetItemTop(index);
            if (top is null)
            {
                FinishNavigation();
                return;
            }
            // Clamp to valid scroll range / 限制到有效滚动范围
            double targetOffset = Math.Clamp(
                ChangelogScrollViewer.VerticalOffset + top.Value,
                0, ChangelogScrollViewer.ScrollableHeight);
            // Animated scroll (disableAnimation=false) / 带动画滚动
            ChangelogScrollViewer.ChangeView(null, targetOffset, null, false);

            // Update sticky after animation settles / 动画稳定后更新sticky
            _navSettleTimer?.Stop();
            _navSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(NavSettleDelayMs) };
            _navSettleTimer.Tick += OnNavSettleTimerTick;
            _navSettleTimer.Start();
        });
    }

    // Navigation settle timer callback / 导航稳定定时器回调
    private void OnNavSettleTimerTick(object? sender, object e)
    {
        _navSettleTimer?.Stop();
        FinishNavigation();
    }

    // Complete navigation: restore state and update UI / 完成导航:恢复状态并更新UI
    private void FinishNavigation()
    {
        _isNavigating = false;
        UpdateStickyVersionInfo();
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

    // Hide original version info column to avoid duplication with sticky / 隐藏原始版本信息列避免与sticky重复
    private void HideOriginalVersionInfo(int entryIndex)
    {
        RestoreHiddenVersionPanel();
        if (entryIndex < 0 || entryIndex >= _displayItems.Count) return;
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(entryIndex);
            if (el is not FrameworkElement fe) return;
            var panel = FindVersionInfoPanel(fe);
            if (panel is not null)
            {
                panel.Visibility = Visibility.Collapsed;
                _hiddenVersionPanel = panel;
            }
        }
        catch { }
    }

    // Restore previously hidden version info panel / 恢复之前隐藏的版本信息列
    private void RestoreHiddenVersionPanel()
    {
        if (_hiddenVersionPanel is not null)
        {
            _hiddenVersionPanel.Visibility = Visibility.Visible;
            _hiddenVersionPanel = null;
        }
    }

    // Find version info StackPanel in item template / 查找项模板中的版本信息StackPanel
    private static FrameworkElement? FindVersionInfoPanel(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is StackPanel sp) return sp;
        }
        return null;
    }

    // Update marquee on size change / 尺寸变化时更新跑马灯
    private void StickyVersionText_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is TextBlock tb) UpdateMarquee(tb);
    }

    private void UpdateEmptyState()
    {
        var hasEntries = _allEntries.Count > 0;
        EmptyState.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChangelogPage page)
        {
            page.LoadingOverlay.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void OnLoadingTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChangelogPage page)
        {
            page.LoadingTextBlock.Text = (string)e.NewValue;
        }
    }
}

/// <summary>
/// Changelog entry data / 更新日志条目数据
/// </summary>
public sealed class ChangelogEntry
{
    public string Version { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsPrerelease { get; set; }
    // Visibility for XAML binding / 供XAML绑定的可见性
    public Visibility PrereleaseVisibility => IsPrerelease ? Visibility.Visible : Visibility.Collapsed;
}
/// <summary>
/// Navigation parameter for ChangelogPage / 更新日志页导航参数
/// </summary>
public sealed class ChangelogPageParameter
{
    /// <summary>Repository as "owner/repo" or full GitHub URL / 仓库地址</summary>
    public string RepoUrl { get; set; } = string.Empty;
    /// <summary>Optional Markdown heading title to filter / 可选章节标题过滤，空则显示全部</summary>
    public string? SectionFilter { get; set; }
}