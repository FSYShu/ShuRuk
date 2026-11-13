using ShuRuk.Runtime.Models;

namespace ShuRuk.Runtime.Contracts;

public interface ITestModeService
{
    bool IsEnabled { get; }
    bool IsFillDataEnabled { get; }
    TestModeCoverage CoverageScope { get; }
    string ChangelogRepoUrl { get; }
    string ChangelogSectionFilter { get; }

    event EventHandler<bool>? TestModeChanged;
    event EventHandler<bool>? FillDataChanged;
    event EventHandler<TestModeCoverage>? CoverageScopeChanged;

    Task InitializeAsync();
    Task SetEnabledAsync(bool enabled);
    Task SetFillDataEnabledAsync(bool enabled);
    Task SetCoverageScopeAsync(TestModeCoverage scope);
    Task SetChangelogRepoUrlAsync(string repoUrl);
    Task SetChangelogSectionFilterAsync(string sectionFilter);

    // Check if a specific page is covered by test mode / 检查某页面是否受测试模式覆盖
    bool IsPageCovered(TestModeCoverage page);
}
