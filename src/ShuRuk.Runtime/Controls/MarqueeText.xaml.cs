using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace ShuRuk.Runtime.Controls;

// Seamless scrolling marquee using dual TextBlocks.
// When text exceeds ClipWidth, TB1 scrolls left while TB2 enters from right;
// at loop boundary they swap invisibly (identical content, no visible jump).
// 双TextBlock无缝跑马灯:文本超出ClipWidth时TB1左滚TB2从右进入,循环时互换不可见.
public sealed partial class MarqueeText : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeText),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty ClipWidthProperty =
        DependencyProperty.Register(nameof(ClipWidth), typeof(double), typeof(MarqueeText),
            new PropertyMetadata(double.NaN, OnClipChanged));

    public static readonly DependencyProperty ClipHeightProperty =
        DependencyProperty.Register(nameof(ClipHeight), typeof(double), typeof(MarqueeText),
            new PropertyMetadata(24.0, OnClipChanged));

    public static readonly DependencyProperty TextBlockStyleProperty =
        DependencyProperty.Register(nameof(TextBlockStyle), typeof(Style), typeof(MarqueeText),
            new PropertyMetadata(null, OnTextBlockStyleChanged));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double ClipWidth
    {
        get => (double)GetValue(ClipWidthProperty);
        set => SetValue(ClipWidthProperty, value);
    }
    public double ClipHeight
    {
        get => (double)GetValue(ClipHeightProperty);
        set => SetValue(ClipHeightProperty, value);
    }
    public Style? TextBlockStyle
    {
        get => (Style?)GetValue(TextBlockStyleProperty);
        set => SetValue(TextBlockStyleProperty, value);
    }

    // Marquee tuning parameters / 跑马灯调节参数
    private const double ScrollSpeed = 30; // px per second / 每秒像素
    private const double PauseTime = 1.5; // Hold at start before scrolling / 开头停顿秒数
    private const double Gap = 40; // Spacing between the two text blocks / 两个文本块间距

    private TextBlock? _clone;
    private Storyboard? _storyboard;
    // Skip initial pause on next StartMarquee (for smooth overlay transitions)
    // / 下次StartMarquee跳过初始停顿(用于覆盖层平滑切换)
    private bool _skipPause;
    // Transfer scroll progress (0-1) from old overlay to new overlay for seamless transition
    // / 从旧覆盖层传递滚动进度(0-1)到新覆盖层实现无缝切换
    private double _transferProgress;
    // Track current marquee text width to avoid redundant restarts
    // / 追踪当前跑马灯文本宽度避免冗余重启
    private double _currentMarqueeWidth;
    // Pending layout-based marquee update (set when text changes or Loaded fires)
    // / 待布局更新跑马灯(文本变更或Loaded时设置)
    private bool _pendingLayoutUpdate;

    public MarqueeText()
    {
        InitializeComponent();
        ApplyText();
        UpdateClip();
        Loaded += (_, _) =>
        {
            _pendingLayoutUpdate = true;
            UpdateMarquee();
        };
        SizeChanged += (_, _) => UpdateClip();
        // LayoutUpdated fires after any layout pass; use it to catch ActualWidth becoming ready
        // / LayoutUpdated在布局后触发,用于捕获ActualWidth就绪
        LayoutUpdated += (_, _) =>
        {
            if (!_pendingLayoutUpdate) return;
            if (MainText.ActualWidth <= 0) return;
            _pendingLayoutUpdate = false;
            UpdateMarquee();
        };
    }

    // Force re-evaluation of marquee after layout / 布局后强制重新评估跑马灯
    public void Refresh()
    {
        _pendingLayoutUpdate = true;
        UpdateMarquee();
    }

    // Whether the marquee is actively running (scrolling or in pause phase)
    // / 跑马灯是否正在运行(滚动中或停顿阶段)
    public bool IsScrolling => _storyboard is not null;

    // Current scroll progress (0 = at start, 1 = fully scrolled left)
    // / 当前滚动进度(0=起始,1=完全滚出)
    public double GetScrollProgress()
    {
        if (_storyboard is null || _currentMarqueeWidth <= 0) return 0;
        double totalDistance = _currentMarqueeWidth + Gap;
        if (totalDistance <= 0) return 0;
        return Math.Clamp(-MainTransform.X / totalDistance, 0, 1);
    }

    // Restart from a specific scroll progress, skipping initial pause
    // / 从指定滚动进度重启,跳过初始停顿
    public void RestartFromProgress(double progress)
    {
        _skipPause = true;
        _transferProgress = Math.Clamp(progress, 0, 1);
        StopMarquee();
        _currentMarqueeWidth = 0;
        _pendingLayoutUpdate = true;
        UpdateMarquee();
    }

    // Restart marquee without the initial pause, for smooth overlay transitions
    // / 无初始停顿重启跑马灯,用于覆盖层平滑切换
    public void RestartWithoutPause()
    {
        _skipPause = true;
        StopMarquee();
        _currentMarqueeWidth = 0;
        _pendingLayoutUpdate = true;
        UpdateMarquee();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeText mt)
        {
            mt.ApplyText();
            // Force stop + reset to ensure clone gets recreated with new text
            // / 强制停止+重置确保clone用新文本重建
            mt.StopMarquee();
            mt._currentMarqueeWidth = 0;
            mt._pendingLayoutUpdate = true;
            mt.UpdateMarquee();
        }
    }


    private static void OnClipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeText mt) mt.UpdateClip();
    }

    private static void OnTextBlockStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeText mt)
        {
            mt.MainText.Style = mt.TextBlockStyle;
            mt.UpdateMarquee();
        }
    }

    private void ApplyText()
    {
        MainText.Text = Text ?? string.Empty;
    }

    // Effective clip width: explicit ClipWidth or ActualWidth / 有效裁切宽度:显式ClipWidth或ActualWidth
    private double EffectiveClipWidth =>
        !double.IsNaN(ClipWidth) && ClipWidth > 0 ? ClipWidth : ActualWidth;

    private void UpdateClip()
    {
        double w = EffectiveClipWidth;
        double h = !double.IsNaN(ClipHeight) && ClipHeight > 0 ? ClipHeight : ActualHeight;
        if (w <= 0 || h <= 0) return;
        RootGrid.Width = w;
        RootGrid.Height = h;
        ClipGeometry.Rect = new Rect(0, 0, w, h);
        UpdateMarquee();
    }

    private void MainText_Loaded(object sender, RoutedEventArgs e) => UpdateMarquee();

    private void MainText_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarquee();

    private void UpdateMarquee()
    {
        if (string.IsNullOrEmpty(MainText.Text))
        {
            StopMarquee();
            _currentMarqueeWidth = 0;
            return;
        }

        // Use ActualWidth, fallback to detached probe / 用ActualWidth,后备为独立探测
        double textWidth = MainText.ActualWidth;
        if (textWidth <= 0)
        {
            var probe = new TextBlock
            {
                Text = MainText.Text,
                FontFamily = MainText.FontFamily,
                FontSize = MainText.FontSize,
                FontWeight = MainText.FontWeight
            };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            textWidth = probe.DesiredSize.Width;
        }
        if (textWidth <= 0) return; // Not laid out yet / 尚未布局

        if (textWidth <= EffectiveClipWidth)
        {
            // Text fits, no marquee or tooltip needed / 文本适配,无需跑马灯和tooltip
            if (_storyboard is not null) { StopMarquee(); _currentMarqueeWidth = 0; }
            ToolTipService.SetToolTip(this, null);
            return;
        }

        // Skip restart if marquee already running with same width (avoids resetting pause timer)
        // / 跑马灯已以相同宽度运行则跳过(避免重置停顿计时器)
        if (_storyboard is not null && Math.Abs(_currentMarqueeWidth - textWidth) < 0.5)
            return;

        StopMarquee();
        // Show full text tooltip when truncated / 截断时显示完整文本tooltip
        ToolTipService.SetToolTip(this, Text);
        _currentMarqueeWidth = textWidth;
        StartMarquee(textWidth);
    }

    private void StopMarquee()
    {
        if (_storyboard is not null)
        {
            _storyboard.Stop();
            _storyboard = null;
        }
        MainTransform.X = 0;
        if (_clone?.Parent is Canvas c) c.Children.Remove(_clone);
        _clone = null;
    }

    private void StartMarquee(double textWidth)
    {
        if (MainText.Parent is not Canvas canvas) return;

        double totalDistance = textWidth + Gap;
        double scrollTime = totalDistance / ScrollSpeed;
        double totalTime = PauseTime + scrollTime;

        // Capture skipPause before resetting (field is reset below, but keyframe
        // generation needs the original value)
        // / 重置前捕获skipPause(字段在下方重置,但关键帧生成需要原始值)
        bool skipPause = _skipPause;
        double effectiveTotalTime = skipPause ? scrollTime : totalTime;

        // Reset one-shot flags / 重置一次性标志
        _skipPause = false;

        // Create clone TextBlock in the same Canvas / 在同一Canvas中创建克隆文本块
        _clone = new TextBlock
        {
            Text = MainText.Text,
            FontFamily = MainText.FontFamily,
            FontSize = MainText.FontSize,
            FontWeight = MainText.FontWeight,
            Foreground = MainText.Foreground,
            Style = MainText.Style
        };
        Canvas.SetTop(_clone, Canvas.GetTop(MainText));
        var tt2 = new TranslateTransform { X = totalDistance };
        _clone.RenderTransform = tt2;
        canvas.Children.Add(_clone);

        // TB1: hold at 0, then scroll left to -totalDistance
        // When skipPause, start scrolling immediately (no hold at 0)
        // / TB1:停顿后左滚到-totalDistance;跳过停顿时立即开始滚动
        var anim1 = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(effectiveTotalTime)
        };
        if (!skipPause)
        {
            anim1.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                Value = 0,
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))
            });
            anim1.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                Value = 0,
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PauseTime))
            });
        }
        anim1.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            Value = -totalDistance,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(effectiveTotalTime))
        });
        Storyboard.SetTarget(anim1, MainTransform);
        Storyboard.SetTargetProperty(anim1, "X");

        // TB2: hold at totalDistance (off-screen right), then scroll left to 0
        // When skipPause, start scrolling immediately
        // / TB2:停在屏外右后左滚到0;跳过停顿时立即开始滚动
        var anim2 = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(effectiveTotalTime)
        };
        if (!skipPause)
        {
            anim2.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                Value = totalDistance,
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))
            });
            anim2.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                Value = totalDistance,
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PauseTime))
            });
        }
        anim2.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            Value = 0,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(effectiveTotalTime))
        });
        Storyboard.SetTarget(anim2, tt2);
        Storyboard.SetTargetProperty(anim2, "X");

        _storyboard = new Storyboard();
        _storyboard.Children.Add(anim1);
        _storyboard.Children.Add(anim2);

        // Set initial transform positions from transfer progress (more reliable than Storyboard.Seek)
        // / 从传递进度设置初始transform位置(比Storyboard.Seek更可靠)
        if (_transferProgress > 0)
        {
            MainTransform.X = -_transferProgress * totalDistance;
            tt2.X = (1 - _transferProgress) * totalDistance;
        }
        _transferProgress = 0;

        _storyboard.Begin();
    }
}
