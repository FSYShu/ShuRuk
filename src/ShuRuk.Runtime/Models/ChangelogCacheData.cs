namespace ShuRuk.Runtime.Models;

/// <summary>
/// Persisted changelog cache for a repo+filter combination / 仓库+过滤组合的持久化更新日志缓存
/// </summary>
public sealed class ChangelogCacheData
{
    public string RepoUrl { get; set; } = string.Empty;
    public string? SectionFilter { get; set; }
    public DateTime LastUpdated { get; set; }
    public string LatestTag { get; set; } = string.Empty;
    // First-page ETag for GitHub conditional requests (If-None-Match)
    // / 第一页ETag用于GitHub条件请求(If-None-Match)
    public string? FirstPageEtag { get; set; }
    public List<ChangelogEntry> Entries { get; set; } = [];
}
