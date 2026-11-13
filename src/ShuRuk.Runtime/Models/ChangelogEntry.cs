using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using ShuRuk.Runtime.Helpers;

namespace ShuRuk.Runtime.Models;

/// <summary>
/// Changelog entry data / 更新日志条目数据
/// </summary>
public sealed class ChangelogEntry : INotifyPropertyChanged
{
    // Collapsed card max content height / 折叠态卡片内容最大高度
    public const double CollapsedMaxHeight = 1000;
    // Max visible segments when collapsed / 折叠时最大可见段数
    // Lower count = fewer MarkdownTextBlock instances per card = less UI thread render cost
    // / 更少段数=每卡片更少MarkdownTextBlock实例=更低UI线程渲染开销
    private const int CollapsedSegmentLimit = 2;

    public string Version { get; set; } = string.Empty;
    // GitHub release name (title), falls back to tag if empty / GitHub release 标题,空时回退到tag
    public string ReleaseName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsPrerelease { get; set; }
    // Parsed segments (text + image alternating), runtime only not serialized
    // / 解析后的段(文字与图片交替),运行时 only 不序列化
    [JsonIgnore]
    public List<ChangelogSegment> Segments { get; set; } = [];
    // Visibility for XAML binding / 供XAML绑定的可见性
    public Visibility PrereleaseVisibility => IsPrerelease ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string PrereleaseLabel => LocalizationHelper.Runtime.GetString("RuntimeChangelogPrerelease");

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
                RaisePropertyChanged(nameof(ExpandButtonVisibility));
                RaisePropertyChanged(nameof(FadeOverlayVisibility));
                RaisePropertyChanged(nameof(CardMaxHeight));
            }
        }
    }

    // Observable segments for incremental add/remove (no container rebuild, preserves scroll)
    // / 可观察段用于增量增删(不重建容器,保持滚动)
    [JsonIgnore]
    public ObservableCollection<ChangelogSegment> VisibleSegments { get; } = [];

    // Initialize VisibleSegments from Segments based on expand state
    // / 根据展开状态从Segments初始化VisibleSegments
    public void InitializeVisibleSegments()
    {
        VisibleSegments.Clear();
        int count = _isExpanded ? Segments.Count : Math.Min(Segments.Count, CollapsedSegmentLimit);
        for (int i = 0; i < count; i++)
            VisibleSegments.Add(Segments[i]);
        // Notify overflow-dependent bindings since Segments was populated / Segments填充后通知溢出相关绑定
        RaisePropertyChanged(nameof(HasOverflow));
        RaisePropertyChanged(nameof(ExpandButtonVisibility));
        RaisePropertyChanged(nameof(FadeOverlayVisibility));
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

    // Overflow when hidden segments exist or measured content exceeds collapsed height
    // / 段数超限或运行时测量溢出
    [JsonIgnore]
    public bool HasOverflow => Segments.Count > CollapsedSegmentLimit || MeasuredOverflow;
    // Runtime-measured overflow: ScrollViewer extent exceeds viewport
    // / 运行时测量溢出:ScrollViewer范围超视口
    private bool _measuredOverflow;
    [JsonIgnore]
    public bool MeasuredOverflow
    {
        get => _measuredOverflow;
        set
        {
            if (_measuredOverflow != value)
            {
                _measuredOverflow = value;
                RaisePropertyChanged(nameof(HasOverflow));
                RaisePropertyChanged(nameof(ExpandButtonVisibility));
                RaisePropertyChanged(nameof(FadeOverlayVisibility));
            }
        }
    }
    [JsonIgnore]
    public Visibility ExpandButtonVisibility =>
        (_isExpanded || HasOverflow) ? Visibility.Visible : Visibility.Collapsed;
    // Fade gradient overlay: only when collapsed and has overflow / 渐变遮罩:仅折叠且有溢出时显示
    [JsonIgnore]
    public Visibility FadeOverlayVisibility => !_isExpanded && HasOverflow ? Visibility.Visible : Visibility.Collapsed;

    // Card max height: capped when collapsed, unlimited when expanded
    // / 卡片最大高度:折叠时上限,展开时无限
    [JsonIgnore]
    public double CardMaxHeight => !_isExpanded ? CollapsedMaxHeight : double.PositiveInfinity;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
