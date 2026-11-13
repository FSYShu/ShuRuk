using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace ShuRuk.Runtime.Services.Markdown;

// Centralized markdown styling: theme colors, inline code font, link replacement
// / 集中markdown样式:主题色,inline code字体,链接替换
// Extracted from ChangelogPage to reduce page size and enable reuse
// / 从ChangelogPage提取以减小页面体积并支持复用
internal static class MarkdownStyler
{
    // Re-entrancy guard for theme config / 主题配置重入保护
    private static readonly HashSet<CommunityToolkit.WinUI.Controls.MarkdownTextBlock> _configuring = [];

    // Retry constants / 重试常量
    // Event-driven model: SizeChanged/Text DP is the sole trigger, no timer on Loaded
    // / 事件驱动模型:SizeChanged/Text DP为唯一触发,Loaded不启动定时器
    // 2 attempts * 80ms = 160ms (debounce already waited 200ms for render)
    // / 2次*80ms=160ms(防抖已等200ms供渲染)
    private const int MaxAttempts = 2;
    private const int RetryIntervalMs = 80;

    // Per-MTB debounce timers for SizeChanged style re-application
    // / 每MTB防抖计时器用于SizeChanged样式重应用
    // ConditionalWeakTable allows GC of MTB when no other references exist
    // / ConditionalWeakTable允许MTB无其他引用时被GC
    private static readonly ConditionalWeakTable<CommunityToolkit.WinUI.Controls.MarkdownTextBlock,
        DispatcherTimer> _sizeChangedTimers = new();

    // === Global batch processing queue / 全局批处理队列 ===
    // Replaces per-MTB timers with a single global timer that processes multiple MTBs per tick
    // / 用单全局定时器替代每MTB定时器,每tick处理多个MTB
    // Reduces timer count from N*MaxAttempts to 1, cutting UI thread contention
    // / 定时器数从N*MaxAttempts减到1,降低UI线程争用
    private const int BatchProcessCount = 5;
    private static readonly Queue<PendingApply> _pendingApply = new();
    private static DispatcherTimer? _batchTimer;

    // Pending style application item with weak reference to MTB / 待应用样式项(弱引用MTB)
    private sealed class PendingApply
    {
        public required WeakReference<CommunityToolkit.WinUI.Controls.MarkdownTextBlock> MtbRef;
        public required bool IsDark;
        public required DispatcherQueue Dq;
        public bool NeedCode = true;
        public bool NeedLink = true;
        public bool NeedTable = true;
        public int Attempt;
    }

    // === Theme brush resolution / 主题画刷解析 ===

    // Resolve a ThemeResource brush for a specific theme via ThemeDictionaries
    // / 经ThemeDictionaries解析指定主题的ThemeResource画刷
    // Application.Resources[key] returns the Application-theme instance
    // which does not follow window root.RequestedTheme
    // / Application.Resources[key]返回Application主题实例,不跟随窗口root.RequestedTheme
    // Only lookup via ThemeDictionaries per ActualTheme;
    // never fallback to appRes.TryGetValue (returns wrong-theme brush)
    // / 只经ThemeDictionaries按ActualTheme查找;不兜底appRes.TryGetValue(返回错误主题画刷)
    private static Brush? ResolveThemeBrush(string key, bool isDark)
    {
        try
        {
            var appRes = Application.Current.Resources;
            if (appRes.ThemeDictionaries is System.Collections.IDictionary td)
            {
                var themeKey = isDark ? "Dark" : "Light";
                if (td.Contains(themeKey) && td[themeKey] is ResourceDictionary themeDict
                    && themeDict.TryGetValue(key, out var b) && b is Brush brush)
                    return brush;
            }
            return null;
        }
        catch { return null; }
    }

    // Fallback brushes when ThemeResource lookup fails / ThemeResource查找失败时的兜底画刷
    private static SolidColorBrush FallbackTextBrush(bool isDark) =>
        new(Windows.UI.Color.FromArgb(0xFF,
            isDark ? (byte)0xE4 : (byte)0x1F,
            isDark ? (byte)0xE4 : (byte)0x1F,
            isDark ? (byte)0xE4 : (byte)0x1F));
    private static SolidColorBrush FallbackCodeBgBrush(bool isDark) =>
        new(Windows.UI.Color.FromArgb(0xFF,
            isDark ? (byte)0x2C : (byte)0xF3,
            isDark ? (byte)0x2C : (byte)0xF3,
            isDark ? (byte)0x2C : (byte)0xF3));
    private static SolidColorBrush FallbackTableBorderBrush(bool isDark) =>
        new(Windows.UI.Color.FromArgb(0xFF,
            isDark ? (byte)0x56 : (byte)0xE5,
            isDark ? (byte)0x56 : (byte)0xE5,
            isDark ? (byte)0x56 : (byte)0xE5));

    // Resolve link brush: try ThemeResource accent, fallback to UISettings accent
    // / 解析链接画刷:尝试ThemeResource accent,兜底UISettings accent
    private static Brush ResolveLinkBrush(bool isDark)
    {
        var brush = ResolveThemeBrush("AccentTextFillColorPrimaryBrush", isDark);
        if (brush != null) return brush;
        // Fallback to user-specified colors: light=#003F94, dark=#9AEBFF
        // / 兜底用户指定色:浅色=#003F94,深色=#9AEBFF
        return new SolidColorBrush(isDark
            ? Windows.UI.Color.FromArgb(0xFF, 0x9A, 0xEB, 0xFF)
            : Windows.UI.Color.FromArgb(0xFF, 0x00, 0x3F, 0x94));
    }

    // === Theme configuration / 主题配置 ===

    // Apply theme-aware colors to markdown config / 应用主题感知颜色到markdown配置
    // Uses ThemeResource brushes resolved per ActualTheme so theme switch is automatic
    // / 使用按ActualTheme解析的ThemeResource画刷,主题切换自动生效
    public static void ConfigureTheme(
        CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb,
        bool isDark, bool forceRender, DispatcherQueue dq)
    {
        if (_configuring.Contains(mtb)) return;
        _configuring.Add(mtb);
        try
        {
            var themes = mtb.Config.Themes;

            var codeFg = ResolveThemeBrush("TextFillColorPrimaryBrush", isDark) ?? FallbackTextBrush(isDark);
            var codeBg = ResolveThemeBrush("SubtleFillColorTertiaryBrush", isDark) ?? FallbackCodeBgBrush(isDark);
            var tableBorder = ResolveThemeBrush("DividerStrokeColorDefaultBrush", isDark)
                ?? FallbackTableBorderBrush(isDark);
            var linkBrush = ResolveLinkBrush(isDark);

            themes.InlineCodeForeground = codeFg;
            themes.LinkForeground = linkBrush;
            themes.InlineCodeBackground = codeBg;
            // Inline code font size / inline code字体大小
            themes.InlineCodeFontSize = 14;
            themes.TableHeadingBackground = codeBg;
            themes.TableBorderBrush = tableBorder;
            // Code block font: Consolas / 代码块字体:Consolas
            themes.CodeBlockFontFamily = new FontFamily("Consolas");
            // Remove inline code border, keep background; code block border stays default
            // / 移除inline code边框保留背景;代码块边框保持默认
            themes.InlineCodeBorderThickness = new Thickness(0);
            // Limit inline markdown image width / 限制markdown内嵌图片宽度
            themes.ImageMaxWidth = 800;

            // Heading foreground follows theme / 标题前景色跟随主题
            themes.H1Foreground = codeFg;
            themes.H2Foreground = codeFg;
            themes.H3Foreground = codeFg;
            themes.H4Foreground = codeFg;
            themes.H5Foreground = codeFg;
            themes.H6Foreground = codeFg;
            // Heading font weight: SemiBold (default may be Normal after color override)
            // / 标题字重:SemiBold(改色后默认可能变Normal)
            themes.H1FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            themes.H2FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            themes.H3FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            themes.H4FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            themes.H5FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            themes.H6FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;

            // Assign back in case Themes is a value type / 赋回以防Themes是值类型
            mtb.Config.Themes = themes;

            // Force re-render only when theme changes, not on size change
            // / 仅主题变化时强制重渲染,尺寸变化时不需要
            if (forceRender)
            {
                var md = mtb.Text;
                if (!string.IsNullOrEmpty(md))
                {
                    // Two-frame re-render: clear first, restore next frame to ensure full re-render
                    // / 两帧重渲染:先清空下一帧再恢复,确保完全重渲染
                    // Single-frame Text toggle may be batched/skipped by MarkdownTextBlock,
                    // leaving stale link colors
                    // / 单帧Text切换可能被批处理跳过,遗留旧链接颜色
                    dq.TryEnqueue(() =>
                    {
                        if (mtb.Text == md)
                        {
                            mtb.Text = string.Empty;
                            dq.TryEnqueue(() =>
                            {
                                mtb.Text = md;
                                // Re-apply all post-render styles after re-render
                                // / 重渲染后重新应用所有后渲染样式
                                ScheduleApplyAll(mtb, isDark, dq);
                            });
                        }
                    });
                }
            }
        }
        finally
        {
            _configuring.Remove(mtb);
        }
    }

    // Re-apply theme to all MarkdownTextBlocks in visual tree / 遍历可视化树重新应用主题
    public static void ReapplyTheme(DependencyObject root, bool isDark, DispatcherQueue dq)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
                ConfigureTheme(mtb, isDark, true, dq);
            ReapplyTheme(child, isDark, dq);
        }
    }

    // === Inline code font / Inline code字体 ===

    // Set inline code font to Consolas by walking visual tree and RichTextBlock inlines
    // / 遍历可视化树和RichTextBlock inlines设inline code字体为Consolas
    // Inline code = InlineUIContainer→Border(CornerRadius=2)→TextBlock;
    // source sets no FontFamily so it inherits parent
    // / inline code为InlineUIContainer→Border(CornerRadius=2)→TextBlock;源码未设FontFamily故继承父级
    // Returns count of TextBlocks updated (0 means not rendered yet, caller may retry)
    // / 返回更新的TextBlock数(0表示尚未渲染,调用方可重试)
    public static int ApplyInlineCodeFont(DependencyObject root)
    {
        int found = 0;
        var consolas = new FontFamily("Consolas");
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border b && b.CornerRadius != new CornerRadius(0))
            {
                for (int j = 0; j < VisualTreeHelper.GetChildrenCount(b); j++)
                {
                    if (VisualTreeHelper.GetChild(b, j) is TextBlock tb)
                    {
                        tb.FontFamily = consolas;
                        found++;
                    }
                }
            }
            // RichTextBlock inlines: InlineUIContainer.Child may not be in visual tree yet
            // / RichTextBlock inlines:InlineUIContainer.Child可能尚未在可视化树
            if (child is RichTextBlock rtb)
            {
                foreach (var block in rtb.Blocks)
                {
                    if (block is Paragraph para) found += ApplyInlineCodeFontInlines(para.Inlines, consolas);
                }
            }
            found += ApplyInlineCodeFont(child);
        }
        return found;
    }

    // Walk inlines for InlineUIContainer→Border(CornerRadius!=0)→TextBlock; recurse Span.Inlines
    // / 遍历inlines找InlineUIContainer→Border(CornerRadius!=0)→TextBlock;递归Span.Inlines
    private static int ApplyInlineCodeFontInlines(IEnumerable<Inline> inlines, FontFamily consolas)
    {
        int found = 0;
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer iuc
                && iuc.Child is Border b && b.CornerRadius != new CornerRadius(0)
                && b.Child is TextBlock tb)
            {
                tb.FontFamily = consolas;
                found++;
            }
            else if (inline is Span span)
            {
                found += ApplyInlineCodeFontInlines(span.Inlines, consolas);
            }
        }
        return found;
    }

    // === Table wrapping / 表格换行 ===

    // Apply TextWrapping=Wrap to all TextBlocks (table cells default to NoWrap)
    // / 对所有TextBlock设自动换行(表格单元格默认不换行)
    // Returns count of TextBlocks updated / 返回更新的TextBlock数
    public static int ApplyTableWrapping(DependencyObject root)
    {
        int found = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb && tb.TextWrapping != TextWrapping.Wrap)
            {
                tb.TextWrapping = TextWrapping.Wrap;
                found++;
            }
            found += ApplyTableWrapping(child);
        }
        return found;
    }

    // === Link styles / 链接样式 ===

    // Replace Hyperlinks with InlineUIContainer+TextBlock to bypass native VSM color
    // / 用InlineUIContainer+TextBlock替换Hyperlink绕过原生VSM颜色
    // Hyperlink native code ignores XAML resource overrides; TextBlock respects Foreground
    // / Hyperlink原生代码忽略XAML资源覆盖;TextBlock尊重Foreground
    // Returns count of hyperlinks replaced / 返回替换的Hyperlink数
    public static int ApplyLinkStyle(DependencyObject root, bool isDark)
    {
        var normalBrush = ResolveLinkBrush(isDark);
        // Hover color matches normal (per user requirement) / hover色与正常色一致(用户要求)
        return ReplaceHyperlinksRecursive(root, normalBrush, normalBrush);
    }

    private static int ReplaceHyperlinksRecursive(DependencyObject root, Brush normalBrush, Brush hoverBrush)
    {
        int found = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is RichTextBlock rtb)
            {
                foreach (var block in rtb.Blocks)
                {
                    if (block is Paragraph para)
                        found += ReplaceHyperlinksInInlines(para.Inlines, normalBrush, hoverBrush);
                }
            }
            found += ReplaceHyperlinksRecursive(child, normalBrush, hoverBrush);
        }
        return found;
    }

    private static int ReplaceHyperlinksInInlines(InlineCollection inlines, Brush normalBrush, Brush hoverBrush)
    {
        // Collect hyperlinks first to avoid modifying during iteration / 先收集Hyperlink避免迭代中修改
        var replacements = new List<(int index, Hyperlink link)>();
        for (int i = 0; i < inlines.Count; i++)
        {
            if (inlines[i] is Hyperlink link)
                replacements.Add((i, link));
            else if (inlines[i] is Span span)
                ReplaceHyperlinksInInlines(span.Inlines, normalBrush, hoverBrush);
        }

        foreach (var (index, link) in replacements)
        {
            // Extract text from Hyperlink / 从Hyperlink提取文本
            var text = new StringBuilder();
            ExtractTextFromInlines(link.Inlines, text);
            var uri = link.NavigateUri;

            // Create TextBlock inheriting font from Hyperlink, with correct foreground
            // / 创建TextBlock继承Hyperlink字体,设正确前景色
            var tb = new TextBlock
            {
                Text = text.ToString(),
                Foreground = normalBrush,
                TextDecorations = Windows.UI.Text.TextDecorations.Underline,
                IsTapEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = link.FontFamily,
                FontSize = link.FontSize,
                FontWeight = link.FontWeight,
                FontStyle = link.FontStyle,
                FontStretch = link.FontStretch,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Hover effect / 悬停效果
            tb.PointerEntered += (_, _) => tb.Foreground = hoverBrush;
            tb.PointerExited += (_, _) => tb.Foreground = normalBrush;
            // Click to open URI / 点击打开URI
            if (uri is not null)
                tb.Tapped += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(uri);

            // Replace Hyperlink with InlineUIContainer / 用InlineUIContainer替换Hyperlink
            inlines.RemoveAt(index);
            inlines.Insert(index, new InlineUIContainer { Child = tb });
        }
        return replacements.Count;
    }

    private static void ExtractTextFromInlines(InlineCollection inlines, StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) sb.Append(run.Text);
            else if (inline is Span span) ExtractTextFromInlines(span.Inlines, sb);
        }
    }

    // === Global batch scheduler / 全局批处理调度 ===

    // Schedule all three style applications via global batch queue
    // / 通过全局批处理队列调度三种样式应用
    // Replaces per-MTB timers: single timer processes BatchProcessCount MTBs per tick
    // / 替代每MTB定时器:单定时器每tick处理BatchProcessCount个MTB
    // Reduces timer count from N*MaxAttempts to 1, cutting UI thread contention
    // / 定时器数从N*MaxAttempts减到1,降低UI线程争用
    public static void ScheduleApplyAll(
        CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb,
        bool isDark, DispatcherQueue dq,
        bool needCode = true, bool needLink = true, bool needTable = true, int attempt = 0)
    {
        // Try immediately first to minimize flash / 先立即尝试最小化闪烁
        if (needCode && ApplyInlineCodeFont(mtb) > 0) needCode = false;
        if (needLink && ApplyLinkStyle(mtb, isDark) > 0) needLink = false;
        if (needTable && ApplyTableWrapping(mtb) > 0) needTable = false;
        // All done or max attempts reached / 全部完成或达到最大重试
        if ((!needCode && !needLink && !needTable) || attempt >= MaxAttempts) return;

        // Enqueue to global batch queue instead of creating per-MTB timer
        // / 加入全局批处理队列而非创建每MTB定时器
        _pendingApply.Enqueue(new PendingApply
        {
            MtbRef = new WeakReference<CommunityToolkit.WinUI.Controls.MarkdownTextBlock>(mtb),
            IsDark = isDark,
            Dq = dq,
            NeedCode = needCode,
            NeedLink = needLink,
            NeedTable = needTable,
            Attempt = attempt
        });

        // Start batch timer if not already running / 启动批处理定时器(如未运行)
        if (_batchTimer is null)
        {
            _batchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RetryIntervalMs) };
            _batchTimer.Tick += BatchTimer_Tick;
            _batchTimer.Start();
        }
    }

    // Batch timer tick: process up to BatchProcessCount MTBs from queue
    // / 批处理定时器tick:从队列处理最多BatchProcessCount个MTB
    private static void BatchTimer_Tick(object? sender, object e)
    {
        _batchTimer!.Stop();
        _batchTimer = null;

        int processed = 0;
        while (_pendingApply.Count > 0 && processed < BatchProcessCount)
        {
            var item = _pendingApply.Dequeue();
            // MTB may have been GC'd or virtualized away / MTB可能已被GC或虚拟化回收
            if (!item.MtbRef.TryGetTarget(out var mtb)) continue;

            bool needCode = item.NeedCode;
            bool needLink = item.NeedLink;
            bool needTable = item.NeedTable;

            if (needCode && ApplyInlineCodeFont(mtb) > 0) needCode = false;
            if (needLink && ApplyLinkStyle(mtb, item.IsDark) > 0) needLink = false;
            if (needTable && ApplyTableWrapping(mtb) > 0) needTable = false;

            // Re-enqueue if still needs work and has remaining attempts
            // / 仍需处理且有剩余重试次数则重新入队
            if ((needCode || needLink || needTable) && item.Attempt + 1 < MaxAttempts)
            {
                _pendingApply.Enqueue(new PendingApply
                {
                    MtbRef = item.MtbRef,
                    IsDark = item.IsDark,
                    Dq = item.Dq,
                    NeedCode = needCode,
                    NeedLink = needLink,
                    NeedTable = needTable,
                    Attempt = item.Attempt + 1
                });
            }
            processed++;
        }

        // Continue batch processing if queue not empty / 队列非空则继续批处理
        if (_pendingApply.Count > 0)
        {
            _batchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RetryIntervalMs) };
            _batchTimer.Tick += BatchTimer_Tick;
            _batchTimer.Start();
        }
    }

    // Cancel all active timers and clear batch queue (call on page Unload)
    // / 取消所有定时器并清空批处理队列(页面Unload时调用)
    // Prevents timers from running after navigation, which would waste UI thread time
    // / 防止导航后定时器继续运行浪费UI线程时间
    public static void CancelAllTimers()
    {
        _batchTimer?.Stop();
        _batchTimer = null;
        _pendingApply.Clear();
    }

    // === Event handler entry points / 事件处理入口 ===

    // Called on MarkdownTextBlock Loading: configure theme only
    // / MarkdownTextBlock Loading时:仅配置主题
    // Style application deferred to SizeChanged/Text DP event (avoids timer storm)
    // / 样式应用延迟到SizeChanged/Text DP事件(避免定时器风暴)
    public static void OnLoading(CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb, bool isDark, DispatcherQueue dq)
        => ConfigureTheme(mtb, isDark, false, dq);

    // Called on MarkdownTextBlock Loaded: trigger style application via batch queue
    // / MarkdownTextBlock Loaded时:通过批处理队列触发样式应用
    // Earlier than SizeChanged (fires after markdown render), reduces flash
    // / 比SizeChanged更早(在markdown渲染后触发),减少闪烁
    public static void OnLoaded(CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb, bool isDark, DispatcherQueue dq)
        => ScheduleApplyAll(mtb, isDark, dq);

    // Called on MarkdownTextBlock ActualThemeChanged: force re-render
    // / MarkdownTextBlock主题变化时:强制重渲染
    public static void OnThemeChanged(
        CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb,
        bool isDark, DispatcherQueue dq)
        => ConfigureTheme(mtb, isDark, true, dq);

    // Called on MarkdownTextBlock DataContextChanged (recycle): no-op (SizeChanged handles)
    // / 回收重用时:空操作(SizeChanged处理)
    public static void OnDataContextChanged(
        CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb,
        bool isDark, DispatcherQueue dq)
    { }

    // Called on MarkdownTextBlock SizeChanged: debounce then re-apply all styles
    // / SizeChanged时:防抖后重新应用所有样式
    // SizeChanged fires when markdown re-renders after container recycle (content size changes)
    // / 容器回收后markdown重渲染时SizeChanged触发(内容尺寸变化)
    // This is the primary mechanism for catching recycled MTBs
    // where Loaded/DataContextChanged don't fire
    // / 这是捕获回收MTB(Loaded/DataContextChanged不触发)的主要机制
    public static void OnSizeChanged(
        CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb,
        bool isDark, DispatcherQueue dq)
    {
        if (!_sizeChangedTimers.TryGetValue(mtb, out var timer))
        {
            // Use weak ref so timer doesn't keep MTB alive / 用弱引用使计时器不阻止MTB被GC
            var weakRef = new WeakReference<CommunityToolkit.WinUI.Controls.MarkdownTextBlock>(mtb);
            // Debounce increased from 150ms to 200ms to reduce trigger frequency
            // / 防抖从150ms增加到200ms减少触发频率
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (weakRef.TryGetTarget(out var target))
                    ScheduleApplyAll(target, isDark, dq);
            };
            _sizeChangedTimers.AddOrUpdate(mtb, timer);
        }
        timer.Stop();
        timer.Start();
    }

    // Apply all post-render styles to all MarkdownTextBlocks in element's visual tree
    // / 对元素可视化树中所有MarkdownTextBlock应用全部后渲染样式
    // Used after scroll-induced virtualization recycle, when DataContextChanged doesn't fire
    // / 用于滚动虚拟化回收后,DataContextChanged不触发时
    public static void ApplyAllStylesInElement(DependencyObject root, bool isDark, DispatcherQueue dq)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is CommunityToolkit.WinUI.Controls.MarkdownTextBlock mtb)
                ScheduleApplyAll(mtb, isDark, dq);
            ApplyAllStylesInElement(child, isDark, dq);
        }
    }
}
