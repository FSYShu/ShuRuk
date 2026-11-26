using ShuRuk.Runtime.Contracts;
using ShuRuk.Runtime.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ShuRuk.Runtime.Services.Changelog;

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

    public async Task<List<ChangelogEntry>> GetEntriesAsync(string repoUrl,
        string? sectionFilter = null, IProgress<string>? progress = null)
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
                var entry = BuildEntry(release, sectionFilter);
                if (entry is not null) allEntries.Add(entry);
            }

            if (releases.GetArrayLength() < pageSize) break;
            page++;
        }

        return allEntries;
    }

    // Streaming fetch: invoke onBatch per page for progressive display
    // / 流式获取:每页回调onBatch实现渐进式显示
    public async Task GetEntriesStreamingAsync(string repoUrl, string? sectionFilter,
        Action<List<ChangelogEntry>> onBatch, IProgress<string>? progress = null)
    {
        EnsureInitialized();

        var (owner, repo) = ParseRepoUrl(repoUrl);
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            progress?.Report($"Fetching page {page}...");
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page={pageSize}&page={page}";
            var response = await _httpClient.GetStringAsync(url);
            var releases = JsonDocument.Parse(response).RootElement;

            if (releases.GetArrayLength() == 0) break;

            var batch = new List<ChangelogEntry>();
            foreach (var release in releases.EnumerateArray())
            {
                var entry = BuildEntry(release, sectionFilter);
                if (entry is not null) batch.Add(entry);
            }

            if (batch.Count > 0) onBatch(batch);

            if (releases.GetArrayLength() < pageSize) break;
            page++;
        }
    }

    // Fetch only entries newer than sinceTag for incremental loading / 增量加载:仅获取比sinceTag更新的条目
    // First page uses ETag conditional request (If-None-Match); 304 means no new releases
    // / 第一页用ETag条件请求(If-None-Match),304表示无新release
    public async Task<(List<ChangelogEntry> newEntries, string? firstPageEtag, bool notModified)> GetNewEntriesAsync(
        string repoUrl, string? sectionFilter, string sinceTag,
        string? ifNoneMatch = null, IProgress<string>? progress = null)
    {
        EnsureInitialized();

        var (owner, repo) = ParseRepoUrl(repoUrl);
        var newEntries = new List<ChangelogEntry>();
        int page = 1;
        const int pageSize = 100;
        string? firstPageEtag = null;

        while (true)
        {
            progress?.Report($"Checking page {page} for new releases...");
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page={pageSize}&page={page}";

            // First page: use conditional request with cached ETag / 第一页:用缓存ETag做条件请求
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (page == 1 && !string.IsNullOrEmpty(ifNoneMatch))
            {
                request.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(ifNoneMatch));
            }

            // Retry on transient network errors (TLS EOF, connection reset, etc.)
            // / 对瞬时网络错误重试(TLS EOF,连接重置等)
            using var response = await SendWithRetryAsync(_httpClient, request);

            // 304 Not Modified: no new releases, keep cached ETag / 304未修改:无新release,保留缓存ETag
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                return (newEntries, ifNoneMatch, true);
            }

            // Capture first-page ETag for future conditional requests / 捕获第一页ETag供下次条件请求
            if (page == 1 && response.Headers.ETag is not null)
            {
                firstPageEtag = response.Headers.ETag.Tag;
            }

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            var releases = JsonDocument.Parse(body).RootElement;

            if (releases.GetArrayLength() == 0) break;

            bool foundKnown = false;
            foreach (var release in releases.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString();
                // Reached already-known release, stop / 遇到已知release,停止
                if (tag == sinceTag) { foundKnown = true; break; }

                var entry = BuildEntry(release, sectionFilter);
                if (entry is not null) newEntries.Add(entry);
            }

            if (foundKnown) break;
            if (releases.GetArrayLength() < pageSize) break;
            page++;
        }

        return (newEntries, firstPageEtag, false);
    }

    // Retry HTTP send on transient errors (TLS EOF, connection reset, timeout)
    // / 对瞬时错误重试HTTP发送(TLS EOF,连接重置,超时)
    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client, HttpRequestMessage request, int maxRetries = 3)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                // Clone request on retry since the original may have been disposed
                // / 重试时克隆请求因为原始请求可能已被释放
                using var sendRequest = attempt == 0 ? request : CloneRequest(request);
                return await client.SendAsync(sendRequest);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
            }
        }
    }

    // Check if an HTTP exception is transient (worth retrying)
    // / 检查HTTP异常是否为瞬时的(值得重试)
    private static bool IsTransient(HttpRequestException ex)
    {
        return ex.InnerException is System.IO.IOException
            || ex.InnerException is System.Net.Sockets.SocketException
            || ex.Message.Contains("EOF", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    // Clone an HttpRequestMessage (headers + content + method + URI)
    // / 克隆HttpRequestMessage(头部+内容+方法+URI)
    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    // Build a ChangelogEntry from a GitHub release JSON element, applying section filter
    // / 从GitHub release JSON构建条目,应用章节过滤
    private static ChangelogEntry? BuildEntry(System.Text.Json.JsonElement release, string? sectionFilter)
    {
        var tag = release.GetProperty("tag_name").GetString();
        var publishedAt = release.GetProperty("published_at").GetString();
        var body = release.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : "";
        var isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
        // Read release name (title), fall back to tag when empty / 读取release标题,空时回退到tag
        var releaseName = release.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(releaseName)) releaseName = tag;

        if (tag is null || publishedAt is null) return null;

        if (!string.IsNullOrEmpty(sectionFilter))
        {
            var filteredNotes = ExtractSection(body ?? "", sectionFilter);
            if (filteredNotes is null) return null;

            return new ChangelogEntry
            {
                Version = tag,
                ReleaseName = releaseName ?? tag,
                Date = DateTime.Parse(publishedAt).ToString("yyyy-MM-dd"),
                Notes = NormalizeMarkdown(filteredNotes),
                IsPrerelease = isPrerelease
            };
        }

        return new ChangelogEntry
        {
            Version = tag,
            ReleaseName = releaseName ?? tag,
            Date = DateTime.Parse(publishedAt).ToString("yyyy-MM-dd"),
            Notes = NormalizeMarkdown(body ?? ""),
            IsPrerelease = isPrerelease
        };
    }

    // Parse repo URL supporting "owner/repo" or full GitHub URL
    // / 解析仓库地址，支持"owner/repo"或完整GitHub URL
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
            // Collapse consecutive blanks, emit empty string for whitespace-only
            // / 压缩连续空行,空白行输出空字符串
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
