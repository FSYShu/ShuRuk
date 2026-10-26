using ShuRuk.Runtime.Views;

namespace ShuRuk.Runtime.Abstractions.Interfaces;

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
    /// <param name="repoUrl">Repository as "owner/repo" or full GitHub URL / 仓库地址，"owner/repo"或完整GitHub URL</param>
    /// <param name="sectionFilter">Optional Markdown heading title to filter; only releases containing this section are kept / 可选Markdown标题过滤，仅保留包含该章节的release</param>
    Task<List<ChangelogEntry>> GetEntriesAsync(string repoUrl, string? sectionFilter = null, IProgress<string>? progress = null);

    /// <summary>
    /// Fetch only changelog entries newer than the given tag, for incremental loading.
    /// Stops fetching pages once a release matching sinceTag is encountered.
    /// / 仅获取比指定tag更新的更新日志条目（增量加载）。遇到匹配sinceTag的release即停止。
    /// </summary>
    /// <param name="sinceTag">The latest tag already cached; entries before/at this tag are skipped / 已缓存的最新tag，此tag及之前的条目被跳过</param>
    Task<List<ChangelogEntry>> GetNewEntriesAsync(string repoUrl, string? sectionFilter, string sinceTag, IProgress<string>? progress = null);
}