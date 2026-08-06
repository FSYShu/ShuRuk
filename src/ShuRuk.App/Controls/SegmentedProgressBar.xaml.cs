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

    private readonly Dictionary<int, Rectangle> _segmentRects = [];

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

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SegmentedProgressBar)d).RenderSegments();
    }

    private void TrackBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderSegments();
    }

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

    private void RenderSegments()
    {
        var segments = Segments;
        if (segments is null || segments.Count == 0 || Maximum <= 0)
        {
            AnimateOutAll();
            return;
        }

        var trackWidth = TrackBorder.ActualWidth;
        if (trackWidth <= 0) return;

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

        var currentKeys = new HashSet<int>(_segmentRects.Keys);

        for (int i = 0; i < segments.Count; i++)
        {

            if (_segmentRects.TryGetValue(i, out var rect))
            {
                rect.Fill = segments[i].Brush;
                AnimateProperty(rect, "Width", targetWidths[i]);
                AnimateProperty(rect, "(Canvas.Left)", targetOffsets[i]);
                currentKeys.Remove(i);
            }
            else
            {
                rect = new Rectangle
                {
                    Fill = segments[i].Brush,
                    Width = 0,
                    Height = TrackBorder.ActualHeight,
                };
                Canvas.SetLeft(rect, targetOffsets[i]);
                SegmentsCanvas.Children.Add(rect);
                _segmentRects[i] = rect;
                AnimateProperty(rect, "Width", targetWidths[i]);
            }
        }

        foreach (var key in currentKeys)
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

    private void AnimateProperty(DependencyObject target, string propertyPath, double toValue, Action? onComplete = null)
    {
        double currentValue = propertyPath == "Width"
            ? ((FrameworkElement)target).Width
            : Canvas.GetLeft((UIElement)target);

        if (double.IsNaN(currentValue))
            currentValue = 0;

        if (Math.Abs(currentValue - toValue) < 0.5)
        {
            if (propertyPath == "Width")
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
