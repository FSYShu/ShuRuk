using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ShuRuk.Runtime.Contracts;
using ShuRuk.Runtime.Models;
using ShuRuk.Runtime.Services.Changelog;

namespace ShuRuk.Runtime.ViewModels;

/// <summary>
/// Manages changelog data state: collections, filtering, and background updates.
/// / 管理更新日志数据状态:集合、过滤和后台更新。
/// </summary>
public sealed class ChangelogViewModel
{
    // All entries and currently displayed subset / 全部条目和当前显示的子集
    public List<ChangelogEntry> AllEntries { get; } = [];
    public List<ChangelogEntry> SourceEntries { get; } = [];
    public ObservableCollection<ChangelogEntry> DisplayItems { get; } = [];

    // Number of featured cards: 1 if latest is stable, 2 if latest is prerelease
    // / featured卡片数:最新为stable时1个,最新为prerelease时2个
    public int FeaturedCount { get; private set; } = 1;

    // Filter tag constants / 过滤标签常量
    public const string FilterTagAll = "all";
    public const string FilterTagExcludePrerelease = "exclude-prerelease";

    // Current filter tag / 当前过滤标签
    public string FilterTag { get; set; } = FilterTagAll;

    // Whether to show images in markdown / markdown中是否显示图片
    public bool ShowImages { get; set; } = true;

    // Current repo+filter for cache key / 当前仓库+过滤(缓存键)
    public string? CurrentRepoUrl { get; set; }
    public string? CurrentSectionFilter { get; set; }

    // Batch size for incremental visibility update / 增量可见性更新的批大小
    private const int ImageBatchSize = 5;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly HashSet<DispatcherTimer> _activeTimers = [];

    // Events for View to subscribe / 供View订阅的事件
    public event Action? DataChanged;
    public event Action? ShowCacheInfoBarRequested;

    public ChangelogViewModel(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    // Create a one-shot timer tracked for cleanup / 创建一次性定时器(跟踪以便清理)
    public DispatcherTimer CreateTimer(int intervalMs, Action onTick)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _activeTimers.Add(timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _activeTimers.Remove(timer);
            onTick();
        };
        timer.Start();
        return timer;
    }

    // Stop all active timers (call on page Unloaded) / 停止所有活动定时器(页面卸载时调用)
    public void CleanupTimers()
    {
        foreach (var t in _activeTimers) t.Stop();
        _activeTimers.Clear();
    }

    // Prepare an entry: set ReleaseName fallback, parse segments / 准备条目:设置ReleaseName回退,解析段
    public static void PrepareEntry(ChangelogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ReleaseName)) entry.ReleaseName = entry.Version;
        entry.Segments = ChangelogSegment.Parse(entry.Notes);
        entry.InitializeVisibleSegments();
    }


    // Featured indices: index 0 (latest) + first non-prerelease entry (may be same)
    // / Featured索引: index 0(最新) + 第一个非prerelease条目(可能相同)
    private readonly HashSet<int> _featuredIndices = [];

    // Set CardMode for all entries: featured = latest + first stable, others = rest
    // / 设置所有条目的 CardMode: featured = 最新 + 第一个 stable, history = 其余
    public void AssignCardModes()
    {
        _featuredIndices.Clear();
        if (AllEntries.Count == 0) return;

        // Index 0 is always featured / index 0 始终为featured
        _featuredIndices.Add(0);

        // If index 0 is prerelease, find first non-prerelease as second featured
        // / 如果index 0是prerelease,找第一个非prerelease作为第二个featured
        if (AllEntries[0].IsPrerelease)
        {
            for (int i = 1; i < AllEntries.Count; i++)
            {
                if (!AllEntries[i].IsPrerelease)
                {
                    _featuredIndices.Add(i);
                    break;
                }
            }
        }

        FeaturedCount = _featuredIndices.Count;
        bool firstHistorySet = false;
        for (int i = 0; i < AllEntries.Count; i++)
        {
            var isFeatured = _featuredIndices.Contains(i);
            AllEntries[i].CardMode = isFeatured ? CardMode.Featured : CardMode.HistoryVersion;
            // Only the first history card shows the "History versions" label
            // / 仅第一个 history 卡片显示"历史版本"标签
            if (!isFeatured && !firstHistorySet)
            {
                AllEntries[i].IsFirstHistory = true;
                firstHistorySet = true;
            }
            else
            {
                AllEntries[i].IsFirstHistory = false;
            }
        }

        // Reorder: featured entries first (preserving original order), then history entries
        // / 重排:featured 条目在前(保持原始顺序),history 条目在后
        var featuredList = new List<ChangelogEntry>();
        var otherList = new List<ChangelogEntry>();
        foreach (var entry in AllEntries)
        {
            if (entry.CardMode == CardMode.Featured)
                featuredList.Add(entry);
            else
                otherList.Add(entry);
        }
        AllEntries.Clear();
        AllEntries.AddRange(featuredList);
        AllEntries.AddRange(otherList);
    }

    // Filter by tag (exclude pre-release if chosen) / 按标签过滤
    // Clears display; caller must call LoadAll to load items / 清空显示;调用方须调LoadAll加载条目
    public void ApplyFilter()
    {
        var excludePrerelease = FilterTag == FilterTagExcludePrerelease;
        AllEntries.Clear();
        foreach (var entry in SourceEntries)
            if (!excludePrerelease || !entry.IsPrerelease)
                AllEntries.Add(entry);
        AssignCardModes();
        DisplayItems.Clear();
        DataChanged?.Invoke();
    }

    // Load all items at once: featured cards with content, history cards with titles only
    // / 一次性加载全部条目:featured 卡片有内容,history 卡片仅标题
    // CardMode determines VisibleSegments: featured=limited, history=empty
    // / CardMode 决定 VisibleSegments:featured=有限,history=空
    public void LoadAll()
    {
        foreach (var entry in AllEntries)
        {
            entry.InitializeVisibleSegments();
            DisplayItems.Add(entry);
        }
    }

    // Update image visibility via data binding / 通过数据绑定更新图片可见性
    public async Task UpdateAllImageVisibilityAsync()
    {
        var visibility = ShowImages ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < DisplayItems.Count; i += ImageBatchSize)
        {
            int end = Math.Min(i + ImageBatchSize, DisplayItems.Count);
            for (int j = i; j < end; j++)
                foreach (var seg in DisplayItems[j].Segments)
                    if (seg.IsImage) seg.ImageVisibility = visibility;
            // Yield to UI thread between batches / 批间让出UI线程
            await Task.Yield();
        }
    }

    // Background incremental check: fetch only entries newer than cached latest tag
    // / 后台增量检测:仅获取比缓存最新tag更新的条目
    // Uses ETag conditional request to avoid rate limit cost when no updates
    // / 用ETag条件请求,无更新时不消耗rate limit
    // On failure: keep cached data and show InfoBar (offline indicator)
    // / 失败时:保留缓存数据并显示InfoBar(离线指示)
    public async Task CheckForNewReleasesAsync(
        IChangelogDataProvider provider, string knownLatestTag, string? cachedEtag)
    {
        try
        {
            var (newEntries, firstPageEtag, notModified) = await provider.GetNewEntriesAsync(
                CurrentRepoUrl!, CurrentSectionFilter, knownLatestTag, cachedEtag);
            // 304 Not Modified: server confirmed no new releases, nothing to do
            // / 304未修改:服务端确认无新release,无需操作
            if (notModified) return;
            if (newEntries.Count == 0)
            {
                // No new entries but ETag changed: update cached ETag only
                // / 无新条目但ETag变了:仅更新缓存ETag
                if (firstPageEtag is not null && firstPageEtag != cachedEtag)
                {
                    await ChangelogCacheService.SaveCacheAsync(
                        CurrentRepoUrl!, CurrentSectionFilter, SourceEntries.ToList(), firstPageEtag);
                }
                return;
            }

            // Marshal to UI thread to update collections / 切换到UI线程更新集合
            _dispatcherQueue.TryEnqueue(() =>
            {
                // Parse segments for new entries / 为新条目解析段
                foreach (var entry in newEntries)
                    PrepareEntry(entry);

                // Prepend to source (newest first), respecting current filter
                // / 前置到源(最新在前),尊重当前过滤
                for (int i = newEntries.Count - 1; i >= 0; i--)
                {
                    var entry = newEntries[i];
                    SourceEntries.Insert(0, entry);
                    if (FilterTag == FilterTagAll || !entry.IsPrerelease)
                    {
                        AllEntries.Insert(0, entry);
                        DisplayItems.Insert(0, entry);
                    }
                }

                // Re-assign card modes after new entries / 新条目后重新分配卡片模式
                AssignCardModes();

                // Initialize visible segments for all entries / 初始化所有条目的可见段
                foreach (var entry in AllEntries)
                    entry.InitializeVisibleSegments();

                // Sync DisplayItems order to match AllEntries using Move (preserves scroll position)
                // / 用Move同步DisplayItems顺序以匹配AllEntries(保持滚动位置)
                // Avoids Clear+Add which resets ScrollableHeight to 0 and loses scroll position
                // / 避免Clear+Add导致ScrollableHeight归零丢失滚动位置
                for (int i = 0; i < AllEntries.Count; i++)
                {
                    if (i >= DisplayItems.Count) break;
                    if (ReferenceEquals(AllEntries[i], DisplayItems[i])) continue;
                    for (int j = i + 1; j < DisplayItems.Count; j++)
                    {
                        if (ReferenceEquals(AllEntries[i], DisplayItems[j]))
                        {
                            DisplayItems.Move(j, i);
                            break;
                        }
                    }
                }

                DataChanged?.Invoke();
            });

            // Update cache with merged list and new ETag / 用合并后的列表和新ETag更新缓存
            await ChangelogCacheService.SaveCacheAsync(
                CurrentRepoUrl!, CurrentSectionFilter, SourceEntries.ToList(), firstPageEtag);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changelog incremental check failed: {ex}");
            // Network failure: cached data already shown, inform user via InfoBar
            // / 网络失败:缓存数据已显示,通过InfoBar告知用户
            _dispatcherQueue.TryEnqueue(() => ShowCacheInfoBarRequested?.Invoke());
        }
    }

    // Clear all data collections / 清空所有数据集合
    public void ClearAll()
    {
        SourceEntries.Clear();
        AllEntries.Clear();
        DisplayItems.Clear();
    }

    // Save current UI preferences / 保存当前UI偏好
    public Task SavePrefsAsync()
    {
        return ChangelogCacheService.SavePrefsAsync(new ChangelogPrefs
        {
            ShowImages = ShowImages,
            FilterTag = FilterTag
        });
    }

    // Load cache for current repo+filter / 加载当前仓库+过滤的缓存
    public Task<ChangelogCacheData?> LoadCacheAsync()
    {
        return ChangelogCacheService.LoadCacheAsync(CurrentRepoUrl!, CurrentSectionFilter);
    }

    // Save cache for current repo+filter / 保存当前仓库+过滤的缓存
    public Task SaveCacheAsync(List<ChangelogEntry> entries, string? firstPageEtag = null)
    {
        return ChangelogCacheService.SaveCacheAsync(CurrentRepoUrl!, CurrentSectionFilter, entries, firstPageEtag);
    }
}
