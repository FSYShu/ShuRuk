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
            new PropertyMetadata(100.0, OnClipChanged));

    public static readonly DependencyProperty ClipHeightProperty =
        DependencyProperty.Register(nameof(ClipHeight), typeof(double), typeof(MarqueeText),
            new PropertyMetadata(24.0, OnClipChanged));

    public static readonly DependencyProperty TextBlockStyleProperty =
        DependencyProperty.Register(nameof(TextBlockStyle), typeof(Style), typeof(MarqueeText),
            new PropertyMetadata(null, OnTextBlockStyleChanged));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double ClipWidth { get => (double)GetValue(ClipWidthProperty); set => SetValue(ClipWidthProperty, value); }
    public double ClipHeight { get => (double)GetValue(ClipHeightProperty); set => SetValue(ClipHeightProperty, value); }
    public Style? TextBlockStyle { get => (Style?)GetValue(TextBlockStyleProperty); set => SetValue(TextBlockStyleProperty, value); }

    // Marquee tuning parameters / 跑马灯调节参数
    private const double ScrollSpeed = 30; // px per second / 每秒像素
    private const double PauseTime = 1.5; // Hold at start before scrolling / 开头停顿秒数
    private const double Gap = 40; // Spacing between the two text blocks / 两个文本块间距

    private TextBlock? _clone;
    private Storyboard? _storyboard;
    // Track current marquee text width to avoid redundant restarts / 追踪当前跑马灯文本宽度避免冗余重启
    private double _currentMarqueeWidth;
    // Pending layout-based marquee update (set when text changes or Loaded fires) / 待布局更新跑马灯(文本变更或Loaded时设置)
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
        SizeChanged += (_, _) => UpdateMarquee();
        // LayoutUpdated fires after any layout pass; use it to catch ActualWidth becoming ready / LayoutUpdated在布局后触发,用于捕获ActualWidth就绪
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

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeText mt)
        {
            mt.ApplyText();
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

    private void UpdateClip()
    {
        RootGrid.Width = ClipWidth;
        RootGrid.Height = ClipHeight;
        ClipGeometry.Rect = new Rect(0, 0, ClipWidth, ClipHeight);
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

        if (textWidth <= ClipWidth)
        {
            // Text fits, no marquee or tooltip needed / 文本适配,无需跑马灯和tooltip
            if (_storyboard is not null) { StopMarquee(); _currentMarqueeWidth = 0; }
            ToolTipService.SetToolTip(this, null);
            return;
        }

        // Skip restart if marquee already running with same width (avoids resetting pause timer) / 跑马灯已以相同宽度运行则跳过(避免重置停顿计时器)
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

        // Create clone TextBlock in the same Canvas / 在同一Canvas中创建克隆文本块
        _clone = new TextBlock
        {
            Text = MainText.Text,
            FontSize = MainText.FontSize,
            FontWeight = MainText.FontWeight,
            Foreground = MainText.Foreground,
            Style = MainText.Style
        };
        Canvas.SetTop(_clone, Canvas.GetTop(MainText));
        var tt2 = new TranslateTransform { X = totalDistance };
        _clone.RenderTransform = tt2;
        canvas.Children.Add(_clone);

        // TB1: hold at 0, then scroll left to -totalDistance / TB1:停顿后左滚到-totalDistance
        var anim1 = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(totalTime)
        };
        anim1.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0)) });
        anim1.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PauseTime)) });
        anim1.KeyFrames.Add(new LinearDoubleKeyFrame { Value = -totalDistance, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(totalTime)) });
        Storyboard.SetTarget(anim1, MainTransform);
        Storyboard.SetTargetProperty(anim1, "X");

        // TB2: hold at totalDistance (off-screen right), then scroll left to 0 / TB2:停在屏外右后左滚到0
        var anim2 = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(totalTime)
        };
        anim2.KeyFrames.Add(new LinearDoubleKeyFrame { Value = totalDistance, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0)) });
        anim2.KeyFrames.Add(new LinearDoubleKeyFrame { Value = totalDistance, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PauseTime)) });
        anim2.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(totalTime)) });
        Storyboard.SetTarget(anim2, tt2);
        Storyboard.SetTargetProperty(anim2, "X");

        _storyboard = new Storyboard();
        _storyboard.Children.Add(anim1);
        _storyboard.Children.Add(anim2);
        _storyboard.Begin();
    }
}