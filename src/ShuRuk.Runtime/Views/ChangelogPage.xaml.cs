using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.Runtime.Abstractions;
using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.ComponentModel;

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
        // Initial tooltip for image toggle / 图片切换初始提示
        ToolTipService.SetToolTip(ShowImagesToggle, RuntimeLocalizationHelper.GetString("RuntimeChangelogHideImages"));
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

    // Whether to show images in markdown / markdown中是否显示图片
    private bool _showImages = true;

    // Configure markdown theme on load / 加载时配置markdown主题
    private void MarkdownTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
        {
            mtb.ActualThemeChanged += (_, _) => ConfigureMarkdownTheme(mtb, forceRender: true);
            ConfigureMarkdownTheme(mtb, forceRender: true);
        }
    }



    // Apply theme-aware colors to markdown config / 应用主题感知颜色到markdown配置
    private void ConfigureMarkdownTheme(CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb, bool forceRender = false)
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
            // Limit inline markdown image width / 限制markdown内嵌图片宽度
            themes.ImageMaxWidth = 800;
            // Heading foreground follows theme / 标题前景色跟随主题
            themes.H1Foreground = codeFg;
            themes.H2Foreground = codeFg;
            themes.H3Foreground = codeFg;
            themes.H4Foreground = codeFg;
            themes.H5Foreground = codeFg;
            themes.H6Foreground = codeFg;

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

                }
            }
        }
        finally
        {
            _configuring.Remove(mtb);
        }
    }


    // Cache directory under %LOCALAPPDATA%\ShuRuk\changelog_cache / %LOCALAPPDATA%\ShuRuk\changelog_cache下的缓存目录
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShuRuk", "changelog_cache");
    private static readonly string PrefsPath = Path.Combine(CacheDir, "prefs.json");
    // JSON options: indented for debuggability / JSON选项:缩进便于调试
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    // Get cache file path for a repo+filter combination / 获取仓库+过滤组合的缓存文件路径
    private static string GetCachePath(string repoUrl, string? sectionFilter)
    {
        var key = $"{repoUrl}|{sectionFilter ?? ""}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(CacheDir, $"{hash}.json");
    }

    // Load cached entries if repo+filter matches / 加载缓存条目(仅当仓库+过滤匹配)
    private static async Task<ChangelogCacheData?> LoadCacheAsync(string repoUrl, string? sectionFilter)
    {
        try
        {
            var path = GetCachePath(repoUrl, sectionFilter);
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path);
            var cache = JsonSerializer.Deserialize<ChangelogCacheData>(json, JsonOpts);
            if (cache is null) return null;
            if (cache.RepoUrl != repoUrl || cache.SectionFilter != sectionFilter) return null;
            return cache;
        }
        catch { return null; }
    }

    // Save entries to cache file / 保存条目到缓存文件
    private static async Task SaveCacheAsync(string repoUrl, string? sectionFilter, List<ChangelogEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var cache = new ChangelogCacheData
            {
                RepoUrl = repoUrl,
                SectionFilter = sectionFilter,
                LastUpdated = DateTime.UtcNow,
                LatestTag = entries.Count > 0 ? entries[0].Version : "",
                Entries = entries
            };
            var path = GetCachePath(repoUrl, sectionFilter);
            var json = JsonSerializer.Serialize(cache, JsonOpts);
            await File.WriteAllTextAsync(path, json);
        }
        catch { }
    }

    // Load UI preferences (show images, filter tag) / 加载UI偏好(显示图片,过滤标签)
    private static async Task<ChangelogPrefs> LoadPrefsAsync()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return new ChangelogPrefs();
            var json = await File.ReadAllTextAsync(PrefsPath);
            return JsonSerializer.Deserialize<ChangelogPrefs>(json, JsonOpts) ?? new ChangelogPrefs();
        }
        catch { return new ChangelogPrefs(); }
    }

    // Save UI preferences / 保存UI偏好
    private static async Task SavePrefsAsync(ChangelogPrefs prefs)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var json = JsonSerializer.Serialize(prefs, JsonOpts);
            await File.WriteAllTextAsync(PrefsPath, json);
        }
        catch { }
    }

    // Current repo+filter for cache key / 当前仓库+过滤(缓存键)
    private string? _currentRepoUrl;
    private string? _currentSectionFilter;

    // Fetch data on navigation via injected provider / 导航时通过注入的提供者获取数据
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Apply localized empty state text / 应用本地化空状态文字
        EmptyTitle = RuntimeLocalizationHelper.GetString("RuntimeChangelogEmptyTitle");
        EmptyDescription = RuntimeLocalizationHelper.GetString("RuntimeChangelogEmptyDescription");
        // Set static localized expand/collapse text for binding / 设置静态本地化展开收起文字供绑定
        ChangelogEntry.ExpandText = RuntimeLocalizationHelper.GetString("RuntimeChangelogExpand");
        ChangelogEntry.CollapseText = RuntimeLocalizationHelper.GetString("RuntimeChangelogCollapse");

        // Read navigation parameter / 读取导航参数
        var param = e.Parameter as ChangelogPageParameter;
        var repoUrl = param?.RepoUrl ?? "microsoft/PowerToys";
        var sectionFilter = param?.SectionFilter;
        _currentRepoUrl = repoUrl;
        _currentSectionFilter = sectionFilter;

        // Restore UI preferences (image toggle, filter) / 恢复UI偏好(图片开关,过滤)
        var prefs = await LoadPrefsAsync();
        _showImages = prefs.ShowImages;
        ShowImagesToggle.IsChecked = _showImages;
        ToolTipService.SetToolTip(ShowImagesToggle,
            RuntimeLocalizationHelper.GetString(_showImages ? "RuntimeChangelogHideImages" : "RuntimeChangelogShowImages"));
        _filterTag = prefs.FilterTag;
        FilterButton.Content = _filterTag == FilterTagExcludePrerelease ? FilterExcludePrereleaseText : FilterAllText;

        var provider = RuntimeServices.ChangelogDataProvider;
        if (provider is null || !provider.IsAvailable)
        {
            Entries = [];
            return;
        }

        // Try cache first: show immediately then check for new releases / 先尝试缓存:立即显示再检查新release
        var cache = await LoadCacheAsync(repoUrl, sectionFilter);
        if (cache is not null && cache.Entries.Count > 0)
        {
            // Show cached data without loading overlay / 无加载遮罩直接显示缓存数据
            Entries = cache.Entries;
            IsLoading = false;
            // Background incremental check for new releases / 后台增量检测新release
            _ = CheckForNewReleasesAsync(provider, repoUrl, sectionFilter, cache.LatestTag);
        }
        else
        {
            // No cache: full load with loading overlay / 无缓存:全量加载带遮罩
            IsLoading = true;
            LoadingText = RuntimeLocalizationHelper.GetString("RuntimeChangelogLoading");
            try
            {
                var entries = await provider.GetEntriesAsync(repoUrl, sectionFilter);
                IsLoading = false;
                Entries = entries;
                await SaveCacheAsync(repoUrl, sectionFilter, entries);
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
    }

    // Background incremental check: fetch only entries newer than cached latest tag / 后台增量检测:仅获取比缓存最新tag更新的条目
    private async Task CheckForNewReleasesAsync(IChangelogDataProvider provider, string repoUrl, string? sectionFilter, string knownLatestTag)
    {
        try
        {
            var newEntries = await provider.GetNewEntriesAsync(repoUrl, sectionFilter, knownLatestTag);
            if (newEntries.Count == 0) return;

            // Marshal to UI thread to update collections / 切换到UI线程更新集合
            DispatcherQueue.TryEnqueue(() =>
            {
                // Parse segments for new entries / 为新条目解析段
                foreach (var entry in newEntries)
                {
                    entry.Segments = ChangelogSegment.Parse(entry.Notes);
                    entry.InitializeVisibleSegments();
                }

                // Prepend to source (newest first), respecting current filter / 前置到源(最新在前),尊重当前过滤
                for (int i = newEntries.Count - 1; i >= 0; i--)
                {
                    var entry = newEntries[i];
                    _sourceEntries.Insert(0, entry);
                    if (_filterTag == FilterTagAll || !entry.IsPrerelease)
                    {
                        _allEntries.Insert(0, entry);
                        _displayItems.Insert(0, entry);
                    }
                }

                // Ensure filter controls visible / 确保过滤控件可见
                if (_sourceEntries.Count > 0)
                {
                    FilterBorder.Visibility = Visibility.Visible;
                    ImageToggleBorder.Visibility = Visibility.Visible;
                    NavButtonsPanel.Visibility = Visibility.Visible;
                }
                UpdateNavButtons();
                UpdateEmptyState();
            });

            // Update cache with merged list / 用合并后的列表更新缓存
            await SaveCacheAsync(repoUrl, sectionFilter, _sourceEntries.ToList());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changelog incremental check failed: {ex}");
        }
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

    // Image failed: no action (LoadingVisibility handled by data layer) / 图片加载失败:无需操作(LoadingVisibility由数据层处理)
    private void Image_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
    }

    // Toggle card expand/collapse via ObservableCollection incremental update / 通过ObservableCollection增量更新切换展开折叠
    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var entry = btn.DataContext as ChangelogEntry ?? btn.Tag as ChangelogEntry;
        if (entry is null) return;
        entry.IsExpanded = !entry.IsExpanded;
        // Track last expanded entry for floating collapse button / 跟踪最近展开条目供浮动收起
        if (entry.IsExpanded)
        {
            _lastExpandedEntry = entry;
            _lastExpandedIndex = _displayItems.IndexOf(entry);
            // Defer check: markdown render needs a moment / 延迟检测:markdown渲染需片刻
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (_, _) => { t.Stop(); UpdateCollapseButton(); };
            t.Start();
        }
        else
        {
            _lastExpandedEntry = null;
            _lastExpandedIndex = -1;
            CollapseFloatingButton.Visibility = Visibility.Collapsed;
        }
    }

    private static void OnEntriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChangelogPage page)
        {
            // Store source entries then apply filter / 存储原始条目后应用过滤
            page._sourceEntries.Clear();
            page._sourceEntries.AddRange(page.Entries ?? []);
            // Parse segments for each entry / 为每个条目解析段
            foreach (var entry in page._sourceEntries)
            {
                entry.Segments = ChangelogSegment.Parse(entry.Notes);
                entry.InitializeVisibleSegments();
            }
            page.ApplyFilter();
            // Show filter and play appear animation / 显示过滤按钮并播放出现动画
            if (page._sourceEntries.Count > 0)
            {
                page.FilterBorder.Visibility = Visibility.Visible;
                // Sync DropDownButton content with restored filter tag / 同步DropDownButton显示与恢复的过滤标签
                page.FilterButton.Content = page._filterTag == FilterTagExcludePrerelease
                    ? page.FilterExcludePrereleaseText : page.FilterAllText;
                page.ImageToggleBorder.Visibility = Visibility.Visible;
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

    // Filter tag constants / 过滤标签常量
    private const string FilterTagAll = "all";
    private const string FilterTagExcludePrerelease = "exclude-prerelease";
    // Current filter tag / 当前过滤标签
    private string _filterTag = FilterTagAll;

    // Filter by tag (exclude pre-release if chosen) / 按标签过滤
    private void ApplyFilter()
    {
        var excludePrerelease = _filterTag == FilterTagExcludePrerelease;
        _allEntries.Clear();
        // Inline filter avoids LINQ iterator allocation / 内联过滤避免LINQ迭代器分配
        foreach (var entry in _sourceEntries)
            if (!excludePrerelease || !entry.IsPrerelease)
                _allEntries.Add(entry);
        _displayItems.Clear();
        LoadMore();
        UpdateEmptyState();
    }

    private async void FilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        _filterTag = item.Tag?.ToString() ?? FilterTagAll;
        FilterButton.Content = item.Text;
        // Persist preference / 持久化偏好
        await SavePrefsAsync(new ChangelogPrefs { ShowImages = _showImages, FilterTag = _filterTag });
        // Ignore during init before source data loaded / 初始化时源数据未加载则忽略
        if (_sourceEntries.Count == 0) return;
        ApplyFilter();
    }

    // Toggle image visibility in all markdown blocks / 切换所有markdown块的图片可见性
    private async void ShowImagesToggle_Click(object sender, RoutedEventArgs e)
    {
        _showImages = ShowImagesToggle.IsChecked == true;
        // Update tooltip to reflect current state / 更新提示反映当前状态
        ToolTipService.SetToolTip(ShowImagesToggle,
            RuntimeLocalizationHelper.GetString(_showImages ? "RuntimeChangelogHideImages" : "RuntimeChangelogShowImages"));
        // Persist preference / 持久化偏好
        await SavePrefsAsync(new ChangelogPrefs { ShowImages = _showImages, FilterTag = _filterTag });

        // Show progress indicator, disable button during update / 更新期间显示进度指示并禁用按钮
        ShowImagesIcon.Visibility = Visibility.Collapsed;
        ShowImagesProgress.Visibility = Visibility.Visible;
        ShowImagesProgress.IsActive = true;
        ShowImagesToggle.IsEnabled = false;

        await UpdateAllImageVisibilityAsync();

        // Restore button state / 恢复按钮状态
        ShowImagesProgress.IsActive = false;
        ShowImagesProgress.Visibility = Visibility.Collapsed;
        ShowImagesIcon.Visibility = Visibility.Visible;
        ShowImagesToggle.IsEnabled = true;
    }

    // Batch size for incremental visibility update / 增量可见性更新的批大小
    private const int ImageBatchSize = 5;

    // Update image visibility via data binding / 通过数据绑定更新图片可见性
    private async Task UpdateAllImageVisibilityAsync()
    {
        var visibility = _showImages ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _displayItems.Count; i += ImageBatchSize)
        {
            int end = Math.Min(i + ImageBatchSize, _displayItems.Count);
            for (int j = i; j < end; j++)
                foreach (var seg in _displayItems[j].Segments)
                    if (seg.IsImage) seg.ImageVisibility = visibility;
            // Yield to UI thread between batches / 批间让出UI线程
            await Task.Yield();
        }
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
            UpdateCollapseButton();
        }
        if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && _displayItems.Count < _allEntries.Count)
        {
            LoadMore();
        }
    }

    // Track current sticky entry index / 追踪当前sticky条目索引
    private int _stickyEntryIndex = -1;

    // Last expanded entry for floating collapse button / 最近展开条目供浮动收起
    private ChangelogEntry? _lastExpandedEntry;
    private int _lastExpandedIndex = -1;

    // Show floating collapse button when expanded card bottom is off-screen / 展开卡片底部不可见时显示浮动收起按钮
    private void UpdateCollapseButton()
    {
        if (_lastExpandedEntry is null || !_lastExpandedEntry.IsExpanded || _lastExpandedIndex < 0 || _lastExpandedIndex >= _displayItems.Count)
        {
            CollapseFloatingButton.Visibility = Visibility.Collapsed;
            return;
        }
        var top = GetItemTop(_lastExpandedIndex);
        if (top is null) { CollapseFloatingButton.Visibility = Visibility.Collapsed; return; }
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex);
            if (el is not FrameworkElement fe) { CollapseFloatingButton.Visibility = Visibility.Collapsed; return; }
            double bottom = top.Value + fe.ActualHeight;
            CollapseFloatingButton.Visibility = bottom > ChangelogScrollViewer.ViewportHeight ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { CollapseFloatingButton.Visibility = Visibility.Collapsed; }
    }

    private void CollapseFloatingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExpandedEntry is not null) _lastExpandedEntry.IsExpanded = false;
        _lastExpandedEntry = null;
        _lastExpandedIndex = -1;
        CollapseFloatingButton.Visibility = Visibility.Collapsed;
    }

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
public sealed class ChangelogEntry : INotifyPropertyChanged
{
    // Collapsed card max content height / 折叠态卡片内容最大高度
    public const double CollapsedMaxHeight = 1000;
    // Max visible segments when collapsed / 折叠时最大可见段数
    private const int CollapsedSegmentLimit = 6;
    // Estimate threshold: notes longer than this likely overflow / 估算阈值:notes超过此长度可能溢出
    private const int OverflowNoteLength = 1200;
    private const int OverflowSegmentCount = 6;

    public string Version { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsPrerelease { get; set; }
    // Parsed segments (text + image alternating), runtime only not serialized / 解析后的段(文字与图片交替),运行时 only 不序列化
    [JsonIgnore]
    public List<ChangelogSegment> Segments { get; set; } = [];
    // Visibility for XAML binding / 供XAML绑定的可见性
    public Visibility PrereleaseVisibility => IsPrerelease ? Visibility.Visible : Visibility.Collapsed;

    // Expand/collapse state, persisted per session only / 展开折叠状态,仅会话内保持
    private bool _isExpanded;
    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                UpdateVisibleSegments();
                RaisePropertyChanged(nameof(IsExpanded));
                RaisePropertyChanged(nameof(ExpandButtonGlyph));
                RaisePropertyChanged(nameof(ExpandButtonText));
                RaisePropertyChanged(nameof(FadeOverlayVisibility));
            }
        }
    }

    // Observable segments for incremental add/remove (no container rebuild, preserves scroll) / 可观察段用于增量增删(不重建容器,保持滚动)
    [JsonIgnore]
    public ObservableCollection<ChangelogSegment> VisibleSegments { get; } = [];

    // Initialize VisibleSegments from Segments based on expand state / 根据展开状态从Segments初始化VisibleSegments
    public void InitializeVisibleSegments()
    {
        VisibleSegments.Clear();
        int count = _isExpanded ? Segments.Count : Math.Min(Segments.Count, CollapsedSegmentLimit);
        for (int i = 0; i < count; i++)
            VisibleSegments.Add(Segments[i]);
    }

    // Incrementally add/remove segments on expand/collapse toggle / 展开/收起时增量增删段
    private void UpdateVisibleSegments()
    {
        if (_isExpanded)
        {
            // Add missing segments when expanding / 展开时添加缺失段
            for (int i = VisibleSegments.Count; i < Segments.Count; i++)
                VisibleSegments.Add(Segments[i]);
        }
        else
        {
            // Remove excess segments when collapsing / 收起时移除多余段
            while (VisibleSegments.Count > CollapsedSegmentLimit)
                VisibleSegments.RemoveAt(VisibleSegments.Count - 1);
        }
    }

    // Chevron glyph: down when collapsed, up when expanded / 折叠向下,展开向上
    [JsonIgnore]
    public string ExpandButtonGlyph => _isExpanded ? "\uE70E" : "\uE70D";

    // Localized expand/collapse text, set statically by page / 本地化展开收起文字,由页面静态设置
    public static string ExpandText { get; set; } = "Expand";
    public static string CollapseText { get; set; } = "Collapse";
    [JsonIgnore]
    public string ExpandButtonText => _isExpanded ? CollapseText : ExpandText;

    // Estimate whether content overflows collapsed height / 估算内容是否超出折叠高度
    [JsonIgnore]
    public bool HasOverflow => Notes.Length > OverflowNoteLength || Segments.Count > OverflowSegmentCount;
    [JsonIgnore]
    public Visibility ExpandButtonVisibility => HasOverflow ? Visibility.Visible : Visibility.Collapsed;
    // Fade gradient overlay: only when collapsed and has overflow / 渐变遮罩:仅折叠且有溢出时显示
    [JsonIgnore]
    public Visibility FadeOverlayVisibility => !_isExpanded && HasOverflow ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A segment of changelog content: either markdown text or an image / 更新日志内容段:markdown文字或图片
/// </summary>
public sealed class ChangelogSegment : INotifyPropertyChanged
{
    // Markdown text (empty for image segments) / markdown文字(图片段为空)
    public string MarkdownText { get; set; } = string.Empty;
    // Image URL (empty for text segments) / 图片URL(文字段为空)
    public string ImageUrl { get; set; } = string.Empty;
    // Image alt text / 图片替代文字
    public string AltText { get; set; } = string.Empty;
    public bool IsImage => !string.IsNullOrEmpty(ImageUrl);

    // Text segment visibility (always visible for text, collapsed for image) / 文字段可见性(文字段可见,图片段隐藏)
    public Visibility TextVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;
    // Image segment visibility (controlled by toggle, collapsed for text) / 图片段可见性(由toggle控制,文字段隐藏)
    private Visibility _imageVisibility = Visibility.Visible;
    public Visibility ImageVisibility
    {
        get => IsImage ? _imageVisibility : Visibility.Collapsed;
        set
        {
            if (_imageVisibility != value)
            {
                _imageVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageVisibility)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Image loaded state for loading placeholder / 图片加载状态(用于加载占位)
    private bool _isImageLoaded;
    public bool IsImageLoaded
    {
        get => _isImageLoaded;
        set
        {
            if (_isImageLoaded != value)
            {
                _isImageLoaded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsImageLoaded)));
                // Update visibility directly instead of computed properties / 直接更新可见性而非计算属性
                LoadingVisibility = IsImage && !value ? Visibility.Visible : Visibility.Collapsed;
                ImageLoadedVisibility = IsImage && value ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
    // Loading placeholder visibility (backing field for reliable x:Bind) / 加载占位可见性( backing field 确保 x:Bind 可靠)
    private Visibility _loadingVisibility = Visibility.Visible;
    public Visibility LoadingVisibility
    {
        get => _loadingVisibility;
        private set
        {
            if (_loadingVisibility != value)
            {
                _loadingVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadingVisibility)));
            }
        }
    }
    // Loaded image visibility (backing field for reliable x:Bind) / 已加载图片可见性( backing field 确保 x:Bind 可靠)
    private Visibility _imageLoadedVisibility = Visibility.Collapsed;
    public Visibility ImageLoadedVisibility
    {
        get => _imageLoadedVisibility;
        private set
        {
            if (_imageLoadedVisibility != value)
            {
                _imageLoadedVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageLoadedVisibility)));
            }
        }
    }

    // Max image width: small images use natural width, large capped / 图片最大宽度:小图用自然宽度,大图限制
    public const double MaxImageWidth = 800;
    private double _imageMaxWidth = MaxImageWidth;
    public double ImageMaxWidth
    {
        get => _imageMaxWidth;
        set
        {
            if (Math.Abs(_imageMaxWidth - value) > 0.5)
            {
                _imageMaxWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageMaxWidth)));
            }
        }
    }

    // Cached BitmapImage for binding (avoids string-to-Uri conversion crash) / 缓存的BitmapImage(避免string转Uri崩溃)
    private ImageSource? _imageSource;
    // Bitmap loaded flag set by BitmapImage.ImageOpened (data layer, survives recycle) / 位图加载标记(数据层,回收后保留)
    private bool _bitmapLoaded;
    public bool BitmapLoaded => _bitmapLoaded;
    public ImageSource? ImageSource
    {
        get
        {
            if (_imageSource is not null) return _imageSource;
            if (string.IsNullOrEmpty(ImageUrl)) return null;
            var url = NormalizeImageUrl(ImageUrl);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                System.Diagnostics.Debug.WriteLine($"[Changelog] Invalid image URL: {url}");
                return null;
            }
            var bi = new BitmapImage(uri);
            bi.ImageOpened += (s, e) =>
            {
                _bitmapLoaded = true;
                LoadingVisibility = Visibility.Collapsed;
                // Cap MaxWidth at natural width for small images / 小图片MaxWidth设为自然宽度避免放大
                if (bi.PixelWidth > 0)
                    ImageMaxWidth = Math.Min(MaxImageWidth, bi.PixelWidth);
            };
            bi.ImageFailed += (s, e) =>
            {
                _bitmapLoaded = true;
                LoadingVisibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine($"[Changelog] Image load failed: {url}");
            };
            _imageSource = bi;
            return _imageSource;
        }
    }

    // Normalize relative image URLs to absolute (GitHub user-attachments etc.) / 补全相对图片URL为绝对路径
    private static string NormalizeImageUrl(string url)
    {
        // Decode common HTML entities in URL / 解码URL中的常见HTML实体
        url = url.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);
        // Fix backslashes to forward slashes / 反斜杠转正斜杠
        url = url.Replace('\\', '/');
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith('/'))
            return "https://github.com" + url;
        return "https://github.com/" + url;
    }

    // Regex to match markdown images: ![alt](url) / 匹配markdown图片的正则
    private static readonly Regex ImageRegex = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    // Regex to match <img> tags: <img src="url"> / 匹配img标签的正则
    private static readonly Regex ImgTagRegex = new(@"<img\s[^>]*?src\s*=\s*[""']([^""']+)[""'][^>]*?/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Parse markdown into alternating text/image segments / 将markdown解析为文字图片交替段
    public static List<ChangelogSegment> Parse(string markdown)
    {
        var segments = new List<ChangelogSegment>();
        if (string.IsNullOrEmpty(markdown)) return segments;
        // Convert <img> tags to markdown image syntax for unified parsing / 统一转换img标签为markdown图片语法
        markdown = ImgTagRegex.Replace(markdown, m => $"![]({m.Groups[1].Value})");
        int lastEnd = 0;
        foreach (Match m in ImageRegex.Matches(markdown))
        {
            // Text before image / 图片前的文字
            if (m.Index > lastEnd)
            {
                var text = markdown[lastEnd..m.Index];
                if (!string.IsNullOrWhiteSpace(text))
                    segments.Add(new ChangelogSegment { MarkdownText = text.Trim() });
            }
            // Image segment / 图片段
            segments.Add(new ChangelogSegment
            {
                ImageUrl = m.Groups[2].Value.Trim(),
                AltText = m.Groups[1].Value.Trim()
            });
            lastEnd = m.Index + m.Length;
        }
        // Remaining text / 剩余文字
        if (lastEnd < markdown.Length)
        {
            var text = markdown[lastEnd..];
            if (!string.IsNullOrWhiteSpace(text))
                segments.Add(new ChangelogSegment { MarkdownText = text.Trim() });
        }
        // Fallback: no segments parsed, use raw markdown / 无段时用原始markdown
        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(markdown))
            segments.Add(new ChangelogSegment { MarkdownText = markdown.Trim() });
        return segments;
    }
}

/// <summary>
/// Persisted changelog cache for a repo+filter combination / 仓库+过滤组合的持久化更新日志缓存
/// </summary>
public sealed class ChangelogCacheData
{
    public string RepoUrl { get; set; } = string.Empty;
    public string? SectionFilter { get; set; }
    public DateTime LastUpdated { get; set; }
    public string LatestTag { get; set; } = string.Empty;
    public List<ChangelogEntry> Entries { get; set; } = [];
}

/// <summary>
/// Persisted UI preferences for changelog page / 更新日志页持久化UI偏好
/// </summary>
public sealed class ChangelogPrefs
{
    public bool ShowImages { get; set; } = true;
    public string FilterTag { get; set; } = "all";
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