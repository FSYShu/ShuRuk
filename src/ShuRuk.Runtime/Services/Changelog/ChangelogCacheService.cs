using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShuRuk.Runtime.Models;

namespace ShuRuk.Runtime.Services.Changelog;

/// <summary>
/// Handles changelog cache file I/O: load/save entries and UI preferences.
/// / 处理更新日志缓存文件读写:加载/保存条目和UI偏好。
/// </summary>
public static class ChangelogCacheService
{
    // Cache directory under %LOCALAPPDATA%\ShuRuk\changelog_cache
    // / %LOCALAPPDATA%\ShuRuk\changelog_cache下的缓存目录
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShuRuk", "changelog_cache");
    private static readonly string PrefsPath = Path.Combine(CacheDir, "prefs.json");
    // JSON options: indented for debuggability / JSON选项:缩进便于调试
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    // Get cache file path for a repo+filter combination / 获取仓库+过滤组合的缓存文件路径
    public static string GetCachePath(string repoUrl, string? sectionFilter)
    {
        var key = $"{repoUrl}|{sectionFilter ?? ""}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(CacheDir, $"{hash}.json");
    }

    // Load cached entries if repo+filter matches / 加载缓存条目(仅当仓库+过滤匹配)
    public static async Task<ChangelogCacheData?> LoadCacheAsync(string repoUrl, string? sectionFilter)
    {
        try
        {
            var path = GetCachePath(repoUrl, sectionFilter);
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path);
            // Deserialize + pre-parse segments on background thread to avoid blocking UI
            // / 后台反序列化+预解析段避免阻塞UI线程
            // OnEntriesChanged skips entries with Segments already populated
            // / OnEntriesChanged跳过已解析段的条目
            var cache = await Task.Run(() =>
            {
                var c = JsonSerializer.Deserialize<ChangelogCacheData>(json, JsonOpts);
                if (c is not null)
                {
                    foreach (var entry in c.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.ReleaseName)) entry.ReleaseName = entry.Version;
                        if (entry.Segments is null || entry.Segments.Count == 0)
                        {
                            entry.Segments = ChangelogSegment.Parse(entry.Notes);
                            entry.InitializeVisibleSegments();
                        }
                    }
                }
                return c;
            });
            if (cache is null) return null;
            if (cache.RepoUrl != repoUrl || cache.SectionFilter != sectionFilter) return null;
            // Invalidate old cache without ReleaseName field / 旧缓存无ReleaseName字段则失效
            if (cache.Entries.Count > 0 && string.IsNullOrWhiteSpace(cache.Entries[0].ReleaseName)) return null;
            return cache;
        }
        catch { return null; }
    }

    // Save entries to cache file / 保存条目到缓存文件
    public static async Task SaveCacheAsync(string repoUrl, string? sectionFilter,
        List<ChangelogEntry> entries, string? firstPageEtag = null)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var cache = new ChangelogCacheData
            {
                RepoUrl = repoUrl,
                SectionFilter = sectionFilter,
                LastUpdated = DateTime.UtcNow,
                LatestTag = entries.Count > 0 ? entries[0].Version : "",
                FirstPageEtag = firstPageEtag,
                Entries = entries
            };
            var path = GetCachePath(repoUrl, sectionFilter);
            var json = JsonSerializer.Serialize(cache, JsonOpts);
            await File.WriteAllTextAsync(path, json);
        }
        catch { }
    }

    // Load UI preferences (show images, filter tag) / 加载UI偏好(显示图片,过滤标签)
    public static async Task<ChangelogPrefs> LoadPrefsAsync()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return new ChangelogPrefs();
            var json = await File.ReadAllTextAsync(PrefsPath);
            return JsonSerializer.Deserialize<ChangelogPrefs>(json, JsonOpts) ?? new ChangelogPrefs();
        }
        catch { return new ChangelogPrefs(); }
    }

    // Save UI preferences / 保存UI偏好
    public static async Task SavePrefsAsync(ChangelogPrefs prefs)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var json = JsonSerializer.Serialize(prefs, JsonOpts);
            await File.WriteAllTextAsync(PrefsPath, json);
        }
        catch { }
    }

    // Delete cache file for a repo+filter combination / 删除仓库+过滤组合的缓存文件
    public static void DeleteCache(string repoUrl, string? sectionFilter)
    {
        try
        {
            var path = GetCachePath(repoUrl, sectionFilter);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    // Nav debug log to %LOCALAPPDATA%\ShuRuk\nav_log.txt / 导航调试日志
    // Nav debug log toggle: set true to enable file logging / 导航调试日志开关:设true启用文件日志
    public static readonly bool NavLogEnabled = true;
    public static void NavLog(string msg)
    {
        if (!NavLogEnabled) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuRuk");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "nav_log.txt"),
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }
}
