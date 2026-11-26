using System.Collections.ObjectModel;
using System.ComponentModel;

using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using ShuRuk.Runtime.Helpers;

namespace ShuRuk.Runtime.Models;

/// <summary>
/// Card display mode: Featured (latest 1-2 versions) or HistoryVersion (remaining versions)
/// / 卡片显示模式: Featured(最新1-2个版本)或HistoryVersion(其余版本)
/// </summary>
public enum CardMode
{
    Featured,
    HistoryVersion
}

/// <summary>
/// Changelog entry data / 更新日志条目数据
/// </summary>
public sealed class ChangelogEntry : INotifyPropertyChanged
{
    // Collapsed card max content height (featured cards only)
    // / 折叠态卡片内容最大高度(仅featured卡片)
    public const double CollapsedMaxHeight = 1000;
    // Max visible segments when collapsed (featured cards only)
    // / 折叠时最大可见段数(仅featured卡片)
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

    // Card display mode: Featured or HistoryVersion / 卡片显示模式: Featured或HistoryVersion
    private CardMode _cardMode = CardMode.Featured;
    [JsonIgnore]
    public CardMode CardMode
    {
        get => _cardMode;
        set
        {
            if (_cardMode != value)
            {
                _cardMode = value;
                UpdateVisibleSegments();
                RaisePropertyChanged(nameof(CardMode));
                RaisePropertyChanged(nameof(HistoryLabelVisibility));
                RaisePropertyChanged(nameof(VersionDetailVisibility));
                RaisePropertyChanged(nameof(InlineExpandVisibility));
                RaisePropertyChanged(nameof(BottomButtonVisibility));
                RaisePropertyChanged(nameof(DividerVisibility));
                RaisePropertyChanged(nameof(HistoryTopDividerVisibility));
                RaisePropertyChanged(nameof(CardMaxHeight));
                RaisePropertyChanged(nameof(ExpandButtonVisibility));
                RaisePropertyChanged(nameof(FeaturedContentVisibility));
                RaisePropertyChanged(nameof(HistoryContentVisibility));
                RaisePropertyChanged(nameof(TitleRowVisibility));
            }
        }
    }

    // "History versions" label for collapsed history cards / 折叠history卡片的"历史版本"标签
    [JsonIgnore]
    public string HistoryVersionsLabel => LocalizationHelper.Runtime.GetString("RuntimeChangelogHistoryVersions");

    // Whether this is the first history card (only first shows the label)
    // / 是否为第一个history卡片(仅第一个显示标签)
    private bool _isFirstHistory;
    [JsonIgnore]
    public bool IsFirstHistory
    {
        get => _isFirstHistory;
        set
        {
            if (_isFirstHistory != value)
            {
                _isFirstHistory = value;
                RaisePropertyChanged(nameof(HistoryLabelVisibility));
            }
        }
    }

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
                RaisePropertyChanged(nameof(HistoryLabelVisibility));
                RaisePropertyChanged(nameof(VersionDetailVisibility));
                RaisePropertyChanged(nameof(InlineExpandVisibility));
                RaisePropertyChanged(nameof(BottomButtonVisibility));
                RaisePropertyChanged(nameof(DividerVisibility));
            }
        }
    }

    // Observable segments for incremental add/remove (no container rebuild, preserves scroll)
    // / 可观察段用于增量增删(不重建容器,保持滚动)
    [JsonIgnore]
    public ObservableCollection<ChangelogSegment> VisibleSegments { get; } = [];

    // Initialize VisibleSegments from Segments based on expand state and card mode
    // / 根据展开状态和卡片模式从Segments初始化VisibleSegments
    public void InitializeVisibleSegments()
    {
        VisibleSegments.Clear();
        // History cards collapsed: 0 segments (no markdown loaded)
        // / History卡片折叠:0段(不加载markdown)
        if (_cardMode == CardMode.HistoryVersion && !_isExpanded) return;
        int count = _isExpanded ? Segments.Count : Math.Min(Segments.Count, CollapsedSegmentLimit);
        for (int i = 0; i < count; i++)
            VisibleSegments.Add(Segments[i]);
        RaisePropertyChanged(nameof(HasOverflow));
        RaisePropertyChanged(nameof(ExpandButtonVisibility));
        RaisePropertyChanged(nameof(BottomButtonVisibility));
        RaisePropertyChanged(nameof(FadeOverlayVisibility));
    }

    // Incrementally add/remove segments on expand/collapse toggle / 展开/收起时增量增删段
    private void UpdateVisibleSegments()
    {
        if (_isExpanded)
        {
            for (int i = VisibleSegments.Count; i < Segments.Count; i++)
                VisibleSegments.Add(Segments[i]);
        }
        else
        {
            // History cards collapsed: remove all segments / History卡片折叠:移除所有段
            // Featured cards collapsed: keep CollapsedSegmentLimit segments
            // / Featured卡片折叠:保留CollapsedSegmentLimit段
            int limit = _cardMode == CardMode.HistoryVersion ? 0 : CollapsedSegmentLimit;
            while (VisibleSegments.Count > limit)
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
                RaisePropertyChanged(nameof(BottomButtonVisibility));
                RaisePropertyChanged(nameof(FadeOverlayVisibility));
            }
        }
    }

    // Bottom expand/collapse button visibility:
    // Featured: show when expanded or has overflow
    // HistoryVersion: show only when expanded (to collapse)
    // / 底部展开/收起按钮可见性:
    // Featured:展开或有溢出时显示
    // HistoryVersion:仅展开时显示(用于收起)
    [JsonIgnore]
    public Visibility BottomButtonVisibility =>
        _cardMode == CardMode.HistoryVersion
            ? (_isExpanded ? Visibility.Visible : Visibility.Collapsed)
            : ((_isExpanded || HasOverflow) ? Visibility.Visible : Visibility.Collapsed);

    // Legacy ExpandButtonVisibility maps to BottomButtonVisibility for backward compat
    // / ExpandButtonVisibility映射到BottomButtonVisibility保持兼容
    [JsonIgnore]
    public Visibility ExpandButtonVisibility => BottomButtonVisibility;

    // Inline expand button: removed for history cards (Expander has its own chevron)
    // / 内联展开按钮: history卡片已移除(Expander自带chevron)
    [JsonIgnore]
    public Visibility InlineExpandVisibility => Visibility.Collapsed;

    // Featured content visibility: visible for featured cards only
    // / Featured内容可见性: 仅featured卡片可见
    [JsonIgnore]
    public Visibility FeaturedContentVisibility =>
        _cardMode == CardMode.Featured ? Visibility.Visible : Visibility.Collapsed;

    // History content visibility: visible for history cards only (drives Expander)
    // / History内容可见性: 仅history卡片可见(驱动Expander)
    [JsonIgnore]
    public Visibility HistoryContentVisibility =>
        _cardMode == CardMode.HistoryVersion ? Visibility.Visible : Visibility.Collapsed;

    // "History versions" label visibility: only for first history card when collapsed
    // / "历史版本"标签可见性:仅第一个history卡片折叠时
    [JsonIgnore]
    public Visibility HistoryLabelVisibility =>
        (_cardMode == CardMode.HistoryVersion && !_isExpanded && _isFirstHistory) ? Visibility.Visible : Visibility.Collapsed;

    // Version detail visibility (version/date/prerelease): featured cards or history cards when expanded
    // / 版本详情可见性:featured卡片或history卡片展开时
    [JsonIgnore]
    public Visibility VersionDetailVisibility =>
        (_cardMode == CardMode.Featured || _isExpanded) ? Visibility.Visible : Visibility.Collapsed;

    // Title row visibility: only featured cards show the title row (history cards use Expander Header)
    // / 标题行可见性:仅featured卡片显示标题行(history卡片用Expander Header)
    [JsonIgnore]
    public Visibility TitleRowVisibility =>
        _cardMode == CardMode.Featured ? Visibility.Visible : Visibility.Collapsed;

    // Divider below title: only for featured cards (history cards have title in Expander Header)
    // / 标题下分隔线:仅featured卡片(history卡片标题在Expander Header中)
    [JsonIgnore]
    public Visibility DividerVisibility =>
        _cardMode == CardMode.Featured ? Visibility.Visible : Visibility.Collapsed;

    // Top divider: only between featured and history sections (first history card only)
    // / 顶部分隔线:仅featured与history交界处(仅第一个history卡片)
    [JsonIgnore]
    public Visibility HistoryTopDividerVisibility =>
        (_cardMode == CardMode.HistoryVersion && _isFirstHistory) ? Visibility.Visible : Visibility.Collapsed;

    // Fade gradient overlay: only when collapsed and has overflow (featured only)
    // / 渐变遮罩:仅折叠且有溢出时(仅featured)
    [JsonIgnore]
    public Visibility FadeOverlayVisibility =>
        (!_isExpanded && HasOverflow && _cardMode == CardMode.Featured) ? Visibility.Visible : Visibility.Collapsed;

    // Card max height: featured collapsed = 1000, featured expanded = infinity, history = always infinity
    // / 卡片最大高度:featured折叠=1000,featured展开=无限,history=始终无限
    [JsonIgnore]
    public double CardMaxHeight =>
        _cardMode == CardMode.HistoryVersion ? double.PositiveInfinity :
        (!_isExpanded ? CollapsedMaxHeight : double.PositiveInfinity);

    // Refresh localized properties after language change or late resource load
    // / 语言变更或资源延迟加载后刷新本地化属性
    public void RefreshLocalization()
    {
        RaisePropertyChanged(nameof(HistoryVersionsLabel));
        RaisePropertyChanged(nameof(PrereleaseLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
