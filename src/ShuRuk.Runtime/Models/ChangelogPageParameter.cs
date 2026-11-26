namespace ShuRuk.Runtime.Models;

/// <summary>
/// Navigation parameter for ChangelogPage / 更新日志页导航参数
/// </summary>
public sealed class ChangelogPageParameter
{
    /// <summary>Repository as "owner/repo" or full GitHub URL / 仓库地址</summary>
    public string RepoUrl { get; set; } = string.Empty;
    /// <summary>Optional Markdown heading title to filter / 可选章节标题过滤，空则显示全部</summary>
    public string? SectionFilter { get; set; }
}
