using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace ShuRuk.App.Controls;

public sealed partial class SegmentedProgressBar : UserControl
{
    private const double MinSegmentFraction = 0.02;
    private const double AnimationDurationMs = 200;
    private const double AnimationSkipThreshold = 0.5;

    private readonly Dictionary<int, Rectangle> _segmentRects = [];

    // Constructor: initialize component / 构造函数:初始化组件
    public SegmentedProgressBar()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(nameof(Segments), typeof(IList<ProgressSegment>), typeof(SegmentedProgressBar), new PropertyMetadata(null, OnSegmentsChanged));

    public IList<ProgressSegment>? Segments
    {
        get => (IList<ProgressSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SegmentedProgressBar), new PropertyMetadata(100.0, OnSegmentsChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    // Property changed callback: re-render segments / 属性变更回调:重新渲染段
    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SegmentedProgressBar)d).RenderSegments();
    }

    // Track size changed: re-render segments / 轨道尺寸变化:重新渲染段
    private void TrackBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderSegments();
    }

    // Compute segment fractions with min clamp and overflow normalization / 计算段比例,含最小值钳制和溢出归一化
    private static double[] ComputeEffectiveFractions(IList<ProgressSegment> segments, double maximum)
    {
        var fractions = new double[segments.Count];
        if (segments.Count == 0 || maximum <= 0) return fractions;

        for (int i = 0; i < segments.Count; i++)
        {
            fractions[i] = segments[i].Value / maximum;
            if (segments[i].Value > 0 && fractions[i] < MinSegmentFraction)
                fractions[i] = MinSegmentFraction;
        }

        var total = fractions.Sum();
        if (total > 1.0)
        {
            var scale = 1.0 / total;
            for (int i = 0; i < fractions.Length; i++)
                fractions[i] *= scale;
        }

        return fractions;
    }

    // Render all segments: compute layout, update, remove stale / 渲染所有段:计算布局、更新、移除过期
    private void RenderSegments()
    {
        var segments = Segments;
        if (segments is null || segments.Count == 0 || Maximum <= 0)
        {
            AnimateOutAll();
            return;
        }

        var trackWidth = TrackBorder.ActualWidth;
        if (trackWidth <= 0)
        {
            AnimateOutAll();
            return;
        }

        var (targetWidths, targetOffsets) = ComputeTargetLayout(segments, trackWidth);
        UpdateExistingSegments(segments, targetWidths, targetOffsets);
        RemoveStaleSegments(segments.Count);
    }

    // Compute target widths and offsets from fractions / 从比例计算目标宽度和偏移
    private (double[] widths, double[] offsets) ComputeTargetLayout(IList<ProgressSegment> segments, double trackWidth)
    {
        var fractions = ComputeEffectiveFractions(segments, Maximum);
        var targetWidths = new double[segments.Count];
        var targetOffsets = new double[segments.Count];
        var offset = 0.0;

        for (int i = 0; i < segments.Count; i++)
        {
            targetWidths[i] = fractions[i] * trackWidth;
            targetOffsets[i] = offset;
            offset += targetWidths[i];
        }

        return (targetWidths, targetOffsets);
    }

    // Update or create segment rectangles to match targets / 更新或创建段矩形以匹配目标
    private void UpdateExistingSegments(IList<ProgressSegment> segments, double[] targetWidths, double[] targetOffsets)
    {
        var trackHeight = TrackBorder.ActualHeight;
        for (int i = 0; i < segments.Count; i++)
        {
            if (_segmentRects.TryGetValue(i, out var rect))
            {
                rect.Fill = segments[i].Brush;
                AnimateProperty(rect, "Width", targetWidths[i]);
                AnimateProperty(rect, "(Canvas.Left)", targetOffsets[i]);
            }
            else
            {
                rect = new Rectangle
                {
                    Fill = segments[i].Brush,
                    Width = 0,
                    Height = trackHeight,
                };
                Canvas.SetLeft(rect, targetOffsets[i]);
                SegmentsCanvas.Children.Add(rect);
                _segmentRects[i] = rect;
                AnimateProperty(rect, "Width", targetWidths[i]);
            }
        }
    }

    // Animate out and remove segments beyond active count / 动画移除超出活跃数量的段
    private void RemoveStaleSegments(int activeCount)
    {
        var staleKeys = _segmentRects.Keys.Where(k => k >= activeCount).ToList();
        foreach (var key in staleKeys)
        {
            if (_segmentRects.TryGetValue(key, out var rect))
            {
                var capturedRect = rect;
                var capturedKey = key;
                AnimateProperty(rect, "Width", 0, () =>
                {
                    SegmentsCanvas.Children.Remove(capturedRect);
                    _segmentRects.Remove(capturedKey);
                });
            }
        }
    }

    // Animate all segments out and clear dictionary / 动画移除所有段并清空字典
    private void AnimateOutAll()
    {
        if (_segmentRects.Count == 0) return;

        foreach (var (key, rect) in _segmentRects.ToList())
        {
            var capturedRect = rect;
            AnimateProperty(rect, "Width", 0, () =>
            {
                SegmentsCanvas.Children.Remove(capturedRect);
            });
        }

        _segmentRects.Clear();
    }

    // Animate a property to target value, skip if delta is small / 动画属性到目标值,差值过小则跳过
    private void AnimateProperty(DependencyObject target, string propertyPath, double toValue, Action? onComplete = null)
    {
        double currentValue = string.Equals(propertyPath, "Width", StringComparison.Ordinal)
            ? ((FrameworkElement)target).Width
            : Canvas.GetLeft((UIElement)target);

        if (double.IsNaN(currentValue))
            currentValue = 0;

        if (Math.Abs(currentValue - toValue) < AnimationSkipThreshold)
        {
            if (string.Equals(propertyPath, "Width", StringComparison.Ordinal))
                ((FrameworkElement)target).Width = toValue;
            else
                Canvas.SetLeft((UIElement)target, toValue);
            onComplete?.Invoke();
            return;
        }

        var anim = new DoubleAnimation
        {
            From = currentValue,
            To = toValue,
            Duration = TimeSpan.FromMilliseconds(AnimationDurationMs),
            EasingFunction = new QuadraticEase(),
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, propertyPath);

        var sb = new Storyboard();
        sb.Children.Add(anim);

        if (onComplete != null)
            sb.Completed += (_, _) => onComplete();

        sb.Begin();
    }
}

public class ProgressSegment
{
    public double Value { get; set; }
    public Brush Brush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
}
