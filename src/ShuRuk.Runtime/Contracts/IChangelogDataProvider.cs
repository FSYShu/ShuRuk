using ShuRuk.Runtime.Models;

namespace ShuRuk.Runtime.Contracts;

/// <summary>
/// Provides changelog data from an external source (e.g. GitHub API).
/// / 从外部源（如GitHub API）提供更新日志数据。
/// </summary>
public interface IChangelogDataProvider
{
    bool IsAvailable { get; }

    /// <summary>
    /// Fetch changelog entries from a GitHub repository.
    /// / 从GitHub仓库获取更新日志条目。
    /// </summary>
    /// <param name="repoUrl">Repository as "owner/repo" or full GitHub URL
    /// / 仓库地址，"owner/repo"或完整GitHub URL</param>
    /// <param name="sectionFilter">Optional Markdown heading title to filter;
    /// only releases containing this section are kept
    /// / 可选Markdown标题过滤，仅保留包含该章节的release</param>
    Task<List<ChangelogEntry>> GetEntriesAsync(string repoUrl, string? sectionFilter = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// Fetch changelog entries in batches, invoking onBatch for each page as it arrives.
    /// Enables progressive display for repositories with many releases.
    /// / 分批获取更新日志条目,每获取一页即回调onBatch。支持多release仓库的渐进式显示。
    /// </summary>
    Task GetEntriesStreamingAsync(string repoUrl, string? sectionFilter,
        Action<List<ChangelogEntry>> onBatch, IProgress<string>? progress = null);

    /// <summary>
    /// Fetch only changelog entries newer than the given tag, for incremental loading.
    /// Stops fetching pages once a release matching sinceTag is encountered.
    /// Supports ETag conditional requests: pass a cached first-page ETag via ifNoneMatch;
    /// when the server returns 304 Not Modified, notModified=true and no entries are returned.
    /// / 仅获取比指定tag更新的更新日志条目（增量加载）。
    /// / 遇到匹配sinceTag的release即停止。
    /// 支持ETag条件请求:通过ifNoneMatch传入缓存的第一页ETag,
    /// 服务端返回304 Not Modified时notModified=true且无条目返回。
    /// </summary>
    /// <param name="sinceTag">The latest tag already cached;
    /// entries before/at this tag are skipped
    /// / 已缓存的最新tag，此tag及之前的条目被跳过</param>
    /// <param name="ifNoneMatch">Cached first-page ETag for conditional request;
    /// null to skip / 缓存的第一页ETag用于条件请求,null跳过</param>
    /// <returns>newEntries: entries newer than sinceTag;
    /// firstPageEtag: ETag of first page to cache;
    /// notModified: server returned 304
    /// / newEntries:比sinceTag新的条目;
    /// firstPageEtag:第一页ETag供缓存;
    /// notModified:服务端返回304</returns>
    Task<(List<ChangelogEntry> newEntries, string? firstPageEtag, bool notModified)> GetNewEntriesAsync(
        string repoUrl, string? sectionFilter, string sinceTag,
        string? ifNoneMatch = null, IProgress<string>? progress = null);
}
