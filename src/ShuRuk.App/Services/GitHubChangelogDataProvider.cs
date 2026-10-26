using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Views;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ShuRuk.App.Services;

/// <summary>
/// Fetches GitHub releases as changelog data for any repository.
/// Supports optional section filtering by Markdown heading title.
/// / 从GitHub API获取任意仓库的releases作为更新日志数据。支持按Markdown标题过滤章节。
/// </summary>
public sealed class GitHubChangelogDataProvider : IChangelogDataProvider
{
    private static readonly HttpClient _httpClient = new();
    private static bool _initialized;

    public bool IsAvailable => true;

    public async Task<List<ChangelogEntry>> GetEntriesAsync(string repoUrl, string? sectionFilter = null, IProgress<string>? progress = null)
    {
        EnsureInitialized();

        var (owner, repo) = ParseRepoUrl(repoUrl);
        var allEntries = new List<ChangelogEntry>();
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            progress?.Report($"Fetching page {page}...");
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page={pageSize}&page={page}";
            var response = await _httpClient.GetStringAsync(url);
            var releases = JsonDocument.Parse(response).RootElement;

            if (releases.GetArrayLength() == 0) break;

            foreach (var release in releases.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString();
                var publishedAt = release.GetProperty("published_at").GetString();
                var body = release.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : "";
                var isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();

                if (tag is null || publishedAt is null) continue;

                // Apply section filter if specified / 应用章节过滤
                if (!string.IsNullOrEmpty(sectionFilter))
                {
                    var filteredNotes = ExtractSection(body ?? "", sectionFilter);
                    if (filteredNotes is null) continue;

                    allEntries.Add(new ChangelogEntry
                    {
                        Version = tag,
                        Date = DateTime.Parse(publishedAt).ToString("yyyy-MM-dd"),
                        Notes = NormalizeMarkdown(filteredNotes),
                        IsPrerelease = isPrerelease
                    });
                }
                else
                {
                    allEntries.Add(new ChangelogEntry
                    {
                        Version = tag,
                        Date = DateTime.Parse(publishedAt).ToString("yyyy-MM-dd"),
                        Notes = NormalizeMarkdown(body ?? ""),
                        IsPrerelease = isPrerelease
                    });
                }
            }

            if (releases.GetArrayLength() < pageSize) break;
            page++;
        }

        return allEntries;
    }

    // Parse repo URL supporting "owner/repo" or full GitHub URL / 解析仓库地址，支持"owner/repo"或完整GitHub URL
    private static (string owner, string repo) ParseRepoUrl(string repoUrl)
    {
        if (repoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(repoUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
                return (segments[0], segments[1]);
        }

        var parts = repoUrl.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (parts[0], parts[1]);

        throw new ArgumentException($"Invalid repo URL: {repoUrl}. Expected 'owner/repo' or full GitHub URL.");
    }

    // Extract a specific Markdown section by heading title / 按标题提取指定Markdown章节
    private static string? ExtractSection(string markdown, string sectionTitle)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        bool inSection = false;
        int sectionLevel = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                var title = trimmed[level..].Trim();

                if (!inSection)
                {
                    if (title.Equals(sectionTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        sectionLevel = level;
                        sb.AppendLine(line);
                        continue;
                    }
                }
                else
                {
                    // End at same or higher level heading / 同级或更高级标题结束
                    if (level <= sectionLevel) break;
                    sb.AppendLine(line);
                    continue;
                }
            }
            else if (inSection)
            {
                sb.AppendLine(line);
            }
        }

        var result = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    // Collapse multiple blank lines into one, trim leading blanks / 压缩连续空行,去除前导空行
    private static string NormalizeMarkdown(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        bool lastBlank = false;
        bool leading = true;
        foreach (var line in lines)
        {
            bool isBlank = string.IsNullOrWhiteSpace(line);
            // Skip leading blank lines / 跳过前导空行
            if (isBlank && leading) continue;
            leading = false;
            // Collapse consecutive blanks, emit empty string for whitespace-only / 压缩连续空行,空白行输出空字符串
            if (isBlank && lastBlank) continue;
            sb.AppendLine(isBlank ? string.Empty : line);
            lastBlank = isBlank;
        }
        return sb.ToString().TrimEnd();
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShuRuk-Changelog");
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }
}