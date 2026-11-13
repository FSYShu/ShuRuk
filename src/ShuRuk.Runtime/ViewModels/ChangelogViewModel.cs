using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ShuRuk.Runtime.Contracts;
using ShuRuk.Runtime.Models;
using ShuRuk.Runtime.Services.Changelog;

namespace ShuRuk.Runtime.ViewModels;

/// <summary>
/// Manages changelog data state: collections, filtering, incremental loading, and background updates.
/// / 管理更新日志数据状态:集合、过滤、增量加载和后台更新。
/// </summary>
public sealed class ChangelogViewModel
{
    // All entries and currently displayed subset / 全部条目和当前显示的子集
    public List<ChangelogEntry> AllEntries { get; } = [];
    public List<ChangelogEntry> SourceEntries { get; } = [];
    public ObservableCollection<ChangelogEntry> DisplayItems { get; } = [];

    // Max items per incremental load (reduced from 4 to 2 for less per-frame render pressure)
    // / 每次增量加载的最大数量(从4减到2降低单帧渲染压力)
    public const int BatchSize = 2;
    // Initial load count: show more items on first load for immediate content
    // / 初始加载数量:首次加载更多条目以立即显示内容
    public const int InitialLoadCount = 8;

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

    // Filter by tag (exclude pre-release if chosen) / 按标签过滤
    public void ApplyFilter()
    {
        var excludePrerelease = FilterTag == FilterTagExcludePrerelease;
        AllEntries.Clear();
        // Inline filter avoids LINQ iterator allocation / 内联过滤避免LINQ迭代器分配
        foreach (var entry in SourceEntries)
            if (!excludePrerelease || !entry.IsPrerelease)
                AllEntries.Add(entry);
        DisplayItems.Clear();
        LoadMore(isInitial: true);
        DataChanged?.Invoke();
    }

    // Load more items: split-frame add spreads MarkdownTextBlock render cost across frames
    // / 加载更多:分帧添加分散MarkdownTextBlock渲染开销
    // defer=true: first half sync, second half deferred 30ms (scroll/initial load)
    // / defer=true:前半同步后半延迟30ms(滚动/初始加载)
    // defer=false: all sync (ScrollToItem needs items immediately)
    // / defer=false:全部同步(ScrollToItem需立即获取条目)
    // isInitial=true: load InitialLoadCount items instead of BatchSize for first display
    // / isInitial=true:首次加载InitialLoadCount条而非BatchSize
    public void LoadMore(bool defer = true, bool isInitial = false)
    {
        var count = isInitial ? InitialLoadCount : BatchSize;
        var toLoad = Math.Min(count, AllEntries.Count - DisplayItems.Count);
        if (toLoad <= 0) return;

        if (defer && toLoad > 1)
        {
            // First half sync for immediate response / 前半同步保持即时响应
            int syncCount = toLoad / 2;
            for (int i = 0; i < syncCount; i++)
                DisplayItems.Add(AllEntries[DisplayItems.Count]);
            // Second half deferred to next frame to avoid blocking UI / 后半延迟到下一帧避免阻塞UI
            int remaining = toLoad - syncCount;
            CreateTimer(30, () =>
            {
                for (int i = 0; i < remaining && DisplayItems.Count < AllEntries.Count; i++)
                    DisplayItems.Add(AllEntries[DisplayItems.Count]);
            });
        }
        else
        {
            for (int i = 0; i < toLoad; i++)
                DisplayItems.Add(AllEntries[DisplayItems.Count]);
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
